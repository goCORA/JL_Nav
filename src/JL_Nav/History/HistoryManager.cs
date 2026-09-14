using System.Diagnostics;
using JL_Nav.Interop;

namespace JL_Nav.History;

using JL_Nav;

/// <summary>
/// Central application-level state: owns the Explorer watcher, the per-window
/// branching histories built from its events, and the archive of trees left
/// behind by windows that have since closed. Outlives any individual popup window.
///
/// Every change is persisted to disk (see HistoryStore), including live windows'
/// trees — not just closed ones — so that if the app restarts while a window is
/// still open, its tree isn't lost: the window is matched back to its saved
/// snapshot by its current folder the next time it's (re)detected.
/// </summary>
public sealed class HistoryManager : IDisposable
{
    private const int MaxClosedHistories = 20;

    private readonly ExplorerWindowWatcher _watcher = new();
    private readonly Dictionary<int, WindowHistory> _histories = new();
    private readonly PersistedState _state;

    /// <summary>Snapshots loaded from disk that haven't yet been matched to a live
    /// window. Strictly separate from _state.OpenSnapshots (what gets written to
    /// disk, always rebuilt fresh from _histories) — mixing the two caused every
    /// save to re-append the whole live set on top of itself.</summary>
    private readonly List<PersistedWindowHistory> _pendingAdoption;

    public event EventHandler? Changed;

    public IReadOnlyDictionary<int, WindowHistory> Histories => _histories;
    public IReadOnlyList<PersistedWindowHistory> ClosedHistories => _state.ClosedHistories;

    public HistoryManager()
    {
        var loaded = HistoryStore.Load();
        _pendingAdoption = loaded.OpenSnapshots;
        _state = new PersistedState { ClosedHistories = loaded.ClosedHistories };

        _watcher.WindowOpened += (_, e) =>
        {
            _histories[e.Hwnd] = AdoptSnapshot(e.Hwnd, e.Url) ?? new WindowHistory(e.Hwnd, e.Url, e.DisplayName);
            PersistState();
            Changed?.Invoke(this, EventArgs.Empty);
        };

        _watcher.Navigated += (_, e) =>
        {
            if (!_histories.TryGetValue(e.Hwnd, out var history))
            {
                history = AdoptSnapshot(e.Hwnd, e.Url) ?? new WindowHistory(e.Hwnd, e.Url, e.DisplayName);
                _histories[e.Hwnd] = history;
            }

            history.RecordNavigation(e.Url, e.DisplayName);
            PersistState();
            Changed?.Invoke(this, EventArgs.Empty);
        };

        _watcher.WindowClosed += (_, e) =>
        {
            if (_histories.Remove(e.Hwnd, out var history))
                ArchiveClosedWindow(history);
            else
                PersistState();

            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    public void Start() => _watcher.Start();

    public void NavigateWindowTo(int hwnd, string url) => _watcher.NavigateWindow(hwnd, url);

    /// <summary>
    /// Reopens a closed window's node in a new Explorer window, and retires the
    /// archived entry — once it's live again, it belongs under "Open windows",
    /// not lingering in "Closed windows" too.
    /// </summary>
    public void ReopenClosedWindow(PersistedWindowHistory entry, string url)
    {
        _state.ClosedHistories.Remove(entry);

        // Feed the archived tree into the same pending-adoption queue used for
        // restart recovery, so once the new window is detected at this URL it
        // inherits the full history instead of starting from a blank node.
        _pendingAdoption.Add(entry);
        PersistState();

        OpenNewWindow(url);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Closes the real Explorer window, same as clicking its own X button — the
    /// existing WindowClosed handling picks this up and archives it normally.
    /// </summary>
    public void CloseWindow(int hwnd) =>
        NativeMethods.PostMessage((IntPtr)hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

    /// <summary>
    /// Closes the real Explorer window without archiving its tree — the live
    /// history is dropped up front so the WindowClosed event that arrives once
    /// the window actually closes finds nothing left to archive.
    /// </summary>
    public void CloseWindowWithoutSaving(int hwnd)
    {
        if (_histories.Remove(hwnd))
        {
            PersistState();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        CloseWindow(hwnd);
    }

    /// <summary>Removes a closed-window entry from the archive permanently.</summary>
    public void RemoveClosedHistory(PersistedWindowHistory entry)
    {
        _state.ClosedHistories.Remove(entry);
        PersistState();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Empties the entire closed-windows archive.</summary>
    public void ClearClosedHistories()
    {
        _state.ClosedHistories.Clear();
        PersistState();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Looks for a saved snapshot (open or closed) whose tree contains a node at
    /// the window's current URL, and adopts that tree/node as this window's
    /// history instead of starting fresh — used both when a window survives an
    /// app restart and, incidentally, when a reopened closed window happens to
    /// land back on one of its own remembered paths.
    /// </summary>
    private WindowHistory? AdoptSnapshot(int hwnd, string currentUrl)
    {
        for (var i = 0; i < _pendingAdoption.Count; i++)
        {
            var snapshot = _pendingAdoption[i];
            var match = FindNodeByUrl(snapshot.Root, currentUrl);
            if (match is not null)
            {
                _pendingAdoption.RemoveAt(i);
                return new WindowHistory(hwnd, snapshot.Root, match);
            }
        }

        return null;
    }

    private static HistoryNode? FindNodeByUrl(HistoryNode node, string url)
    {
        var key = UrlNormalizer.Normalize(url);
        return FindNodeByNormalizedUrl(node, key);
    }

    private static HistoryNode? FindNodeByNormalizedUrl(HistoryNode node, string normalizedKey)
    {
        if (UrlNormalizer.Normalize(node.Url) == normalizedKey)
            return node;

        foreach (var child in node.Children)
        {
            var found = FindNodeByNormalizedUrl(child, normalizedKey);
            if (found is not null)
                return found;
        }

        return null;
    }

    private void OpenNewWindow(string url)
    {
        if (url.Length == 0)
            return;

        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{url}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Diagnostics.LogException($"OpenNewWindow failed for url={url}", ex);
        }
    }

    private void ArchiveClosedWindow(WindowHistory history)
    {
        _state.ClosedHistories.Insert(0, new PersistedWindowHistory
        {
            Hwnd = history.Hwnd,
            ClosedAt = DateTime.Now,
            Root = history.Root,
            CurrentNodeId = history.Current.Id
        });

        while (_state.ClosedHistories.Count > MaxClosedHistories)
            _state.ClosedHistories.RemoveAt(_state.ClosedHistories.Count - 1);

        PersistState();
    }

    private void PersistState()
    {
        // Always rebuilt fresh from the live set, plus whatever's still waiting to
        // be matched (e.g. this startup scan hasn't reached that window yet) — never
        // appended onto the previous save, or every navigation would re-save the
        // whole live set on top of itself.
        var snapshots = _histories.Values
            .Select(h => new PersistedWindowHistory { Hwnd = h.Hwnd, Root = h.Root, CurrentNodeId = h.Current.Id })
            .ToList();
        snapshots.AddRange(_pendingAdoption);

        _state.OpenSnapshots = snapshots;
        HistoryStore.Save(_state);
    }

    public void Dispose() => _watcher.Dispose();
}
