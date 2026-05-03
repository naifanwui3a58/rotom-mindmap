using System;
using Godot;

namespace RotomMindmap.UI;

public partial class OutlineItemRow : HBoxContainer
{
    private Control _indent = null!;
    private Label _bullet = null!;
    private LineEdit _editor = null!;

    public LineEdit Editor
    {
        get
        {
            EnsureNodes();
            return _editor;
        }
    }

    public void ApplyState(int depth, string text, string placeholder)
    {
        EnsureNodes();

        _indent.CustomMinimumSize = new Vector2(depth * 24f, 0f);
        _bullet.Text = "\u2022";
        _bullet.Modulate = depth == 0 ? new Color("3d4c63") : new Color("8a93a5");
        _bullet.AddThemeFontSizeOverride("font_size", depth == 0 ? 18 : 16);

        _editor.Text = text;
        _editor.PlaceholderText = placeholder;
        _editor.AddThemeFontSizeOverride("font_size", depth == 0 ? 20 : 17);
    }

    private void EnsureNodes()
    {
        if (_editor is not null)
        {
            return;
        }

        _indent = RequireNode<Control>("Indent");
        _bullet = RequireNode<Label>("Bullet");
        _editor = RequireNode<LineEdit>("ItemEdit");
    }

    private T RequireNode<T>(string nodeName) where T : Node
    {
        var node = FindChild(nodeName, true, false);
        if (node is T typedNode)
        {
            return typedNode;
        }

        throw new InvalidOperationException($"Required node '{nodeName}' of type {typeof(T).Name} was not found.");
    }
}
