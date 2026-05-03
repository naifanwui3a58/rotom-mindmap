namespace RotomMindmap.Domain;

public sealed class DocumentSnapshot
{
    public string RelativePath { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;

    public string DisplayTitle { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;
}
