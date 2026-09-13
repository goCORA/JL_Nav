namespace JL_Nav.History;

/// <summary>
/// Tracks the branching navigation history for a single Explorer window (one HWND).
/// Revisiting a node already reachable from Current (an ancestor, or a previously
/// visited child) moves Current there instead of duplicating it. Navigating
/// somewhere new from a non-leaf node creates a sibling branch, leaving the
/// original branch intact underneath its parent.
/// </summary>
public class WindowHistory
{
    public int Hwnd { get; }
    public HistoryNode Root { get; }
    public HistoryNode Current { get; private set; }

    public WindowHistory(int hwnd, string url, string displayName)
    {
        Hwnd = hwnd;
        Root = Current = new HistoryNode { Url = url, DisplayName = displayName };
    }

    /// <summary>Adopts a previously persisted tree, e.g. when a window that was already
    /// open survives an app restart and is matched back to its saved snapshot.</summary>
    public WindowHistory(int hwnd, HistoryNode root, HistoryNode current)
    {
        Hwnd = hwnd;
        Root = root;
        Current = current;
    }

    public HistoryNode RecordNavigation(string url, string displayName)
    {
        var key = UrlNormalizer.Normalize(url);

        if (UrlNormalizer.Normalize(Current.Url) == key)
            return Current;

        // Explorer sometimes fires a second navigation that just resolves the "real"
        // URL for a folder it only just opened (e.g. a quick-access/special folder
        // resolving to its file path), with no user action in between. As long as
        // nothing has branched off Current yet, treat that as refining this node's
        // URL rather than recording it as a separate hop.
        if (Current.Children.Count == 0 && Current.DisplayName == displayName)
        {
            Current.Url = url;
            return Current;
        }

        var existingChild = Current.Children.FirstOrDefault(c => UrlNormalizer.Normalize(c.Url) == key);
        if (existingChild is not null)
        {
            Current = existingChild;
            return Current;
        }

        var ancestor = FindAncestor(Current, key);
        if (ancestor is not null)
        {
            Current = ancestor;
            return Current;
        }

        var node = new HistoryNode
        {
            Url = url,
            DisplayName = displayName,
            Parent = Current
        };
        Current.Children.Add(node);
        Current = node;
        return node;
    }

    private static HistoryNode? FindAncestor(HistoryNode from, string normalizedKey)
    {
        for (var n = from.Parent; n is not null; n = n.Parent)
        {
            if (UrlNormalizer.Normalize(n.Url) == normalizedKey)
                return n;
        }
        return null;
    }
}
