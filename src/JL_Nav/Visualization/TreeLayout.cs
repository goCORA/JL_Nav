using JL_Nav.History;

namespace JL_Nav.Visualization;

public class LayoutNode
{
    public required HistoryNode Node { get; init; }
    public required int Column { get; init; }
    public required double Row { get; init; }
}

public class TreeLayoutResult
{
    public required List<LayoutNode> Nodes { get; init; }
    public required int ColumnCount { get; init; }
    public required double RowCount { get; init; }
}

/// <summary>
/// Basic centered tree layout: depth becomes the column, leaves get consecutive
/// rows in traversal order, and each internal node is centered over the row-span
/// of its children. Good enough for the branch counts a folder history produces;
/// no attempt at lane-packing like a full git-graph layout.
/// </summary>
public static class TreeLayout
{
    public static TreeLayoutResult Compute(HistoryNode root)
    {
        var positions = new Dictionary<HistoryNode, (int Column, double Row)>();
        var nextRow = 0;

        double Layout(HistoryNode node, int column)
        {
            double row;
            if (node.Children.Count == 0)
            {
                row = nextRow++;
            }
            else
            {
                var childRows = node.Children.Select(c => Layout(c, column + 1)).ToList();
                row = (childRows.Min() + childRows.Max()) / 2.0;
            }

            positions[node] = (column, row);
            return row;
        }

        Layout(root, 0);

        var nodes = positions
            .Select(kv => new LayoutNode { Node = kv.Key, Column = kv.Value.Column, Row = kv.Value.Row })
            .ToList();

        return new TreeLayoutResult
        {
            Nodes = nodes,
            ColumnCount = nodes.Count == 0 ? 0 : nodes.Max(n => n.Column) + 1,
            RowCount = Math.Max(1, nextRow)
        };
    }
}
