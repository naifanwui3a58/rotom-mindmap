using System;
using Godot;

namespace RotomMindmap.UI;

public partial class OutlineItemRow : HBoxContainer
{
    private Color _bulletPrimaryColor = new("233247");
    private Color _bulletSecondaryColor = new("6a7a90");
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
        _bullet.Modulate = Colors.White;
        _bullet.AddThemeColorOverride("font_color", depth == 0 ? _bulletPrimaryColor : _bulletSecondaryColor);
        _bullet.AddThemeFontSizeOverride("font_size", depth == 0 ? 18 : 16);

        _editor.Text = text;
        _editor.PlaceholderText = placeholder;
        _editor.AddThemeFontSizeOverride("font_size", depth == 0 ? 20 : 17);
    }

    public void ApplyTheme(Color bulletPrimaryColor, Color bulletSecondaryColor)
    {
        _bulletPrimaryColor = bulletPrimaryColor;
        _bulletSecondaryColor = bulletSecondaryColor;
        if (_bullet is null)
        {
            return;
        }

        var isPrimaryDepth = _editor is not null && _editor.GetThemeFontSize("font_size") >= 20;
        _bullet.Modulate = Colors.White;
        _bullet.AddThemeColorOverride("font_color", isPrimaryDepth ? _bulletPrimaryColor : _bulletSecondaryColor);
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
