using Godot;

namespace RotomMindmap.UI;

public partial class OutlineTitleRow : LineEdit
{
    public LineEdit Editor => this;

    public void ApplyState(string text, string placeholder)
    {
        Text = text;
        PlaceholderText = placeholder;
    }
}
