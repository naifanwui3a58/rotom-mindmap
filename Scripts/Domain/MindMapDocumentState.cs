using System.Collections.Generic;

namespace RotomMindmap.Domain;

public sealed class MindMapDocumentState
{
    public float Zoom { get; set; } = 1f;

    public MindMapLayoutMode LayoutMode { get; set; } = MindMapLayoutMode.Horizontal;

    public List<MindMapNodeLayout> Nodes { get; set; } = [];

    public List<MindMapEdgeLayout> Edges { get; set; } = [];
}
