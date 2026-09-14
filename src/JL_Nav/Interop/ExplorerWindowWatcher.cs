using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace JL_Nav.Interop;

using JL_Nav;

public class ExplorerNavigationEventArgs : EventArgs
{
    public required int Hwnd { get; init; }
    public required string Url { get; init; }
    public required string DisplayName { get; init; }
}

public class ExplorerWindowClosedEventArgs : EventArgs
{
    public required int Hwnd { get; init; }
}

/// <summary>
/// Enumerates open Explorer windows via the Shell.Application COM automation object
/// and hooks each one's DWebBrowserEvents2 connection point (through
/// System.Runtime.InteropServices.ComEventsHelper, which drives the
/// IConnectionPointContainer/Advise dance for us) to get real navigation events
/// instead of polling for URL changes.
///
/// New/closed top-level Explorer windows are still detected by a lightweight
/// periodic rescan, since Shell.Application does not push "window opened" events
/// to us the way DWebBrowserEvents2 pushes navigation.
/// </summary>
public sealed class ExplorerWindowWatcher : IDisposable
{
    private readonly DispatcherTimer _scanTimer;
    private readonly Dictionary<int, TrackedWindow> _tracked = new();
    private dynamic? _shellApp;

    public event EventHandler<ExplorerNavigationEventArgs>? WindowOpened;
    public event EventHandler<ExplorerNavigationEventArgs>? Navigated;
    public event EventHandler<ExplorerWindowClosedEventArgs>? WindowClosed;

    private sealed class TrackedWindow
    {
        public required object Rcw;
        public required NavigateComplete2Handler NavigateComplete2;
        public required OnQuitHandler OnQuit;
    }

    public ExplorerWindowWatcher()
    {
        _scanTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _scanTimer.Tick += (_, _) => Scan();
    }

    public void Start()
    {
        var type = Type.GetTypeFromProgID("Shell.Application")
            ?? throw new InvalidOperationException("Shell.Application COM type not found.");
        _shellApp = Activator.CreateInstance(type);

        Scan();
        _scanTimer.Start();
    }

    public void Stop()
    {
        _scanTimer.Stop();
        // Just unhook COM events here — these windows aren't actually closing,
        // we're only tearing down because the app itself is shutting down.
        // Raising WindowClosed for them would make HistoryManager archive their
        // trees as "closed" and drop them from the open-snapshot set that gets
        // matched back up on the next app start, losing their history.
        foreach (var hwnd in _tracked.Keys.ToList())
            Untrack(hwnd, notify: false);
    }

    private void Scan()
    {
        if (_shellApp is null)
            return;

        dynamic windows;
        try
        {
            windows = _shellApp.Windows();
        }
        catch (COMException)
        {
            return;
        }

        var seen = new HashSet<int>();

        foreach (dynamic window in windows)
        {
            string fullName;
            int hwnd;
            try
            {
                fullName = (string)window.FullName;
                hwnd = (int)window.HWND;
            }
            catch
            {
                continue; // window may have closed mid-enumeration
            }

            if (!fullName.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase))
                continue;

            seen.Add(hwnd);

            if (!_tracked.ContainsKey(hwnd))
                Track(hwnd, window);
        }

        foreach (var hwnd in _tracked.Keys.ToList())
        {
            // Shell.Application's Windows() collection can keep a stale entry for a
            // window that's already been destroyed — most often one closed quickly
            // (e.g. the active window, via the X button or Alt+F4) — so a hwnd it
            // still reports as "seen" isn't proof the window is actually still
            // there. Cross-check against the real window handle so we don't leave
            // a closed window stuck looking "open" forever.
            if (!seen.Contains(hwnd) || !NativeMethods.IsWindow((IntPtr)hwnd))
                Untrack(hwnd);
        }
    }

    private void Track(int hwnd, object rcw)
    {
        NavigateComplete2Handler navigateComplete2 = (object pDisp, ref object url) =>
        {
            RaiseNavigated(hwnd, url as string ?? "");
        };
        OnQuitHandler onQuit = () => Untrack(hwnd);

        ComEventsHelper.Combine(rcw, WebBrowserEventIds.DWebBrowserEvents2, WebBrowserEventIds.NavigateComplete2, navigateComplete2);
        ComEventsHelper.Combine(rcw, WebBrowserEventIds.DWebBrowserEvents2, WebBrowserEventIds.OnQuit, onQuit);

        _tracked[hwnd] = new TrackedWindow
        {
            Rcw = rcw,
            NavigateComplete2 = navigateComplete2,
            OnQuit = onQuit
        };

        dynamic dyn = rcw;
        var url = TryGetString(() => (string)dyn.LocationURL);
        var name = TryGetString(() => (string)dyn.LocationName);

        WindowOpened?.Invoke(this, new ExplorerNavigationEventArgs { Hwnd = hwnd, Url = url, DisplayName = name });
    }

    private void Untrack(int hwnd, bool notify = true)
    {
        if (!_tracked.Remove(hwnd, out var tracked))
            return;

        try
        {
            ComEventsHelper.Remove(tracked.Rcw, WebBrowserEventIds.DWebBrowserEvents2, WebBrowserEventIds.NavigateComplete2, tracked.NavigateComplete2);
            ComEventsHelper.Remove(tracked.Rcw, WebBrowserEventIds.DWebBrowserEvents2, WebBrowserEventIds.OnQuit, tracked.OnQuit);
        }
        catch
        {
            // COM object is already gone; nothing to unhook.
        }

        if (notify)
            WindowClosed?.Invoke(this, new ExplorerWindowClosedEventArgs { Hwnd = hwnd });
    }

    private void RaiseNavigated(int hwnd, string url)
    {
        var name = "";
        if (_tracked.TryGetValue(hwnd, out var tracked))
            name = TryGetString(() => (string)((dynamic)tracked.Rcw).LocationName);

        Navigated?.Invoke(this, new ExplorerNavigationEventArgs { Hwnd = hwnd, Url = url, DisplayName = name });
    }

    private static string TryGetString(Func<string> getter)
    {
        try { return getter(); }
        catch { return ""; }
    }

    public void NavigateWindow(int hwnd, string url)
    {
        if (!_tracked.TryGetValue(hwnd, out var tracked))
            return;

        try
        {
            dynamic dyn = tracked.Rcw;
            dyn.Navigate2(url);
        }
        catch (Exception ex)
        {
            Diagnostics.LogException($"Navigate2 failed for hwnd={hwnd}", ex);
            return;
        }

        var hWndPtr = new IntPtr(hwnd);
        if (NativeMethods.IsIconic(hWndPtr))
            NativeMethods.ShowWindow(hWndPtr, NativeMethods.SW_RESTORE);
        NativeMethods.SetForegroundWindow(hWndPtr);
    }

    public void Dispose() => Stop();
}
