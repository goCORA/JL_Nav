using System.Windows.Automation;

namespace JL_Nav.Interop;

using JL_Nav;

/// <summary>
/// Locates an Explorer window's Back/Forward buttons via UI Automation
/// (AutomationId "backButton"/"forwardButton" — confirmed stable across this
/// build's windows) and hit-tests a screen point against them. Falls back to
/// ControlType.Button + accessible name ("Back"/"Forward") if a future
/// Explorer update ever renames or drops those AutomationIds, so we're not
/// depending on a single identifier.
/// </summary>
internal static class ExplorerNavButtons
{
    // Logged at most once per process run — a right-click retries this lookup
    // every time, and we don't want a broken build spamming the log forever.
    private static bool _loggedNotFound;

    /// <returns>true if the point hit Forward, false if it hit Back, null if neither.</returns>
    public static bool? HitTest(IntPtr rootHwnd, POINT screenPoint)
    {
        try
        {
            var root = AutomationElement.FromHandle(rootHwnd);
            if (root is null)
                return null;

            var back = FindButton(root, "backButton", "Back");
            if (back is not null && Contains(back.Current.BoundingRectangle, screenPoint))
                return false;

            var forward = FindButton(root, "forwardButton", "Forward");
            if (forward is not null && Contains(forward.Current.BoundingRectangle, screenPoint))
                return true;

            if (back is null && forward is null && !_loggedNotFound)
            {
                _loggedNotFound = true;
                Diagnostics.Log("ExplorerNavButtons: navigation buttons not found (AutomationId and ControlType/Name fallback both failed)");
            }

            return null;
        }
        catch (Exception ex)
        {
            Diagnostics.LogException("ExplorerNavButtons.HitTest failed", ex);
            return null;
        }
    }

    private static AutomationElement? FindButton(AutomationElement root, string automationId, string accessibleName)
    {
        var byId = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, automationId));
        if (byId is not null)
            return byId;

        var byNameCondition = new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
            new PropertyCondition(AutomationElement.NameProperty, accessibleName));
        return root.FindFirst(TreeScope.Descendants, byNameCondition);
    }

    private static bool Contains(System.Windows.Rect rect, POINT pt) =>
        pt.X >= rect.Left && pt.X <= rect.Right && pt.Y >= rect.Top && pt.Y <= rect.Bottom;
}
