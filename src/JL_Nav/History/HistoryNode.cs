using System.Text.Json.Serialization;

namespace JL_Nav.History;

public class HistoryNode
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Url { get; set; }
    public required string DisplayName { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;

    [JsonIgnore]
    public HistoryNode? Parent { get; set; }

    public List<HistoryNode> Children { get; init; } = new();
}
