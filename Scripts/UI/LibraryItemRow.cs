using System;
using Godot;

namespace RotomMindmap.UI;

public partial class LibraryItemRow : PanelContainer
{
    [Signal]
    public delegate void SelectedEventHandler(string relativePath, bool isDirectory);

    [Signal]
    public delegate void ActivatedEventHandler(string relativePath, bool isDirectory);

    [Signal]
    public delegate void SecondaryActionRequestedEventHandler(string relativePath, bool isDirectory, Vector2 globalPosition);

    [Signal]
    public delegate void ExpansionToggledEventHandler(string relativePath);

    [Signal]
    public delegate void RenameSubmittedEventHandler(string relativePath, bool isDirectory, string newName);

    [Signal]
    public delegate void LibraryItemMoveRequestedEventHandler(string sourceRelativePath, bool sourceIsDirectory, string targetFolderRelativePath);

    [Signal]
    public delegate void DragStartedEventHandler(string sourceRelativePath, bool sourceIsDirectory);

    private Control _indent = null!;
    private Control _row = null!;
    private Button _expandButton = null!;
    private Label _iconLabel = null!;
    private Label _titleLabel = null!;
    private LineEdit _renameEdit = null!;

    private string _relativePath = string.Empty;
    private bool _isDirectory;
    private bool _isEditing;
    private bool _isWired;
    private bool _isSelected;
    private bool _isDropTarget;
    private bool _isLeftPressed;
    private bool _dragStartedThisPress;
    private Vector2 _leftPressPosition;

    public string RelativePath => _relativePath;

    public override void _Ready()
    {
        EnsureNodes();
    }

    private void EnsureNodes()
    {
        if (_indent is not null && _isWired)
        {
            return;
        }

        _row = RequireNode<Control>("Row");
        _indent = RequireNode<Control>("Indent");
        _expandButton = RequireNode<Button>("ExpandButton");
        _iconLabel = RequireNode<Label>("IconLabel");
        _titleLabel = RequireNode<Label>("TitleLabel");
        _renameEdit = RequireNode<LineEdit>("RenameEdit");

        _row.MouseFilter = MouseFilterEnum.Ignore;
        _indent.MouseFilter = MouseFilterEnum.Ignore;
        _iconLabel.MouseFilter = MouseFilterEnum.Ignore;
        _titleLabel.MouseFilter = MouseFilterEnum.Ignore;

        if (_isWired)
        {
            return;
        }

        _expandButton.Pressed += OnExpandPressed;
        GuiInput += OnGuiInput;
        _renameEdit.TextSubmitted += OnRenameSubmitted;
        _renameEdit.FocusExited += OnRenameFocusExited;
        _isWired = true;
    }

    public void ApplyState(
        string relativePath,
        bool isDirectory,
        string title,
        int depth,
        bool isExpanded,
        bool isSelected,
        bool isEditing)
    {
        EnsureNodes();

        _relativePath = relativePath;
        _isDirectory = isDirectory;
        _isEditing = isEditing;
        _isSelected = isSelected;
        _isDropTarget = false;

        _indent.CustomMinimumSize = new Vector2(depth * 18f, 0f);
        _expandButton.Visible = isDirectory;
        _expandButton.Text = isExpanded ? "\u25BE" : "\u25B8";
        _iconLabel.Text = isDirectory ? "\u25A0" : "\u2022";
        _iconLabel.Modulate = isDirectory ? new Color("6f85a6") : new Color("7c90b4");
        _expandButton.AddThemeColorOverride("font_color", new Color("7d8da4"));
        _titleLabel.Text = title;
        ApplyTitleColor();
        _titleLabel.Visible = !isEditing;
        _renameEdit.Visible = isEditing;
        _renameEdit.Text = title;

        RefreshPanelStyle();

        if (isEditing)
        {
            CallDeferred(nameof(FocusRenameEditor));
        }
    }

    private void OnExpandPressed()
    {
        if (_isDirectory)
        {
            EmitSignal(SignalName.ExpansionToggled, _relativePath);
        }
    }

