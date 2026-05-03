using System.Collections.Generic;

namespace RotomMindmap.Domain;

public sealed class LibraryNode
{
    public string Name { get; set; } = string.Empty;

    public string RelativePath { get; set; } = string.Empty;

    public bool IsDirectory { get; set; }

    public string DisplayTitle { get; set; } = string.Empty;

    public List<LibraryNode> Children { get; } = [];
}
