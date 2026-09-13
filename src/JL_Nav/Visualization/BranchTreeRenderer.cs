using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using JL_Nav.History;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Cursors = System.Windows.Input.Cursors;
using Point = System.Windows.Point;

namespace JL_Nav.Visualization;

/// <summary>
/// Draws a WindowHistory's branching tree onto a Canvas: boxes for nodes, curved
/// connector lines for parent/child edges, the current node highlighted. Clicking
/// a node invokes onNodeClicked so the caller can jump the Explorer window there.
/// </summary>
public static class BranchTreeRenderer
{
    private const double ColumnWidth = 190;
    private const double RowHeight = 44;
    private const double NodeWidth = 160;
    private const double NodeHeight = 30;
    private const double Margin = 16;

    private static readonly SolidColorBrush CurrentFill = new(Color.FromRgb(0xF5, 0x8A, 0x1F));
    private static readonly SolidColorBrush CurrentBorder = new(Color.FromRgb(0xD9, 0x6A, 0x00));

    public static FrameworkElement Render(HistoryNode root, Guid currentId, Action<HistoryNode> onNodeClicked)
    {
        var layout = TreeLayout.Compute(root);
        var byNode = layout.Nodes.ToDictionary(n => n.Node, n => n);

        var canvas = new Canvas
        {
            Width = Margin * 2 + layout.ColumnCount * ColumnWidth,
            Height = Margin * 2 + layout.RowCount * RowHeight,
            Background = Brushes.Transparent // makes empty space (not just the node boxes) hit-testable, e.g. for right-click
        };

        Point Center(LayoutNode n) => new(
            Margin + n.Column * ColumnWidth + NodeWidth / 2,
            Margin + n.Row * RowHeight + NodeHeight / 2);

        foreach (var parent in layout.Nodes)
        {
            foreach (var child in parent.Node.Children)
            {
                canvas.Children.Add(MakeConnector(Center(parent), Center(byNode[child])));
            }
        }

        foreach (var ln in layout.Nodes)
        {
            var node = MakeNode(ln.Node, ln.Node.Id == currentId, onNodeClicked);
            Canvas.SetLeft(node, Margin + ln.Column * ColumnWidth);
            Canvas.SetTop(node, Margin + ln.Row * RowHeight);
            canvas.Children.Add(node);
        }

        return canvas;
    }

    private static Path MakeConnector(Point p1, Point p2)
    {
        var start = new Point(p1.X + NodeWidth / 2, p1.Y);
        var end = new Point(p2.X - NodeWidth / 2, p2.Y);
        var reach = ColumnWidth / 3;

        var figure = new PathFigure(
            start,
            new PathSegment[]
            {
                new BezierSegment(
                    new Point(start.X + reach, start.Y),
                    new Point(end.X - reach, end.Y),
                    end,
                    isStroked: true)
            },
            closed: false);

        return new Path
        {
            Stroke = Brushes.Gray,
            StrokeThickness = 1.5,
            Data = new PathGeometry(new[] { figure })
        };
    }

    private static Border MakeNode(HistoryNode node, bool isCurrent, Action<HistoryNode> onNodeClicked)
    {
        var border = new Border
        {
            Width = NodeWidth,
            Height = NodeHeight,
            CornerRadius = new CornerRadius(6),
            Background = isCurrent ? CurrentFill : Brushes.WhiteSmoke,
            BorderBrush = isCurrent ? CurrentBorder : Brushes.Gray,
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = node.Url.Length > 0 ? node.Url : node.DisplayName,
            Child = new TextBlock
            {
                Text = node.DisplayName.Length == 0 ? "(root)" : node.DisplayName,
                FontSize = 11,
                Foreground = isCurrent ? Brushes.White : Brushes.Black,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 8, 0)
            }
        };

        border.MouseLeftButtonUp += (_, _) => onNodeClicked(node);

        return border;
    }
}