    private void OnGuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion)
        {
            HandleMouseMotion(motion);
            return;
        }

        if (@event is not InputEventMouseButton button)
        {
            return;
        }

        if (button.ButtonIndex == MouseButton.Left && button.Pressed)
        {
            if (button.DoubleClick)
            {
                EmitSignal(SignalName.Selected, _relativePath, _isDirectory);
                EmitSignal(SignalName.Activated, _relativePath, _isDirectory);
                _isLeftPressed = false;
                _dragStartedThisPress = true;
                return;
            }

            _isLeftPressed = true;
            _dragStartedThisPress = false;
            _leftPressPosition = button.Position;
            return;
        }

        if (button.ButtonIndex == MouseButton.Left && !button.Pressed)
        {
            if (_isLeftPressed && !_dragStartedThisPress)
            {
                EmitSignal(SignalName.Selected, _relativePath, _isDirectory);
                if (!_isDirectory)
                {
                    EmitSignal(SignalName.Activated, _relativePath, _isDirectory);
                }
            }

            _isLeftPressed = false;
            _dragStartedThisPress = false;
            return;
        }

        if (button.ButtonIndex == MouseButton.Right && button.Pressed)
        {
            EmitSignal(SignalName.Selected, _relativePath, _isDirectory);
            EmitSignal(SignalName.SecondaryActionRequested, _relativePath, _isDirectory, GetGlobalMousePosition());
            GetViewport().SetInputAsHandled();
        }
    }

    private void OnRenameSubmitted(string newText)
    {
        CommitRename(newText);
    }

    private void OnRenameFocusExited()
    {
        if (_isEditing)
        {
            CommitRename(_renameEdit.Text);
        }
    }

    private void CommitRename(string newText)
    {
        if (!_isEditing)
        {
            return;
        }

        _isEditing = false;
        EmitSignal(SignalName.RenameSubmitted, _relativePath, _isDirectory, newText.Trim());
    }

    private void FocusRenameEditor()
    {
        if (!_renameEdit.IsVisibleInTree())
        {
            return;
        }

        _renameEdit.GrabFocus();
        _renameEdit.SelectAll();
    }

    public void SetSelectedState(bool isSelected)
    {
        _isSelected = isSelected;
        ApplyTitleColor();
        RefreshPanelStyle();
    }

    private void HandleMouseMotion(InputEventMouseMotion motion)
    {
        if (!_isLeftPressed || _dragStartedThisPress || _isEditing || string.IsNullOrWhiteSpace(_relativePath))
        {
            return;
        }

        if (motion.Position.DistanceTo(_leftPressPosition) < 8f)
        {
            return;
        }

        _dragStartedThisPress = true;
        _isLeftPressed = false;
        EmitSignal(SignalName.Selected, _relativePath, _isDirectory);
        EmitSignal(SignalName.DragStarted, _relativePath, _isDirectory);
        ForceDrag(BuildDragPayload(), BuildDragPreview());
    }

    public override Variant _GetDragData(Vector2 atPosition)
    {
        if (_isEditing || string.IsNullOrWhiteSpace(_relativePath))
        {
            return default;
        }

        SetDragPreview(BuildDragPreview());
        return BuildDragPayload();
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        var canDrop = TryGetLibraryItemDragSource(data, out var sourceRelativePath, out var sourceIsDirectory)
            && TryResolveDropTargetFolder(out var targetFolderRelativePath)
            && CanAcceptDrop(sourceRelativePath, sourceIsDirectory, targetFolderRelativePath);

        if (_isDropTarget != canDrop)
        {
            _isDropTarget = canDrop;
            RefreshPanelStyle();
        }

        return canDrop;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (!TryGetLibraryItemDragSource(data, out var sourceRelativePath, out var sourceIsDirectory)
            || !TryResolveDropTargetFolder(out var targetFolderRelativePath))
        {
            return;
        }

        _isDropTarget = false;
        RefreshPanelStyle();
        EmitSignal(SignalName.LibraryItemMoveRequested, sourceRelativePath, sourceIsDirectory, targetFolderRelativePath);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationDragEnd && _isDropTarget)
        {
            _isDropTarget = false;
            RefreshPanelStyle();
        }
    }

    private void RefreshPanelStyle()
    {
        AddThemeStyleboxOverride("panel", BuildPanelStyle(_isSelected, _isDropTarget));
    }

    private void ApplyTitleColor()
    {
        _titleLabel.AddThemeColorOverride("font_color", _isSelected ? new Color("233247") : new Color("42536b"));
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

    private bool CanAcceptDrop(string sourceRelativePath, bool sourceIsDirectory, string targetFolderRelativePath)
    {
        if (string.Equals(sourceRelativePath, targetFolderRelativePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var sourceParent = System.IO.Path.GetDirectoryName(sourceRelativePath.Replace('/', System.IO.Path.DirectorySeparatorChar))
            ?.Replace('\\', '/')
            ?? string.Empty;
        if (string.Equals(sourceParent, targetFolderRelativePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!sourceIsDirectory)
        {
            return true;
        }

        var normalizedTarget = targetFolderRelativePath.Trim('/');
        var normalizedSource = sourceRelativePath.Trim('/');
        return !normalizedTarget.StartsWith($"{normalizedSource}/", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryResolveDropTargetFolder(out string targetFolderRelativePath)
    {
        targetFolderRelativePath = string.Empty;
        if (_isDirectory)
        {
            targetFolderRelativePath = _relativePath;
            return true;
        }

        var parentFolder = System.IO.Path.GetDirectoryName(_relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar))
            ?.Replace('\\', '/')
            ?? string.Empty;
        targetFolderRelativePath = parentFolder;
        return true;
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

    private Godot.Collections.Dictionary<string, Variant> BuildDragPayload()
    {
        return new Godot.Collections.Dictionary<string, Variant>
        {
            { "type", "library_item" },
            { "source_relative_path", _relativePath },
            { "source_is_directory", _isDirectory }
        };
    }

    private Control BuildDragPreview()
    {
        var previewPanel = new PanelContainer();
        var previewStyle = new StyleBoxFlat
        {
            BgColor = Colors.White,
            BorderColor = new Color("cfdcee"),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 12,
            CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12,
            CornerRadiusBottomRight = 12,
            ContentMarginLeft = 12,
            ContentMarginTop = 8,
            ContentMarginRight = 12,
            ContentMarginBottom = 8
        };
        previewPanel.AddThemeStyleboxOverride("panel", previewStyle);

        var previewLabel = new Label
        {
            Text = _titleLabel.Text
        };
        previewLabel.AddThemeColorOverride("font_color", new Color("233247"));
        previewPanel.AddChild(previewLabel);
        return previewPanel;
    }

    private static StyleBoxFlat BuildPanelStyle(bool isSelected, bool isDropTarget)
    {
        return new StyleBoxFlat
        {
            BgColor = isDropTarget
                ? new Color("e7f0ff")
                : isSelected
                    ? new Color("edf4ff")
                    : new Color(1f, 1f, 1f, 0f),
            BorderColor = isDropTarget
                ? new Color("9db9f3")
                : isSelected
                    ? new Color("d4e1f7")
                    : new Color(1f, 1f, 1f, 0f),
            BorderWidthLeft = isDropTarget || isSelected ? 1 : 0,
            BorderWidthTop = isDropTarget || isSelected ? 1 : 0,
            BorderWidthRight = isDropTarget || isSelected ? 1 : 0,
            BorderWidthBottom = isDropTarget || isSelected ? 1 : 0,
            CornerRadiusTopLeft = 14,
            CornerRadiusTopRight = 14,
            CornerRadiusBottomLeft = 14,
            CornerRadiusBottomRight = 14,
            ContentMarginLeft = 8,
            ContentMarginTop = 4,
            ContentMarginRight = 8,
            ContentMarginBottom = 4
        };
    }
}
