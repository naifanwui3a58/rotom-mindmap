namespace RotomMindmap.Domain;

public sealed class MarkdownNode
{
    public string Id { get; set; } = string.Empty;

    public string ParentId { get; set; } = string.Empty;

    public int Level { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public int Order { get; set; }

    public string Path { get; set; } = string.Empty;
}
