using System.Runtime.InteropServices;
using Point = System.Windows.Point;

namespace JL_Nav.Interop;

public class NavButtonClickEventArgs : EventArgs
{
    public required int Hwnd { get; init; }
    public required bool IsForward { get; init; }
    public required Point ScreenPoint { get; init; }
}

/// <summary>
/// Installs a low-level mouse hook (WH_MOUSE_LL) to catch right-clicks anywhere
/// on screen — this runs in our own process and sees system-wide input without
/// needing to inject code into explorer.exe. When a right-click lands on a
/// tracked Explorer window's Back/Forward button, the click is swallowed (so
/// Explorer's own history flyout never appears) and NavButtonRightClicked fires
/// instead. Must be started from the WPF UI thread, which pumps the messages
/// this hook relies on.
/// </summary>
public sealed class NavButtonClickWatcher : IDisposable
{
    private readonly LowLevelMouseProc _proc;
    private readonly Func<IEnumerable<int>> _trackedHwnds;
    private IntPtr _hookHandle;

    public event EventHandler<NavButtonClickEventArgs>? NavButtonRightClicked;

    public NavButtonClickWatcher(Func<IEnumerable<int>> trackedHwnds)
    {
        _trackedHwnds = trackedHwnds;
        _proc = HookCallback; // keep a live reference so the GC never collects it out from under native code
    }

    public void Start()
    {
        _hookHandle = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _proc, IntPtr.Zero, 0);
    }

    public void Stop()
    {
        if (_hookHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam.ToInt32() == NativeMethods.WM_RBUTTONDOWN)
        {
            var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
            var hwndUnderCursor = NativeMethods.WindowFromPoint(data.pt);
            var root = NativeMethods.GetAncestor(hwndUnderCursor, NativeMethods.GA_ROOT);
            var rootHwnd = root.ToInt32();

            // Cheap check first — only do the UI Automation lookup for windows we
            // actually track, so right-clicks everywhere else stay fast.
            if (rootHwnd != 0 && _trackedHwnds().Contains(rootHwnd))
            {
                var hit = ExplorerNavButtons.HitTest(root, data.pt);
                if (hit is { } isForward)
                {
                    NavButtonRightClicked?.Invoke(this, new NavButtonClickEventArgs
                    {
                        Hwnd = rootHwnd,
                        IsForward = isForward,
                        ScreenPoint = new Point(data.pt.X, data.pt.Y)
                    });
                    return (IntPtr)1; // swallow: Explorer never sees this click
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose() => Stop();
}
