namespace JL_Nav.Interop;

/// <summary>
/// GUID and DISPIDs for the DWebBrowserEvents2 dispinterface (exdispid.h), and the
/// matching delegate shapes, hand-declared so we can hook Explorer's navigation
/// events via ComEventsHelper without needing a tlbimp-generated interop assembly
/// (SHDocVw) or the Windows SDK to build.
/// </summary>
internal static class WebBrowserEventIds
{
    public static readonly Guid DWebBrowserEvents2 = new("34A715A0-6587-11D0-924A-0020AFC7AC4D");

    public const int BeforeNavigate2 = 250;
    public const int NavigateComplete2 = 252;
    public const int OnQuit = 253;
    public const int DocumentComplete = 259;
}

public delegate void BeforeNavigate2Handler(
    object pDisp, ref object url, ref object flags,
    ref object targetFrameName, ref object postData, ref object headers, ref bool cancel);

public delegate void NavigateComplete2Handler(object pDisp, ref object url);

public delegate void DocumentCompleteHandler(object pDisp, ref object url);

public delegate void OnQuitHandler();
