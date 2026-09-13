using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Forms;
using JL_Nav.History;
using JL_Nav.Interop;
using Application = System.Windows.Application;
using MouseButtons = System.Windows.Forms.MouseButtons;
using MouseEventArgs = System.Windows.Forms.MouseEventArgs;
using Point = System.Windows.Point;

namespace JL_Nav;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private NotifyIcon? _trayIcon;
    private HistoryManager? _historyManager;
    private NavButtonClickWatcher? _navButtonWatcher;
    private HistoryPopupWindow? _popup;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, "JL_Nav_SingleInstance", out var createdNew);
        if (!createdNew)
        {
            System.Windows.MessageBox.Show(
                "JL_Nav is already running — check your system tray.",
                "JL_Nav", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }
        _ownsSingleInstanceMutex = true;

        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        DispatcherUnhandledException += (_, args) =>
        {
            Diagnostics.LogException("DispatcherUnhandledException", args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Diagnostics.LogException("AppDomain.UnhandledException", args.ExceptionObject as Exception);
        };

        _historyManager = new HistoryManager();
        _historyManager.Start();

        _navButtonWatcher = new NavButtonClickWatcher(() => _historyManager.Histories.Keys);
        _navButtonWatcher.NavButtonRightClicked += (_, args) => ShowPopupForWindow(args.Hwnd, args.ScreenPoint);
        _navButtonWatcher.Start();

        var icon = Environment.ProcessPath is { } path
            ? Icon.ExtractAssociatedIcon(path)
            : null;

        _trayIcon = new NotifyIcon
        {
            Icon = icon ?? SystemIcons.Application,
            Visible = true,
            Text = "JL_Nav — Explorer history"
        };
        _trayIcon.Click += TrayIcon_Click;

        var startWithWindowsItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = StartupManager.IsEnabled,
            CheckOnClick = true
        };
        startWithWindowsItem.Click += (_, _) => StartupManager.SetEnabled(startWithWindowsItem.Checked);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show history", null, (_, _) => ShowPopup());
        menu.Items.Add(startWithWindowsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        _trayIcon.ContextMenuStrip = menu;
    }

    private void TrayIcon_Click(object? sender, EventArgs e)
    {
        if (e is MouseEventArgs { Button: MouseButtons.Left })
            ShowPopup();
    }

    private void ShowPopup()
    {
        if (_historyManager is null)
            return;

        _popup?.Close();
        _popup = new HistoryPopupWindow(_historyManager);
        _popup.ShowAtCursor();
    }

    private void ShowPopupForWindow(int hwnd, Point screenPoint)
    {
        if (_historyManager is null)
            return;

        _popup?.Close();
        _popup = new HistoryPopupWindow(_historyManager, hwnd);
        _popup.ShowAt(screenPoint);
    }

    private void ExitApp()
    {
        _trayIcon!.Visible = false;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _navButtonWatcher?.Dispose();
        _historyManager?.Dispose();

        if (_ownsSingleInstanceMutex)
            _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();

        base.OnExit(e);
    }
}
