using System;
using Godot;

namespace RotomMindmap.UI;

public partial class LibraryRootDropZone : PanelContainer
{
    [Signal]
    public delegate void LibraryItemMoveRequestedEventHandler(string sourceRelativePath, bool sourceIsDirectory, string targetFolderRelativePath);

    private bool _isDropTarget;
    private readonly StyleBoxFlat _normalStyle = new()
    {
        BgColor = new Color(1f, 1f, 1f, 0.001f),
        BorderColor = new Color(1f, 1f, 1f, 0f),
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        ContentMarginLeft = 0,
        ContentMarginTop = 0,
        ContentMarginRight = 0,
        ContentMarginBottom = 0
    };

    private readonly StyleBoxFlat _dropStyle = new()
    {
        BgColor = new Color("eef5ff"),
        BorderColor = new Color("8fb2ff"),
        BorderWidthLeft = 1,
        BorderWidthTop = 1,
        BorderWidthRight = 1,
        BorderWidthBottom = 1,
        CornerRadiusTopLeft = 16,
        CornerRadiusTopRight = 16,
        CornerRadiusBottomLeft = 16,
        CornerRadiusBottomRight = 16
    };

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        SizeFlagsVertical = SizeFlags.Fill;
        CustomMinimumSize = Vector2.Zero;
        RefreshStyle();
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        var canDrop = TryGetLibraryItemDragSource(data, out var sourceRelativePath, out var sourceIsDirectory)
            && CanDropLibraryItemToRoot(sourceRelativePath, sourceIsDirectory);
        if (_isDropTarget != canDrop)
        {
            _isDropTarget = canDrop;
            RefreshStyle();
        }

        return canDrop;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (!TryGetLibraryItemDragSource(data, out var sourceRelativePath, out var sourceIsDirectory))
        {
            return;
        }

        _isDropTarget = false;
        RefreshStyle();
        EmitSignal(SignalName.LibraryItemMoveRequested, sourceRelativePath, sourceIsDirectory, string.Empty);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationDragEnd && _isDropTarget)
        {
            _isDropTarget = false;
            RefreshStyle();
        }
    }

    private void RefreshStyle()
    {
        AddThemeStyleboxOverride("panel", _isDropTarget ? _dropStyle : _normalStyle);
    }

    private static bool TryGetLibraryItemDragSource(Variant data, out string sourceRelativePath, out bool sourceIsDirectory)
    {
        sourceRelativePath = string.Empty;
        sourceIsDirectory = false;
        if (data.VariantType != Variant.Type.Dictionary)
        {
            return false;
        }

        var payload = data.AsGodotDictionary<string, Variant>();
        if (!payload.TryGetValue("type", out var typeVariant)
            || !string.Equals(typeVariant.AsString(), "library_item", StringComparison.Ordinal))
        {
            return false;
        }

        if (!payload.TryGetValue("source_relative_path", out var pathVariant))
        {
            return false;
        }

        sourceRelativePath = pathVariant.AsString();
        if (payload.TryGetValue("source_is_directory", out var directoryVariant))
        {
            sourceIsDirectory = directoryVariant.AsBool();
        }

        return !string.IsNullOrWhiteSpace(sourceRelativePath);
    }

    private static bool CanDropLibraryItemToRoot(string sourceRelativePath, bool sourceIsDirectory)
    {
        var sourceParent = System.IO.Path.GetDirectoryName(sourceRelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar))
            ?.Replace('\\', '/')
            ?? string.Empty;
        if (string.Equals(sourceParent, string.Empty, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!sourceIsDirectory)
        {
            return true;
        }

        return true;
    }
}
