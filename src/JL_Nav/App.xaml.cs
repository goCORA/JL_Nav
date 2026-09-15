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
using Windows.ApplicationModel;

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
            CheckOnClick = true
        };
        startWithWindowsItem.Click += async (_, _) =>
        {
            var wantEnabled = startWithWindowsItem.Checked;
            try
            {
                var state = await StartupManager.SetEnabledAsync(wantEnabled);
                ApplyStartupState(startWithWindowsItem, state);
            }
            catch (Exception ex)
            {
                Diagnostics.LogException("StartupManager.SetEnabledAsync failed", ex);
                startWithWindowsItem.Checked = !wantEnabled; // revert the optimistic checkbox flip
            }
        };
        _ = InitializeStartupMenuItemAsync(startWithWindowsItem);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show history", null, (_, _) => ShowPopup());
        menu.Items.Add(startWithWindowsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Open diagnostics log", null, (_, _) => Diagnostics.OpenLog());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());
        _trayIcon.ContextMenuStrip = menu;
    }

    private static async Task InitializeStartupMenuItemAsync(ToolStripMenuItem item)
    {
        try
        {
            ApplyStartupState(item, await StartupManager.GetStateAsync());
        }
        catch (Exception ex)
        {
            Diagnostics.LogException("StartupManager.GetStateAsync failed", ex);
        }
    }

    private static void ApplyStartupState(ToolStripMenuItem item, StartupTaskState state)
    {
        item.Checked = state is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;

        // The user turned this off from Windows Settings/Task Manager directly — we can't
        // re-enable it in code, so disable the menu item instead of letting it silently fail.
        item.Enabled = state != StartupTaskState.DisabledByUser;
        item.Text = state == StartupTaskState.DisabledByUser
            ? "Start with Windows (turned off in Windows Settings)"
            : "Start with Windows";
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
