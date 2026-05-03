namespace RotomMindmap.Domain;

public sealed class OutlineItem
{
    public string Id { get; set; } = string.Empty;

    public int Depth { get; set; }

    public string Text { get; set; } = string.Empty;
}
