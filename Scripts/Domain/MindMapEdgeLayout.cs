namespace RotomMindmap.Domain;

public sealed class MindMapEdgeLayout
{
    public string ChildNodeId { get; set; } = string.Empty;

    public MindMapConnectorSide ParentSide { get; set; } = MindMapConnectorSide.Right;

    public MindMapConnectorSide ChildSide { get; set; } = MindMapConnectorSide.Left;
}
