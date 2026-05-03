using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Godot;
using RotomMindmap.Domain;

namespace RotomMindmap.UI;

public partial class MindMapCanvas : Control
{
    private const string RootNodeId = "__document_root__";
    private const float ExportPadding = 48f;

    [Signal]
    public delegate void NodeMovedEventHandler(string nodeId, Vector2 position);

    [Signal]
    public delegate void NodeReparentedEventHandler(string nodeId, string newParentId);

    [Signal]
    public delegate void ConnectionChangedEventHandler(
        string childNodeId,
        string parentNodeId,
        int parentSide,
        int childSide);

    [Signal]
    public delegate void ZoomChangedEventHandler(float zoom);

    private sealed class NodeCardState
    {
        public required MindMapNodeViewModel Node { get; init; }

        public required PanelContainer Panel { get; init; }

        public required MarginContainer Margin { get; init; }

        public required Label TitleLabel { get; init; }

        public Label? BodyLabel { get; init; }

        public required Vector2 BaseSize { get; init; }

        public required Dictionary<MindMapConnectorSide, Button> Handles { get; init; }
    }

    private readonly Dictionary<string, NodeCardState> _cards = new(StringComparer.Ordinal);
    private readonly List<(string ParentId, string ChildId)> _edges = [];
    private readonly StyleBoxFlat _rootStyle = new();
    private readonly StyleBoxFlat _branchStyle = new();
    private readonly StyleBoxFlat _handleStyle = new();
    private readonly StyleBoxFlat _handleActiveStyle = new();
    private Color _backgroundColor = new("f7fafe");
    private Color _edgeColor = new("c7d6e9");
    private Color _previewEdgeColor = new("aabbd8");
    private Color _edgeStartHandleColor = new("c3d0e3");
    private Color _edgeEndHandleColor = new("7d9eff");
    private Color _titleTextColor = new("223247");
    private Color _bodyTextColor = new("74849a");

    private string _selectedNodeId = string.Empty;
    private string _draggingNodeId = string.Empty;
    private Vector2 _dragMouseOffset;
    private bool _isPanning;
    private Vector2 _panStartMousePosition;
    private Vector2 _panOffset = new(80f, 80f);
    private Vector2 _panStartOffset;
    private MindMapConnectorSide? _armedChildSide;
    private float _zoom = 1f;
    private string _dragPreviewTargetId = string.Empty;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;

        _rootStyle.BgColor = Colors.White;
        _rootStyle.BorderColor = new Color("cfdcee");
        _rootStyle.BorderWidthLeft = 1;
        _rootStyle.BorderWidthTop = 1;
        _rootStyle.BorderWidthRight = 1;
        _rootStyle.BorderWidthBottom = 1;
        _rootStyle.CornerRadiusTopLeft = 12;
        _rootStyle.CornerRadiusTopRight = 12;
        _rootStyle.CornerRadiusBottomLeft = 12;
        _rootStyle.CornerRadiusBottomRight = 12;

        _branchStyle.BgColor = Colors.White;
        _branchStyle.BorderColor = new Color("d9e3ef");
        _branchStyle.BorderWidthLeft = 1;
        _branchStyle.BorderWidthTop = 1;
        _branchStyle.BorderWidthRight = 1;
        _branchStyle.BorderWidthBottom = 1;
        _branchStyle.CornerRadiusTopLeft = 12;
        _branchStyle.CornerRadiusTopRight = 12;
        _branchStyle.CornerRadiusBottomLeft = 12;
        _branchStyle.CornerRadiusBottomRight = 12;

        _handleStyle.BgColor = new Color("eef4ff");
        _handleStyle.BorderColor = new Color("c6d8f4");
        _handleStyle.BorderWidthLeft = 1;
        _handleStyle.BorderWidthTop = 1;
        _handleStyle.BorderWidthRight = 1;
        _handleStyle.BorderWidthBottom = 1;
        _handleStyle.CornerRadiusTopLeft = 999;
        _handleStyle.CornerRadiusTopRight = 999;
        _handleStyle.CornerRadiusBottomLeft = 999;
        _handleStyle.CornerRadiusBottomRight = 999;

