namespace RotomMindmap.Domain;

public sealed class MindMapNodeLayout
{
    public string NodeId { get; set; } = string.Empty;

    public float X { get; set; }

    public float Y { get; set; }

    public float Width { get; set; }

    public float Height { get; set; }
}
