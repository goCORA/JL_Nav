using System.Windows.Automation;

namespace JL_Nav.Interop;

using JL_Nav;

/// <summary>
/// Locates an Explorer window's Back/Forward buttons via UI Automation
/// (AutomationId "backButton"/"forwardButton" — confirmed stable across this
/// build's windows) and hit-tests a screen point against them.
/// </summary>
internal static class ExplorerNavButtons
{
    /// <returns>true if the point hit Forward, false if it hit Back, null if neither.</returns>
    public static bool? HitTest(IntPtr rootHwnd, POINT screenPoint)
    {
        try
        {
            var root = AutomationElement.FromHandle(rootHwnd);
            if (root is null)
                return null;

            var back = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "backButton"));
            if (back is not null && Contains(back.Current.BoundingRectangle, screenPoint))
                return false;

            var forward = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.AutomationIdProperty, "forwardButton"));
            if (forward is not null && Contains(forward.Current.BoundingRectangle, screenPoint))
                return true;

            return null;
        }
        catch (Exception ex)
        {
            Diagnostics.LogException("ExplorerNavButtons.HitTest failed", ex);
            return null;
        }
    }

    private static bool Contains(System.Windows.Rect rect, POINT pt) =>
        pt.X >= rect.Left && pt.X <= rect.Right && pt.Y >= rect.Top && pt.Y <= rect.Bottom;
}
