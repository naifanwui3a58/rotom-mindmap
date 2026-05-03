using System;

namespace RotomMindmap.Domain;

public sealed class TrashEntry
{
    public string TrashId { get; set; } = string.Empty;

    public string OriginalRelativePath { get; set; } = string.Empty;

    public string StoredName { get; set; } = string.Empty;

    public bool IsDirectory { get; set; }

    public DateTime DeletedAtUtc { get; set; }
}
