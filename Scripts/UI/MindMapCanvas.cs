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
    private const float ResizeEdgeHitThickness = 10f;

    private enum ResizeEdge
    {
        None,
        Left,
        Top,
        Right,
        Bottom
    }

    private sealed partial class HandleOverlay : Control
    {
        public MindMapCanvas? OwnerCanvas { get; set; }

        public HandleOverlay()
        {
            MouseFilter = MouseFilterEnum.Ignore;
        }

        public override void _Draw()
        {
            OwnerCanvas?.DrawVisibleHandles(this);
        }
    }

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
    public delegate void NodeResizedEventHandler(string nodeId, Vector2 position, Vector2 size);

    [Signal]
    public delegate void ZoomChangedEventHandler(float zoom);

    private sealed class NodeCardState
    {
        public required MindMapNodeViewModel Node { get; init; }

        public required PanelContainer Panel { get; init; }

        public required MarginContainer Margin { get; init; }

        public required Label TitleLabel { get; init; }

        public Label? BodyLabel { get; init; }

        public required Vector2 BaseSize { get; set; }
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
    private string _resizingNodeId = string.Empty;
    private ResizeEdge _resizingEdge = ResizeEdge.None;
    private Vector2 _resizeStartMousePosition;
    private Vector2 _resizeStartSize;
    private Vector2 _resizeStartPosition;
    private bool _isPanning;
    private Vector2 _panStartMousePosition;
    private Vector2 _panOffset = new(80f, 80f);
    private Vector2 _panStartOffset;
    private MindMapConnectorSide? _armedChildSide;
    private float _zoom = 1f;
    private string _dragPreviewTargetId = string.Empty;
    private HandleOverlay? _handleOverlay;

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
        _handleOverlay?.QueueRedraw();
    }

    public void RenderNodes(IReadOnlyList<MindMapNodeViewModel> nodes, float zoom)
    {
        _zoom = Math.Clamp(zoom, 0.5f, 2.5f);

        foreach (var child in GetChildren())
        {
            child.QueueFree();
        }

        _handleOverlay = null;
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
            var baseSize = EstimateNodeSize(node);
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

        EnsureHandleOverlay();

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

        if (button.Pressed)
        {
            if (TryHitHandle(button.Position, out var hitHandleNodeId, out var hitHandleSide))
            {
                OnHandlePressed(hitHandleNodeId, hitHandleSide);
                GetViewport().SetInputAsHandled();
                return;
            }

            if (TryHitResizeEdge(button.Position, out var resizeNodeId, out var resizeEdge))
            {
                _selectedNodeId = resizeNodeId;
                _resizingNodeId = resizeNodeId;
                _resizingEdge = resizeEdge;
                _resizeStartMousePosition = button.Position;
                _resizeStartSize = _cards[resizeNodeId].BaseSize;
                _resizeStartPosition = _cards[resizeNodeId].Node.Position;
                _armedChildSide = null;
                _dragPreviewTargetId = string.Empty;
                RefreshHandleVisibility();
                GetViewport().SetInputAsHandled();
                return;
            }

            var hitNodeId = HitTest(button.Position);
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
        if (!string.IsNullOrWhiteSpace(_resizingNodeId) && _cards.TryGetValue(_resizingNodeId, out var resizedCard))
        {
            var resizedNodeId = _resizingNodeId;
            _resizingNodeId = string.Empty;
            _resizingEdge = ResizeEdge.None;
            EmitSignal(SignalName.NodeResized, resizedNodeId, resizedCard.Node.Position, resizedCard.BaseSize);
            QueueRedraw();
            _handleOverlay?.QueueRedraw();
            GetViewport().SetInputAsHandled();
            return;
        }

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

        if (!string.IsNullOrWhiteSpace(_resizingNodeId) && _cards.TryGetValue(_resizingNodeId, out var resizingCard))
        {
            ApplyResize(resizingCard, motion.Position);
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
        _handleOverlay?.QueueRedraw();
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
        _handleOverlay?.QueueRedraw();
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

        return new NodeCardState
        {
            Node = node,
            Panel = panel,
            Margin = margin,
            TitleLabel = title,
            BodyLabel = body,
            BaseSize = baseSize
        };
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
        const int horizontalPadding = 16;
        const int verticalPadding = 14;
        var contentWidth = Math.Max(56f, state.BaseSize.X - horizontalPadding * 2f);

        state.Panel.Position = rect.Position;
        state.Panel.Size = state.BaseSize;
        state.Panel.CustomMinimumSize = state.BaseSize;
        state.Panel.Scale = new Vector2(_zoom, _zoom);
        state.Panel.PivotOffset = Vector2.Zero;
        state.Margin.AddThemeConstantOverride("margin_left", horizontalPadding);
        state.Margin.AddThemeConstantOverride("margin_top", verticalPadding);
        state.Margin.AddThemeConstantOverride("margin_right", horizontalPadding);
        state.Margin.AddThemeConstantOverride("margin_bottom", verticalPadding);
        var isSelected = state.Node.Id == _selectedNodeId;
        var isPreviewTarget = state.Node.Id == _dragPreviewTargetId && state.Node.Id != _draggingNodeId;
        state.Panel.AddThemeStyleboxOverride("panel", BuildStyle(state.Node.IsRoot, isSelected, isPreviewTarget));
        state.TitleLabel.AddThemeFontSizeOverride("font_size", state.Node.IsRoot ? 18 : 14);
        state.TitleLabel.AddThemeColorOverride("font_color", _titleTextColor);
        state.TitleLabel.CustomMinimumSize = new Vector2(contentWidth, 0f);

        if (state.BodyLabel is not null)
        {
            state.BodyLabel.AddThemeFontSizeOverride("font_size", 11);
            state.BodyLabel.AddThemeColorOverride("font_color", _bodyTextColor);
            state.BodyLabel.CustomMinimumSize = new Vector2(contentWidth, 0f);
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
        _handleOverlay?.QueueRedraw();
    }

    private void ApplyResize(NodeCardState state, Vector2 currentMousePosition)
    {
        var logicalDelta = (currentMousePosition - _resizeStartMousePosition) / _zoom;
        var minSize = GetMinimumNodeSize(state.Node);
        var nextPosition = _resizeStartPosition;
        var nextSize = _resizeStartSize;

        switch (_resizingEdge)
        {
            case ResizeEdge.Left:
            {
                var maxDelta = _resizeStartSize.X - minSize.X;
                var clampedDelta = Math.Clamp(logicalDelta.X, -100000f, maxDelta);
                nextPosition.X = _resizeStartPosition.X + clampedDelta;
                nextSize.X = _resizeStartSize.X - clampedDelta;
                break;
            }
            case ResizeEdge.Top:
            {
                var maxDelta = _resizeStartSize.Y - minSize.Y;
                var clampedDelta = Math.Clamp(logicalDelta.Y, -100000f, maxDelta);
                nextPosition.Y = _resizeStartPosition.Y + clampedDelta;
                nextSize.Y = _resizeStartSize.Y - clampedDelta;
                break;
            }
            case ResizeEdge.Right:
                nextSize.X = Math.Max(minSize.X, _resizeStartSize.X + logicalDelta.X);
                break;
            case ResizeEdge.Bottom:
                nextSize.Y = Math.Max(minSize.Y, _resizeStartSize.Y + logicalDelta.Y);
                break;
        }

        state.Node.Position = nextPosition;
        state.BaseSize = nextSize;
        state.Node.CustomSize = nextSize;
        ApplyCardVisual(state);
        QueueRedraw();
        _handleOverlay?.QueueRedraw();
    }

    private void RefreshHandleVisibility()
    {
        QueueRedraw();
        _handleOverlay?.QueueRedraw();
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

    private static Vector2 EstimateNodeSize(MindMapNodeViewModel node)
    {
        var width = node.CustomSize?.X ?? (node.IsRoot ? 260f : 220f);
        var titleLines = EstimateLineCount(node.Title, width - 32f);
        var bodyLines = string.IsNullOrWhiteSpace(node.Body) ? 0f : EstimateLineCount(node.Body, width - 32f);
        var height = 28f + titleLines * 22f + bodyLines * 18f + (bodyLines > 0 ? 10f : 0f);
        if (node.IsRoot)
        {
            height += 8f;
        }

        if (node.CustomSize is { } customSize)
        {
            height = Math.Max(height, customSize.Y);
        }

        return new Vector2(width, Math.Max(72f, height));
    }

    private static float EstimateLineCount(string text, float availableWidth)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 1f;
        }

        var length = text.Trim().Length;
        var charsPerLine = Math.Max(8f, availableWidth / 14f);
        return Math.Max(1f, (float)Math.Ceiling(length / charsPerLine));
    }

    private Rect2 GetLogicalRect(string nodeId)
    {
        var card = _cards[nodeId];
        return new Rect2(card.Node.Position, EstimateNodeSize(card.Node));
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
                CustomSize = card.BaseSize,
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

    private float GetHandleDiameter()
    {
        return 14f;
    }

    private void DrawVisibleHandles(CanvasItem target)
    {
        var diameter = GetHandleDiameter();
        var radius = diameter / 2f;

        foreach (var card in _cards.Values)
        {
            if (!ShouldDrawHandlesForNode(card.Node.Id))
            {
                continue;
            }

            var rect = GetViewRect(card.Node.Id);
            foreach (var side in Enum.GetValues<MindMapConnectorSide>())
            {
                var center = GetHandleCenter(rect, side, diameter);
                var isActive = card.Node.Id == _selectedNodeId && _armedChildSide == side;
                var fill = isActive ? _handleActiveStyle.BgColor : _handleStyle.BgColor;
                var border = isActive ? _handleActiveStyle.BorderColor : _handleStyle.BorderColor;
                target.DrawCircle(center, radius, fill);
                target.DrawArc(center, radius, 0f, Mathf.Tau, 24, border, 0.95f, true);
            }
        }

    }

    private bool ShouldDrawHandlesForNode(string nodeId)
    {
        var showOwnHandles = nodeId == _selectedNodeId;
        var showTargetHandles = _armedChildSide is not null && nodeId != _selectedNodeId;
        return showOwnHandles || showTargetHandles;
    }

    private bool TryHitHandle(Vector2 viewPosition, out string nodeId, out MindMapConnectorSide side)
    {
        var diameter = GetHandleDiameter();
        var radius = diameter / 2f;

        foreach (var card in _cards.Values.Reverse())
        {
            if (!ShouldDrawHandlesForNode(card.Node.Id))
            {
                continue;
            }

            var rect = GetViewRect(card.Node.Id);
            foreach (var currentSide in Enum.GetValues<MindMapConnectorSide>())
            {
                var center = GetHandleCenter(rect, currentSide, diameter);
                if (center.DistanceTo(viewPosition) <= radius)
                {
                    nodeId = card.Node.Id;
                    side = currentSide;
                    return true;
                }
            }
        }

        nodeId = string.Empty;
        side = MindMapConnectorSide.Top;
        return false;
    }

    private static Vector2 GetHandleCenter(Rect2 rect, MindMapConnectorSide side, float diameter)
    {
        return GetAnchorForSide(rect, side, 0f);
    }

    private bool TryHitResizeEdge(Vector2 viewPosition, out string nodeId, out ResizeEdge edge)
    {
        foreach (var card in _cards.Values.Reverse())
        {
            if (card.Node.Id != _selectedNodeId)
            {
                continue;
            }

            var rect = GetViewRect(card.Node.Id);
            edge = GetResizeEdgeHit(rect, viewPosition);
            if (edge != ResizeEdge.None)
            {
                nodeId = card.Node.Id;
                return true;
            }
        }

        nodeId = string.Empty;
        edge = ResizeEdge.None;
        return false;
    }

    private static ResizeEdge GetResizeEdgeHit(Rect2 rect, Vector2 point)
    {
        if (!rect.Grow(ResizeEdgeHitThickness).HasPoint(point))
        {
            return ResizeEdge.None;
        }

        var leftDistance = Math.Abs(point.X - rect.Position.X);
        var topDistance = Math.Abs(point.Y - rect.Position.Y);
        var rightDistance = Math.Abs(point.X - rect.End.X);
        var bottomDistance = Math.Abs(point.Y - rect.End.Y);
        var bestDistance = ResizeEdgeHitThickness + 1f;
        var edge = ResizeEdge.None;
        var handleGap = 18f;
        var centerX = rect.Position.X + rect.Size.X / 2f;
        var centerY = rect.Position.Y + rect.Size.Y / 2f;

        if (leftDistance <= ResizeEdgeHitThickness
            && leftDistance < bestDistance
            && Math.Abs(point.Y - centerY) > handleGap)
        {
            bestDistance = leftDistance;
            edge = ResizeEdge.Left;
        }
        if (topDistance <= ResizeEdgeHitThickness
            && topDistance < bestDistance
            && Math.Abs(point.X - centerX) > handleGap)
        {
            bestDistance = topDistance;
            edge = ResizeEdge.Top;
        }
        if (rightDistance <= ResizeEdgeHitThickness
            && rightDistance < bestDistance
            && Math.Abs(point.Y - centerY) > handleGap)
        {
            bestDistance = rightDistance;
            edge = ResizeEdge.Right;
        }
        if (bottomDistance <= ResizeEdgeHitThickness
            && bottomDistance < bestDistance
            && Math.Abs(point.X - centerX) > handleGap)
        {
            edge = ResizeEdge.Bottom;
        }

        return edge;
    }

    private static Vector2 GetMinimumNodeSize(MindMapNodeViewModel node)
    {
        var estimated = EstimateNodeSize(new MindMapNodeViewModel
        {
            Id = node.Id,
            ParentId = node.ParentId,
            Title = node.Title,
            Body = node.Body,
            Level = node.Level,
            Position = node.Position,
            CustomSize = null,
            IsRoot = node.IsRoot,
            IncomingParentSide = node.IncomingParentSide,
            IncomingChildSide = node.IncomingChildSide
        });

        return new Vector2(Math.Max(180f, estimated.X), Math.Max(72f, estimated.Y));
    }

    private static Vector2 SnapToPixel(Vector2 value)
    {
        return new Vector2(MathF.Round(value.X), MathF.Round(value.Y));
    }

    private void EnsureHandleOverlay()
    {
        _handleOverlay = new HandleOverlay
        {
            Name = "HandleOverlay",
            OwnerCanvas = this,
            Size = CustomMinimumSize,
            CustomMinimumSize = CustomMinimumSize,
            ZIndex = 100
        };
        _handleOverlay.SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(_handleOverlay);
        MoveChild(_handleOverlay, -1);
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
