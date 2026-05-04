using Godot;

namespace RotomMindmap.Domain;

public sealed class MindMapNodeViewModel
{
    public string Id { get; set; } = string.Empty;

    public string ParentId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public int Level { get; set; }

    public Vector2 Position { get; set; }

    public Vector2? CustomSize { get; set; }

    public bool IsRoot { get; set; }

    public MindMapConnectorSide? IncomingParentSide { get; set; }

    public MindMapConnectorSide? IncomingChildSide { get; set; }
}
