using System.Collections.Generic;

namespace RotomMindmap.Domain;

public sealed class OutlineDocumentModel
{
    public string Title { get; set; } = string.Empty;

    public List<OutlineItem> Items { get; } = [];
}
