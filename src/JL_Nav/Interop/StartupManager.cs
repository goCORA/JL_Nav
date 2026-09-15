using Windows.ApplicationModel;

namespace JL_Nav.Interop;

/// <summary>
/// Toggles launching JL_Nav at Windows login via the packaged app's startup task
/// (declared in Package.appxmanifest as TaskId "JL_NavStartupTask") — off by default,
/// no admin rights needed. Replaces the old per-user Run-key approach: MSIX packaging
/// virtualizes registry writes, so Windows won't honor a Run-key entry for login startup
/// from a packaged app.
/// </summary>
internal static class StartupManager
{
    private const string TaskId = "JL_NavStartupTask";

    public static async Task<StartupTaskState> GetStateAsync()
    {
        var task = await StartupTask.GetAsync(TaskId);
        return task.State;
    }

    /// <returns>The resulting state, so the caller can reflect what actually happened
    /// rather than assuming the request succeeded.</returns>
    public static async Task<StartupTaskState> SetEnabledAsync(bool enabled)
    {
        var task = await StartupTask.GetAsync(TaskId);

        if (!enabled)
        {
            task.Disable();
            return StartupTaskState.Disabled;
        }

        return await task.RequestEnableAsync();
    }
}