        _handleActiveStyle.BgColor = new Color("7d9eff");
        _handleActiveStyle.BorderColor = new Color("6d8ef2");
        _handleActiveStyle.BorderWidthLeft = 1;
        _handleActiveStyle.BorderWidthTop = 1;
        _handleActiveStyle.BorderWidthRight = 1;
        _handleActiveStyle.BorderWidthBottom = 1;
        _handleActiveStyle.CornerRadiusTopLeft = 999;
        _handleActiveStyle.CornerRadiusTopRight = 999;
        _handleActiveStyle.CornerRadiusBottomLeft = 999;
        _handleActiveStyle.CornerRadiusBottomRight = 999;
    }

    public string GetSelectedNodeId()
    {
        return _selectedNodeId;
    }

    public void ApplyTheme(
        bool isDark,
        Color panelColor,
        Color borderColor,
        Color secondaryBorderColor,
        Color primaryTextColor,
        Color secondaryTextColor,
        Color selectedBorderColor)
    {
        _backgroundColor = isDark ? new Color("101826") : new Color("f7fafe");
        _edgeColor = isDark ? new Color("42556f") : new Color("c7d6e9");
        _previewEdgeColor = isDark ? new Color("6f86a3") : new Color("aabbd8");
        _edgeStartHandleColor = isDark ? new Color("64748b") : new Color("c3d0e3");
        _edgeEndHandleColor = isDark ? new Color("7d9eff") : new Color("7d9eff");
        _titleTextColor = primaryTextColor;
        _bodyTextColor = secondaryTextColor;

        _rootStyle.BgColor = panelColor;
        _rootStyle.BorderColor = borderColor;
        _branchStyle.BgColor = panelColor;
        _branchStyle.BorderColor = secondaryBorderColor;
        _handleStyle.BgColor = isDark ? new Color("1f2b3d") : new Color("eef4ff");
        _handleStyle.BorderColor = isDark ? new Color("51657f") : new Color("c6d8f4");
        _handleActiveStyle.BgColor = isDark ? new Color("5d8cff") : new Color("7d9eff");
        _handleActiveStyle.BorderColor = selectedBorderColor;

        foreach (var card in _cards.Values)
        {
            ApplyCardVisual(card);
        }

        QueueRedraw();
    }

    public void RenderNodes(IReadOnlyList<MindMapNodeViewModel> nodes, float zoom)
    {
        _zoom = Math.Clamp(zoom, 0.5f, 2.5f);

        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }

        _cards.Clear();
        _edges.Clear();

        if (nodes.Count == 0)
        {
            CustomMinimumSize = new Vector2(980, 640);
            QueueRedraw();
            return;
        }

        var maxX = 980f;
        var maxY = 640f;
        foreach (var node in nodes)
        {
            var baseSize = node.IsRoot
                ? new Vector2(220f, 84f)
                : new Vector2(180f, string.IsNullOrWhiteSpace(node.Body) ? 72f : 96f);
            var card = CreateCard(node, baseSize);
            _cards[node.Id] = card;
            ApplyCardVisual(card);

            var rect = GetViewRect(node.Id);
            maxX = Math.Max(maxX, rect.End.X + 140f);
            maxY = Math.Max(maxY, rect.End.Y + 140f);
        }

        foreach (var node in nodes)
        {
            if (!string.IsNullOrWhiteSpace(node.ParentId) && _cards.ContainsKey(node.ParentId))
            {
                _edges.Add((node.ParentId, node.Id));
            }
        }

        CustomMinimumSize = new Vector2(maxX, maxY);
        RefreshHandleVisibility();
        QueueRedraw();
    }

    public override void _Draw()
    {
        DrawRect(new Rect2(Vector2.Zero, Size), _backgroundColor, true);

        foreach (var (parentId, childId) in _edges)
        {
            if (!_cards.TryGetValue(parentId, out var parentCard) || !_cards.TryGetValue(childId, out var childCard))
            {
                continue;
            }

            var parentRect = GetViewRect(parentId);
            var childRect = GetViewRect(childId);
            var anchorInset = Math.Max(4f, 8f * _zoom);
            var start = childCard.Node.IncomingParentSide is { } fixedParentSide
                ? GetAnchorForSide(parentRect, fixedParentSide, anchorInset)
                : GetDirectionalAnchor(parentRect, childRect.GetCenter() - parentRect.GetCenter(), anchorInset);
            var end = childCard.Node.IncomingChildSide is { } fixedChildSide
                ? GetAnchorForSide(childRect, fixedChildSide, anchorInset)
                : GetDirectionalAnchor(childRect, parentRect.GetCenter() - childRect.GetCenter(), anchorInset);
            var parentSide = childCard.Node.IncomingParentSide
                ?? MindMapEdgeRouteBuilder.ResolveDirectionalSide(childRect.GetCenter() - parentRect.GetCenter());
            var childSide = childCard.Node.IncomingChildSide
                ?? MindMapEdgeRouteBuilder.ResolveDirectionalSide(parentRect.GetCenter() - childRect.GetCenter());
            var route = MindMapEdgeRouteBuilder.BuildRoute(start, parentSide, end, childSide, _zoom);
            var isPreview = !string.IsNullOrWhiteSpace(_dragPreviewTargetId)
                && childId == _draggingNodeId
                && parentId == _dragPreviewTargetId;
            DrawPolyline(
                route,
                isPreview ? _previewEdgeColor : _edgeColor,
                isPreview ? Math.Max(1.2f, 1.9f * _zoom) : Math.Max(1.05f, 1.45f * _zoom),
                true);

            if (childId == _selectedNodeId)
            {
                DrawCircle(start, Math.Max(2f, 2.8f * _zoom), _edgeStartHandleColor);
                DrawCircle(end, Math.Max(2.4f, 3.4f * _zoom), _edgeEndHandleColor);
            }
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton button:
                HandleMouseButton(button);
                break;
            case InputEventMouseMotion motion:
                HandleMouseMotion(motion);
                break;
        }
    }

    private void HandleMouseButton(InputEventMouseButton button)
    {
        if (button.ButtonIndex == MouseButton.WheelUp && button.Pressed)
        {
            SetZoom(_zoom * 1.12f, button.Position);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (button.ButtonIndex == MouseButton.WheelDown && button.Pressed)
        {
            SetZoom(_zoom / 1.12f, button.Position);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (button.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        var hitNodeId = HitTest(button.Position);
        if (button.Pressed)
        {
            if (button.ShiftPressed && !string.IsNullOrWhiteSpace(_selectedNodeId))
            {
                EmitSignal(SignalName.NodeReparented, _selectedNodeId, hitNodeId);
                if (string.IsNullOrWhiteSpace(hitNodeId))
                {
                    _selectedNodeId = string.Empty;
                }
                _armedChildSide = null;
                RefreshHandleVisibility();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (string.IsNullOrWhiteSpace(hitNodeId))
            {
                _selectedNodeId = string.Empty;
                _armedChildSide = null;
                _dragPreviewTargetId = string.Empty;
                _isPanning = true;
                _panStartMousePosition = button.Position;
                _panStartOffset = _panOffset;
                RefreshHandleVisibility();
                GetViewport().SetInputAsHandled();
                return;
            }

            _selectedNodeId = hitNodeId;
            _armedChildSide = null;
            _dragPreviewTargetId = string.Empty;
            _draggingNodeId = hitNodeId;
            _dragMouseOffset = (button.Position - _panOffset) / _zoom - _cards[hitNodeId].Node.Position;
            RefreshHandleVisibility();
            GetViewport().SetInputAsHandled();
            return;
        }

        _isPanning = false;
        if (string.IsNullOrWhiteSpace(_draggingNodeId) || !_cards.TryGetValue(_draggingNodeId, out var draggingCard))
        {
            return;
        }

        _draggingNodeId = string.Empty;
        _dragPreviewTargetId = string.Empty;
        EmitSignal(SignalName.NodeMoved, draggingCard.Node.Id, draggingCard.Node.Position);
        foreach (var card in _cards.Values)
        {
            ApplyCardVisual(card);
        }
        QueueRedraw();
        GetViewport().SetInputAsHandled();
    }

    private void HandleMouseMotion(InputEventMouseMotion motion)
    {
        if (_isPanning)
        {
            ApplyPan(motion.Position);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (string.IsNullOrWhiteSpace(_draggingNodeId) || !_cards.TryGetValue(_draggingNodeId, out var card))
        {
            return;
        }

        var logicalPosition = (motion.Position - _panOffset) / _zoom - _dragMouseOffset;
        card.Node.Position = ClampNodePosition(logicalPosition);
        _dragPreviewTargetId = FindClosestDropTarget(card.Node.Id);
        foreach (var currentCard in _cards.Values)
        {
            ApplyCardVisual(currentCard);
        }
        QueueRedraw();
        GetViewport().SetInputAsHandled();
    }

    private void SetZoom(float nextZoom, Vector2? anchorViewPosition = null)
    {
        var clamped = Math.Clamp(nextZoom, 0.5f, 2.5f);
        if (Math.Abs(clamped - _zoom) < 0.001f)
        {
            return;
        }

        Vector2? logicalAnchor = null;
        if (anchorViewPosition is { } viewPosition)
        {
            logicalAnchor = (viewPosition - _panOffset) / _zoom;
        }

        _zoom = clamped;
        if (anchorViewPosition is { } nextViewPosition && logicalAnchor is { } nextLogicalAnchor)
        {
            _panOffset = nextViewPosition - nextLogicalAnchor * _zoom;
        }

        foreach (var card in _cards.Values)
        {
            ApplyCardVisual(card);
        }

        if (_cards.Count > 0)
        {
            CustomMinimumSize = new Vector2(
                Math.Max(980f, _cards.Values.Max(card => GetViewRect(card.Node.Id).End.X) + 140f),
                Math.Max(640f, _cards.Values.Max(card => GetViewRect(card.Node.Id).End.Y) + 140f));
        }
        QueueRedraw();
        EmitSignal(SignalName.ZoomChanged, _zoom);
    }

    private Vector2 ClampNodePosition(Vector2 logicalPosition)
    {
        var minLogicalX = (-_panOffset.X / _zoom) + 20f;
        var minLogicalY = (-_panOffset.Y / _zoom) + 20f;
        return new Vector2(
            Math.Max(minLogicalX, logicalPosition.X),
            Math.Max(minLogicalY, logicalPosition.Y));
    }

    private NodeCardState CreateCard(MindMapNodeViewModel node, Vector2 baseSize)
    {
        var panel = new PanelContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            ClipContents = true
        };
        var margin = new MarginContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_top", 14);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddThemeConstantOverride("margin_bottom", 14);
        panel.AddChild(margin);

        var box = new VBoxContainer
        {
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        box.AddThemeConstantOverride("separation", 6);
        margin.AddChild(box);

        var title = new Label
        {
            Text = node.Title,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(Math.Max(120f, baseSize.X - 40f), 0)
        };
        box.AddChild(title);

        Label? body = null;
        if (!string.IsNullOrWhiteSpace(node.Body))
        {
            body = new Label
            {
                Text = node.Body,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
                MouseFilter = MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(Math.Max(120f, baseSize.X - 40f), 0)
            };
            box.AddChild(body);
        }

        AddChild(panel);

        var handles = Enum
            .GetValues<MindMapConnectorSide>()
            .ToDictionary(side => side, side => CreateHandle(node.Id, side));

        return new NodeCardState
        {
            Node = node,
            Panel = panel,
            Margin = margin,
            TitleLabel = title,
            BodyLabel = body,
            BaseSize = baseSize,
            Handles = handles
        };
    }

    private Button CreateHandle(string nodeId, MindMapConnectorSide side)
    {
        var handle = new Button
        {
            Text = string.Empty,
            FocusMode = FocusModeEnum.None,
            MouseDefaultCursorShape = CursorShape.PointingHand,
            CustomMinimumSize = new Vector2(16f, 16f),
            Size = new Vector2(16f, 16f),
            Visible = false
        };
        handle.AddThemeStyleboxOverride("normal", _handleStyle);
        handle.AddThemeStyleboxOverride("hover", _handleActiveStyle);
        handle.AddThemeStyleboxOverride("pressed", _handleActiveStyle);
        handle.Pressed += () => OnHandlePressed(nodeId, side);
        AddChild(handle);
        return handle;
    }

    private void OnHandlePressed(string nodeId, MindMapConnectorSide side)
    {
        if (string.IsNullOrWhiteSpace(_selectedNodeId) || _selectedNodeId == RootNodeId)
        {
            return;
        }

        if (nodeId == _selectedNodeId)
        {
            _armedChildSide = side;
            RefreshHandleVisibility();
            return;
        }

        if (_armedChildSide is null)
        {
            return;
        }

        EmitSignal(SignalName.ConnectionChanged, _selectedNodeId, nodeId, (int)side, (int)_armedChildSide.Value);
        _armedChildSide = null;
        RefreshHandleVisibility();
    }

    private void ApplyCardVisual(NodeCardState state)
    {
        var rect = GetViewRect(state.Node.Id);
        var horizontalPadding = Mathf.RoundToInt(Math.Max(8f, 16f * _zoom));
        var verticalPadding = Mathf.RoundToInt(Math.Max(6f, 14f * _zoom));
        var contentWidth = Math.Max(56f, rect.Size.X - horizontalPadding * 2f);
        var handleSize = Math.Max(10f, 16f * _zoom);

        state.Panel.Position = rect.Position;
        state.Panel.Size = rect.Size;
        state.Panel.CustomMinimumSize = rect.Size;
        state.Margin.AddThemeConstantOverride("margin_left", horizontalPadding);
        state.Margin.AddThemeConstantOverride("margin_top", verticalPadding);
        state.Margin.AddThemeConstantOverride("margin_right", horizontalPadding);
        state.Margin.AddThemeConstantOverride("margin_bottom", verticalPadding);
        var isSelected = state.Node.Id == _selectedNodeId;
        var isPreviewTarget = state.Node.Id == _dragPreviewTargetId && state.Node.Id != _draggingNodeId;
        state.Panel.AddThemeStyleboxOverride("panel", BuildStyle(state.Node.IsRoot, isSelected, isPreviewTarget));
        state.TitleLabel.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(state.Node.IsRoot ? 18f * _zoom : 14f * _zoom));
        state.TitleLabel.AddThemeColorOverride("font_color", _titleTextColor);
        state.TitleLabel.CustomMinimumSize = new Vector2(contentWidth, 0f);

        if (state.BodyLabel is not null)
        {
            state.BodyLabel.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(11f * _zoom));
            state.BodyLabel.AddThemeColorOverride("font_color", _bodyTextColor);
            state.BodyLabel.CustomMinimumSize = new Vector2(contentWidth, 0f);
        }

        foreach (var (side, handle) in state.Handles)
        {
            var center = GetAnchorForSide(rect, side, 0f);
            handle.CustomMinimumSize = new Vector2(handleSize, handleSize);
            handle.Size = new Vector2(handleSize, handleSize);
            handle.Position = center - handle.Size / 2f;
            handle.AddThemeStyleboxOverride(
                "normal",
                state.Node.Id == _selectedNodeId && _armedChildSide == side ? _handleActiveStyle : _handleStyle);
        }
    }

    private StyleBoxFlat BuildStyle(bool isRoot, bool isSelected, bool isPreviewTarget)
    {
        var template = isRoot ? _rootStyle : _branchStyle;
        var style = (StyleBoxFlat)template.Duplicate();
        if (isSelected)
        {
            style.BorderColor = new Color("7d9eff");
            style.BorderWidthLeft = 2;
            style.BorderWidthTop = 2;
            style.BorderWidthRight = 2;
            style.BorderWidthBottom = 2;
        }
        else if (isPreviewTarget)
        {
            style.BorderColor = new Color("bfd0ea");
        }

        return style;
    }

    private void ApplyPan(Vector2 currentMousePosition)
    {
        _panOffset = _panStartOffset + (currentMousePosition - _panStartMousePosition);
        foreach (var card in _cards.Values)
        {
            ApplyCardVisual(card);
        }

        QueueRedraw();
    }

    private void RefreshHandleVisibility()
    {
        foreach (var card in _cards.Values)
        {
            var showOwnHandles = card.Node.Id == _selectedNodeId;
            var showTargetHandles = _armedChildSide is not null && card.Node.Id != _selectedNodeId;
            var visible = showOwnHandles || showTargetHandles;

            foreach (var handle in card.Handles.Values)
            {
                handle.Visible = visible;
            }
        }

        foreach (var card in _cards.Values)
        {
            ApplyCardVisual(card);
        }

        QueueRedraw();
    }

    private string HitTest(Vector2 viewPosition)
    {
        foreach (var card in _cards.Values.Reverse())
        {
            if (GetViewRect(card.Node.Id).HasPoint(viewPosition))
            {
                return card.Node.Id;
            }
        }

        return string.Empty;
    }

    private Rect2 GetViewRect(string nodeId)
    {
        var card = _cards[nodeId];
        return new Rect2(_panOffset + card.Node.Position * _zoom, card.BaseSize * _zoom);
    }

    private Rect2 GetLogicalRect(string nodeId)
    {
        var card = _cards[nodeId];
        return new Rect2(card.Node.Position, card.BaseSize);
    }

    private Rect2 GetLogicalBounds()
    {
        var first = true;
        var bounds = new Rect2(Vector2.Zero, new Vector2(960f, 640f));
        foreach (var nodeId in _cards.Keys)
        {
            var rect = GetLogicalRect(nodeId);
            bounds = first ? rect : bounds.Merge(rect);
            first = false;
        }

        return first ? new Rect2(0f, 0f, 960f, 640f) : bounds;
    }

    private List<MindMapNodeViewModel> CloneRenderedNodes()
    {
        return _cards.Values
            .Select(card => new MindMapNodeViewModel
            {
                Id = card.Node.Id,
                ParentId = card.Node.ParentId,
                Title = card.Node.Title,
                Body = card.Node.Body,
                Level = card.Node.Level,
                IsRoot = card.Node.IsRoot,
                Position = card.Node.Position,
                IncomingParentSide = card.Node.IncomingParentSide,
                IncomingChildSide = card.Node.IncomingChildSide
            })
            .ToList();
    }

    private string FindClosestDropTarget(string activeNodeId)
    {
        if (!_cards.TryGetValue(activeNodeId, out var activeCard))
        {
            return string.Empty;
        }

        var activeRect = GetViewRect(activeNodeId);
        var activeCenter = activeRect.GetCenter();
        var bestNodeId = string.Empty;
        var bestScore = float.MaxValue;

        foreach (var candidate in _cards.Values)
        {
            if (candidate.Node.Id == activeNodeId)
            {
                continue;
            }

            var candidateRect = GetViewRect(candidate.Node.Id);
            var candidateCenter = candidateRect.GetCenter();
            var gapX = Math.Max(0f, Math.Max(activeRect.Position.X - candidateRect.End.X, candidateRect.Position.X - activeRect.End.X));
            var gapY = Math.Max(0f, Math.Max(activeRect.Position.Y - candidateRect.End.Y, candidateRect.Position.Y - activeRect.End.Y));
            var score = gapX + gapY * 0.4f + activeCenter.DistanceTo(candidateCenter) * 0.2f;
            if (score >= bestScore || score > 260f * _zoom)
            {
                continue;
            }

            bestScore = score;
            bestNodeId = candidate.Node.Id;
        }

        return bestNodeId;
    }

    private static Vector2 GetAnchorForSide(Rect2 rect, MindMapConnectorSide side, float inset)
    {
        return side switch
        {
            MindMapConnectorSide.Top => new Vector2(rect.Position.X + rect.Size.X / 2f, rect.Position.Y + inset),
            MindMapConnectorSide.Right => new Vector2(rect.End.X - inset, rect.Position.Y + rect.Size.Y / 2f),
            MindMapConnectorSide.Bottom => new Vector2(rect.Position.X + rect.Size.X / 2f, rect.End.Y - inset),
            MindMapConnectorSide.Left => new Vector2(rect.Position.X + inset, rect.Position.Y + rect.Size.Y / 2f),
            _ => rect.GetCenter()
        };
    }

    private static Vector2 GetDirectionalAnchor(Rect2 rect, Vector2 direction, float padding)
    {
        if (direction == Vector2.Zero)
        {
            return rect.GetCenter();
        }

        var normalized = direction.Normalized();
        return Math.Abs(normalized.X) >= Math.Abs(normalized.Y)
            ? GetAnchorForSide(rect, normalized.X >= 0f ? MindMapConnectorSide.Right : MindMapConnectorSide.Left, padding)
            : GetAnchorForSide(rect, normalized.Y >= 0f ? MindMapConnectorSide.Bottom : MindMapConnectorSide.Top, padding);
    }

    public async Task<Error> ExportRasterSnapshotAsync(string outputPath, string extension)
    {
        if (_cards.Count == 0)
        {
            return Error.Failed;
        }

        var logicalBounds = GetLogicalBounds();
        var exportSize = new Vector2(
            logicalBounds.Size.X * _zoom + ExportPadding * 2f,
            logicalBounds.Size.Y * _zoom + ExportPadding * 2f);
        var viewportSize = new Vector2I(
            Math.Max(1, Mathf.CeilToInt(exportSize.X)),
            Math.Max(1, Mathf.CeilToInt(exportSize.Y)));

        var viewport = new SubViewport
        {
            Name = "MindMapExportViewport",
            Size = viewportSize,
            TransparentBg = false,
            HandleInputLocally = false,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            RenderTargetClearMode = SubViewport.ClearMode.Always
        };
        viewport.SetSize2DOverride(viewportSize);

        var exportCanvas = new MindMapCanvas
        {
            Name = "MindMapExportCanvas",
            MouseFilter = MouseFilterEnum.Ignore
        };
        exportCanvas.SetAnchorsPreset(LayoutPreset.FullRect);
        exportCanvas.Size = viewportSize;
        exportCanvas.CustomMinimumSize = viewportSize;

        viewport.AddChild(exportCanvas);
        GetTree().Root.AddChild(viewport);

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

        exportCanvas._panOffset = new Vector2(ExportPadding, ExportPadding) - logicalBounds.Position * _zoom;
        exportCanvas.RenderNodes(CloneRenderedNodes(), _zoom);
        exportCanvas.Size = viewportSize;
        exportCanvas.CustomMinimumSize = viewportSize;

        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        RenderingServer.ForceDraw();

        var image = viewport.GetTexture().GetImage();
        viewport.QueueFree();
        if (image is null)
        {
            return Error.Failed;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            ? image.SaveJpg(outputPath, 0.92f)
            : image.SavePng(outputPath);
    }
}
