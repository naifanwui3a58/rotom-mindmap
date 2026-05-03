using System.Collections.Generic;

namespace RotomMindmap.Domain;

public sealed class MindMapDocumentState
{
    public float Zoom { get; set; } = 1f;

    public List<MindMapNodeLayout> Nodes { get; set; } = [];

    public List<MindMapEdgeLayout> Edges { get; set; } = [];
}
