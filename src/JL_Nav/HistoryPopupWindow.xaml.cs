using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using JL_Nav.History;
using JL_Nav.Interop;
using JL_Nav.Visualization;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;

namespace JL_Nav;

public partial class HistoryPopupWindow : Window
{
    private readonly HistoryManager _historyManager;
    private bool _closing;

    /// <param name="focusHwnd">When set, shows only this window's tree (no header,
    /// no closed windows) — used when triggered from that window's own Back/Forward
    /// button rather than the tray icon.</param>
    public HistoryPopupWindow(HistoryManager historyManager, int? focusHwnd = null)
    {
        InitializeComponent();
        _historyManager = historyManager;

        if (focusHwnd is { } hwnd)
        {
            if (_historyManager.Histories.TryGetValue(hwnd, out var focused))
            {
                TreesPanel.Children.Add(BranchTreeRenderer.Render(focused.Root, focused.Current.Id, node =>
                {
                    _historyManager.NavigateWindowTo(hwnd, node.Url);
                    CloseOnce();
                }));
            }
            return;
        }

        // Context-menu actions (close/delete/clear) don't close this popup, so it
        // needs to stay in sync on its own — including the delayed case where
        // "Close window" only actually disappears once Explorer's real close is
        // detected, which arrives asynchronously via this event.
        _historyManager.Changed += OnHistoryChanged;
        Closed += (_, _) => _historyManager.Changed -= OnHistoryChanged;

        BuildTrees();
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        // This can arrive from a COM callback (Explorer's OnQuit) nested inside a
        // still-closing ContextMenu's own message loop. Rebuilding TreesPanel right
        // there updates the data fine, but WPF doesn't flush the repaint until that
        // nested loop unwinds, leaving stale pixels on screen until something else
        // forces a redraw. Deferring to a fresh dispatcher frame avoids that.
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(BuildTrees));
    }

    /// <summary>(Re)populates TreesPanel from the manager's current state.</summary>
    private void BuildTrees()
    {
        TreesPanel.Children.Clear();

        if (_historyManager.Histories.Count == 0 && _historyManager.ClosedHistories.Count == 0)
        {
            TreesPanel.Children.Add(new TextBlock
            {
                Text = "No Explorer windows detected yet.",
                Foreground = Brushes.Gray,
                Margin = new Thickness(4)
            });
            return;
        }

        if (_historyManager.Histories.Count > 0)
        {
            AddSectionHeader("Open windows");

            foreach (var history in _historyManager.Histories.Values)
            {
                var openHwnd = history.Hwnd;

                var closeItem = new MenuItem { Header = "Close window" };
                closeItem.Click += (_, _) => _historyManager.CloseWindow(openHwnd);

                var closeWithoutSavingItem = new MenuItem { Header = "Close without saving Nodes" };
                closeWithoutSavingItem.Click += (_, _) => _historyManager.CloseWindowWithoutSaving(openHwnd);

                var rowMenu = CreateRowContextMenu(closeItem, closeWithoutSavingItem);
                AddWindowHeader($"Window {openHwnd}", rowMenu);

                var tree = BranchTreeRenderer.Render(history.Root, history.Current.Id, node =>
                {
                    _historyManager.NavigateWindowTo(openHwnd, node.Url);
                    CloseOnce();
                });
                tree.ContextMenu = rowMenu;
                TreesPanel.Children.Add(tree);
            }
        }

        if (_historyManager.ClosedHistories.Count > 0)
        {
            var clearAllItem = new MenuItem { Header = "Clear all Closed Nodes" };
            clearAllItem.Click += (_, _) => _historyManager.ClearClosedHistories();
            AddSectionHeader("Closed windows (click a node to reopen there)", CreateRowContextMenu(clearAllItem));

            foreach (var entry in _historyManager.ClosedHistories)
            {
                var deleteItem = new MenuItem { Header = "Delete" };
                deleteItem.Click += (_, _) => _historyManager.RemoveClosedHistory(entry);

                var rowMenu = CreateRowContextMenu(deleteItem);
                AddWindowHeader($"Window {entry.Hwnd} — closed {entry.ClosedAt:t}", rowMenu);

                var tree = BranchTreeRenderer.Render(entry.Root, entry.CurrentNodeId, node =>
                {
                    _historyManager.ReopenClosedWindow(entry, node.Url);
                    CloseOnce();
                });
                tree.ContextMenu = rowMenu;
                TreesPanel.Children.Add(tree);
            }
        }
    }

    private void AddSectionHeader(string text, ContextMenu? contextMenu = null) => TreesPanel.Children.Add(new TextBlock
    {
        Text = text,
        FontWeight = FontWeights.Bold,
        Margin = new Thickness(4, 12, 4, 4),
        Background = Brushes.Transparent,
        ContextMenu = contextMenu
    });

    private void AddWindowHeader(string text, ContextMenu? contextMenu = null) => TreesPanel.Children.Add(new TextBlock
    {
        Text = text,
        FontWeight = FontWeights.SemiBold,
        FontSize = 11,
        Foreground = Brushes.DimGray,
        Margin = new Thickness(4, 6, 4, 2),
        Background = Brushes.Transparent, // makes the whole row width right-clickable, not just under the glyphs
        ContextMenu = contextMenu
    });

    /// <summary>Builds a ContextMenu shared by a row's header and its tree canvas,
    /// so right-clicking anywhere on the row — the label or the branch diagram
    /// itself — shows the same menu.</summary>
    private static ContextMenu CreateRowContextMenu(params MenuItem[] items)
    {
        var menu = new ContextMenu();
        foreach (var item in items)
            menu.Items.Add(item);

        // A ContextMenu's own popup doesn't inherit Topmost from its owner window,
        // so on a Topmost="True" window (like this popup) it renders invisibly
        // behind it unless explicitly pushed above via SetWindowPos.
        menu.Opened += (_, _) =>
        {
            if (PresentationSource.FromVisual(menu) is HwndSource hwndSource)
            {
                NativeMethods.SetWindowPos(hwndSource.Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
            }
        };

        return menu;
    }

    public void ShowAtCursor()
    {
        var cursorPos = System.Windows.Forms.Cursor.Position;
        ShowAt(new Point(cursorPos.X, cursorPos.Y));
    }

    public void ShowAt(Point screenPoint)
    {
        Show();
        UpdateLayout();

        var dpi = VisualTreeHelper.GetDpi(this);
        var widthPx = ActualWidth * dpi.DpiScaleX;
        var heightPx = ActualHeight * dpi.DpiScaleY;

        var workingArea = System.Windows.Forms.Screen
            .FromPoint(new System.Drawing.Point((int)screenPoint.X, (int)screenPoint.Y))
            .WorkingArea;

        var leftPx = Clamp(screenPoint.X - widthPx / 2, workingArea.Left, workingArea.Right - widthPx);
        var topPx = Clamp(screenPoint.Y - heightPx - 10, workingArea.Top, workingArea.Bottom - heightPx);

        Left = leftPx / dpi.DpiScaleX;
        Top = topPx / dpi.DpiScaleY;
        Activate();
    }

    private static double Clamp(double value, double min, double max) =>
        Math.Max(min, Math.Min(value, max));

    private void Window_Deactivated(object? sender, EventArgs e) => CloseOnce();

    private void CloseOnce()
    {
        if (_closing)
            return;

        _closing = true;
        Close();
    }
}
