using System.IO;
using System.Text.Json;

namespace JL_Nav.History;

/// <summary>
/// A window's tree as of some point in time. For a still-open window this is a
/// running snapshot (ClosedAt is null); for a closed one it's the final state
/// (ClosedAt records when). CurrentNodeId records which node was active, for
/// highlighting and for re-adopting the tree if the window survives a restart.
/// </summary>
public class PersistedWindowHistory
{
    public int Hwnd { get; set; }
    public DateTime? ClosedAt { get; set; }
    public required HistoryNode Root { get; set; }
    public Guid CurrentNodeId { get; set; }
}

public class PersistedState
{
    public List<PersistedWindowHistory> OpenSnapshots { get; set; } = new();
    public List<PersistedWindowHistory> ClosedHistories { get; set; } = new();
}

/// <summary>
/// Persists all window histories to disk on every change, so both live and
/// closed trees survive an app restart (open ones are re-adopted by matching
/// the window's current folder against the saved tree; see HistoryManager).
/// </summary>
public static class HistoryStore
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "JL_Nav", "state.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static PersistedState Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new PersistedState();

            var json = File.ReadAllText(FilePath);
            var state = JsonSerializer.Deserialize<PersistedState>(json, Options) ?? new PersistedState();

            foreach (var entry in state.OpenSnapshots)
                FixParents(entry.Root, null);
            foreach (var entry in state.ClosedHistories)
                FixParents(entry.Root, null);

            return state;
        }
        catch (Exception ex)
        {
            Diagnostics.LogException("HistoryStore.Load", ex);
            return new PersistedState();
        }
    }

    public static void Save(PersistedState state)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var json = JsonSerializer.Serialize(state, Options);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Diagnostics.LogException("HistoryStore.Save", ex);
        }
    }

    private static void FixParents(HistoryNode node, HistoryNode? parent)
    {
        node.Parent = parent;
        foreach (var child in node.Children)
            FixParents(child, node);
    }
}
