using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Godot;
using RotomMindmap.Domain;
using RotomMindmap.Services;
using RotomMindmap.UI;
using System.Text;

namespace RotomMindmap;

public partial class Main : Control
{
    private const string MindMapRootNodeId = "__document_root__";
    private const string OutlineTitleEditorId = "__outline_title__";

    private enum PromptMode
    {
        None,
        CreateFolder,
        CreateDocument,
        RenameFolder,
        RenameDocument,
        RenameMindMapNode
    }

    private enum ExportRequestKind
    {
        None,
        OutlineMarkdown,
        Csv,
        MindMap
    }

    private enum LibraryMenuAction
    {
        NewDocument = 1,
        NewFolder = 2,
        Rename = 3,
        Delete = 4,
        OpenLocation = 5
    }

    private readonly LibraryService _libraryService = new();
    private readonly LocalizationService _localization = new();
    private readonly ThemeService _themeService = new();
    private readonly MarkdownStructureParser _structureParser = new();
    private readonly OutlineDocumentSerializer _outlineSerializer = new();
    private static readonly string[] CsvColumns = ["id", "parent_id", "level", "type", "title", "body", "order", "path"];
    private static readonly int[] CsvColumnMinimumWidths = [80, 92, 72, 84, 220, 260, 84, 220];
    private Color _primaryTextColor = new("233247");
    private Color _secondaryTextColor = new("6a7a90");
    private Color _subtleTextColor = new("8d9ab0");
    private readonly StyleBoxFlat _toolbarSurfaceStyle = new();
    private readonly StyleBoxFlat _inputSurfaceStyle = new();
    private readonly StyleBoxFlat _inputFocusSurfaceStyle = new();
    private readonly StyleBoxFlat _outlineItemStyle = new();
    private readonly StyleBoxFlat _outlineItemFocusStyle = new();
    private readonly StyleBoxFlat _treePanelStyle = new();
    private readonly StyleBoxFlat _trashPanelStyle = new();
    private readonly StyleBoxFlat _csvPanelStyle = new();
    private readonly StyleBoxFlat _csvHeaderStyle = new();
    private readonly StyleBoxFlat _csvHeaderHoverStyle = new();
    private readonly StyleBoxFlat _csvHeaderPressedStyle = new();
    private readonly StyleBoxFlat _csvRowSelectedStyle = new();
    private readonly StyleBoxFlat _csvRowSelectedFocusStyle = new();
    private readonly StyleBoxFlat _csvBodyPanelStyle = new();
    private readonly StyleBoxFlat _csvCellStyle = new();
    private readonly StyleBoxFlat _csvCellAltStyle = new();
    private readonly StyleBoxFlat _softButtonStyle = new();
    private readonly StyleBoxFlat _softButtonHoverStyle = new();
    private readonly StyleBoxFlat _softButtonDisabledStyle = new();
    private readonly StyleBoxFlat _destructiveButtonStyle = new();
    private readonly StyleBoxFlat _destructiveButtonDisabledStyle = new();
    private readonly StyleBoxFlat _treeSelectedStyle = new();
    private readonly StyleBoxFlat _treeCursorStyle = new();
    private readonly StyleBoxFlat _listSelectedStyle = new();
    private readonly PackedScene _libraryItemRowScene = GD.Load<PackedScene>("res://Scenes/Components/LibraryItemRow.tscn");
    private readonly PackedScene _outlineTitleRowScene = GD.Load<PackedScene>("res://Scenes/Components/OutlineTitleRow.tscn");
    private readonly PackedScene _outlineItemRowScene = GD.Load<PackedScene>("res://Scenes/Components/OutlineItemRow.tscn");

    private Tree _libraryTree = null!;
    private PanelContainer _libraryListPanel = null!;
    private ScrollContainer _libraryListScroll = null!;
    private VBoxContainer _libraryList = null!;
    private LibraryRootDropZone _libraryRootDropZone = null!;
    private ItemList _trashList = null!;
    private ScrollContainer _csvSheetScroll = null!;
    private VBoxContainer _csvSheetRows = null!;
    private Label _trashCountLabel = null!;
    private Label _saveStatusLabel = null!;
    private Label _statusLabel = null!;
    private Label _csvExportPathLabel = null!;
    private Label? _mindMapHintLabel;
    private Label? _outlineHintLabel;
    private LineEdit _searchBox = null!;
    private Button _contentSearchCheck = null!;
    private Timer _saveTimer = null!;
    private ConfirmationDialog _inputDialog = null!;
    private Label _inputDialogLabel = null!;
    private LineEdit _inputDialogLineEdit = null!;
    private ConfirmationDialog _deleteDialog = null!;
    private Label _deleteDialogLabel = null!;
    private AcceptDialog _infoDialog = null!;
    private FileDialog _exportFileDialog = null!;
    private Label _infoDialogLabel = null!;
    private TabContainer _mainTabs = null!;
    private VBoxContainer _outlineList = null!;
    private ScrollContainer _outlineScroll = null!;
    private MindMapCanvas _mindMapCanvas = null!;
    private PopupMenu _libraryContextMenu = null!;
    private Button _addMindMapChildButton = null!;
    private Button _addMindMapSiblingButton = null!;
    private Button _renameMindMapNodeButton = null!;
    private Button _deleteMindMapNodeButton = null!;
    private OptionButton? _localeSwitcher;
    private Label? _localeLabel;
    private OptionButton? _themeSwitcher;
    private Label? _themeLabel;

    private readonly List<OutlineItem> _outlineItems = [];
    private readonly Dictionary<string, LineEdit> _outlineEditors = new();

    private DocumentSnapshot? _currentDocument;
    private MindMapDocumentState _currentMindMapState = new();
    private List<TrashEntry> _trashEntries = [];
    private PromptMode _promptMode = PromptMode.None;
    private bool _suppressEvents;
    private bool _hasUnsavedDocumentChanges;
    private bool _hasUnsavedMindMapChanges;
    private bool _isLibraryDragActive;
    private bool _dragSourceIsDirectory;
    private string _pendingDeletePath = string.Empty;
    private bool _pendingDeleteIsDirectory;
    private string _pendingRenamePath = string.Empty;
    private bool _pendingRenameIsDirectory;
    private string _pendingTreeEditPath = string.Empty;
    private bool _pendingTreeEditIsDirectory;
    private string _dragSourceRelativePath = string.Empty;
    private string _focusedOutlineItemId = string.Empty;
    private string _outlineTitleText = string.Empty;
    private string _selectedLibraryPath = string.Empty;
    private bool _selectedLibraryIsDirectory;
    private readonly HashSet<string> _collapsedLibraryFolders = new(StringComparer.Ordinal);
    private ExportRequestKind _pendingExportRequest = ExportRequestKind.None;

    public override void _Ready()
    {
        CallDeferred(nameof(InitializeApp));
    }

    private void InitializeApp()
    {
        EnsureRuntimeOverlays();
        CacheNodes();
        _localization.Initialize();
        _themeService.Initialize();
        WireEvents();
        _libraryService.Initialize();
        ConfigureModernUi();
        ConfigureContextMenus();
        ConstrainViewportLayout();
        RefreshAll();
        SetStatusKey("status.workspace", ("path", _libraryService.WorkspaceRootPath));
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
        {
            SaveCurrentDocumentIfNeeded();
            GetTree().Quit();
            return;
        }

        if (what == NotificationDragEnd)
        {
            TryCompleteLibraryDragToRoot();
        }
    }

    private void CacheNodes()
    {
        _libraryTree = RequireNode<Tree>("LibraryTree");
        _libraryListPanel = RequireNode<PanelContainer>("LibraryListPanel");
        _libraryListScroll = RequireNode<ScrollContainer>("LibraryListScroll");
        _libraryList = RequireNode<VBoxContainer>("LibraryList");
        _libraryRootDropZone = EnsureLibraryRootDropZone();
        _trashList = RequireNode<ItemList>("TrashList");
        _trashCountLabel = EnsureLabel("TrashCountLabel", "SidebarPanel");
        _saveStatusLabel = EnsureLabel("SaveStatusLabel", "TopBar");
        _statusLabel = EnsureLabel("StatusLabel", "ContentPanel");
        _csvExportPathLabel = EnsureLabel("CsvExportPathLabel", "CsvTab");
        _mindMapHintLabel = TryNode<Label>("MindMapHint");
        _outlineHintLabel = TryNode<Label>("OutlineHintLabel");
        _searchBox = RequireNode<LineEdit>("SearchBox");
        _contentSearchCheck = RequireNode<Button>("ContentSearchCheck");
        _saveTimer = RequireNode<Timer>("SaveTimer");
        _inputDialog ??= GetNodeOrNull<ConfirmationDialog>("InputDialog")
            ?? GetNode<Node>("RuntimeOverlays").GetNodeOrNull<ConfirmationDialog>("InputDialog")
            ?? throw new InvalidOperationException("InputDialog was not initialized.");
        _inputDialogLabel ??= _inputDialog.GetNodeOrNull<Label>("InputDialogMargin/InputDialogBox/InputDialogLabel")
            ?? throw new InvalidOperationException("InputDialogLabel was not initialized.");
        _inputDialogLineEdit ??= _inputDialog.GetNodeOrNull<LineEdit>("InputDialogMargin/InputDialogBox/InputDialogLineEdit")
            ?? throw new InvalidOperationException("InputDialogLineEdit was not initialized.");
        _deleteDialog ??= GetNodeOrNull<ConfirmationDialog>("DeleteDialog")
            ?? GetNode<Node>("RuntimeOverlays").GetNodeOrNull<ConfirmationDialog>("DeleteDialog")
            ?? throw new InvalidOperationException("DeleteDialog was not initialized.");
        _deleteDialogLabel ??= _deleteDialog.GetNodeOrNull<Label>("DeleteDialogLabel")
            ?? throw new InvalidOperationException("DeleteDialogLabel was not initialized.");
        _infoDialog ??= GetNodeOrNull<AcceptDialog>("InfoDialog")
            ?? GetNode<Node>("RuntimeOverlays").GetNodeOrNull<AcceptDialog>("InfoDialog")
            ?? throw new InvalidOperationException("InfoDialog was not initialized.");
        _infoDialogLabel ??= _infoDialog.GetNodeOrNull<Label>("InfoDialogLabel")
            ?? throw new InvalidOperationException("InfoDialogLabel was not initialized.");
        _exportFileDialog ??= GetNodeOrNull<FileDialog>("ExportFileDialog")
            ?? GetNode<Node>("RuntimeOverlays").GetNodeOrNull<FileDialog>("ExportFileDialog")
            ?? throw new InvalidOperationException("ExportFileDialog was not initialized.");
        _mainTabs = RequireNode<TabContainer>("MainTabs");
        _outlineScroll = RequireNode<ScrollContainer>("OutlineScroll");
        _outlineList = RequireNode<VBoxContainer>("OutlineList");
        _mindMapCanvas = RequireNode<MindMapCanvas>("MindMapCanvas");
        (_csvSheetScroll, _csvSheetRows) = EnsureCsvSheetBody();
        _libraryContextMenu ??= GetNodeOrNull<PopupMenu>("LibraryContextMenu")
            ?? GetNode<Node>("RuntimeOverlays").GetNodeOrNull<PopupMenu>("LibraryContextMenu")
            ?? throw new InvalidOperationException("LibraryContextMenu was not initialized.");
        _addMindMapChildButton = RequireNode<Button>("AddMindMapChildButton");
        _addMindMapSiblingButton = RequireNode<Button>("AddMindMapSiblingButton");
        _renameMindMapNodeButton = RequireNode<Button>("RenameMindMapNodeButton");
        _deleteMindMapNodeButton = RequireNode<Button>("DeleteMindMapNodeButton");
        _localeSwitcher = TryNode<OptionButton>("LocaleSwitcher");
        _localeLabel = TryNode<Label>("LocaleLabel");
        _themeSwitcher = TryNode<OptionButton>("ThemeSwitcher");
        _themeLabel = TryNode<Label>("ThemeLabel");
        FindChild("AddOutlineItemButton", true, false)?.QueueFree();
    }

    private void EnsureRuntimeOverlays()
    {
        var overlayRoot = GetNode<Node>("RuntimeOverlays");

        if (GetNodeOrNull<ConfirmationDialog>("InputDialog") is null)
        {
            var inputDialog = new ConfirmationDialog
            {
                Name = "InputDialog"
            };
            overlayRoot.AddChild(inputDialog);
            _inputDialog = inputDialog;

            var margin = new MarginContainer
            {
                Name = "InputDialogMargin"
            };
            margin.AddThemeConstantOverride("margin_left", 16);
            margin.AddThemeConstantOverride("margin_top", 16);
            margin.AddThemeConstantOverride("margin_right", 16);
            margin.AddThemeConstantOverride("margin_bottom", 16);
            inputDialog.AddChild(margin);

            var box = new VBoxContainer
            {
                Name = "InputDialogBox"
            };
            box.AddThemeConstantOverride("separation", 12);
            margin.AddChild(box);

            _inputDialogLabel = new Label
            {
                Name = "InputDialogLabel",
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            box.AddChild(_inputDialogLabel);

            _inputDialogLineEdit = new LineEdit
            {
                Name = "InputDialogLineEdit"
            };
            box.AddChild(_inputDialogLineEdit);
        }

        if (GetNodeOrNull<ConfirmationDialog>("DeleteDialog") is null)
        {
            var deleteDialog = new ConfirmationDialog
            {
                Name = "DeleteDialog"
            };
            overlayRoot.AddChild(deleteDialog);
            _deleteDialog = deleteDialog;

            _deleteDialogLabel = new Label
            {
                Name = "DeleteDialogLabel",
                OffsetLeft = 16f,
                OffsetTop = 16f,
                OffsetRight = 420f,
                OffsetBottom = 100f,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            deleteDialog.AddChild(_deleteDialogLabel);
        }

        if (GetNodeOrNull<AcceptDialog>("InfoDialog") is null)
        {
            var infoDialog = new AcceptDialog
            {
                Name = "InfoDialog"
            };
            overlayRoot.AddChild(infoDialog);
            _infoDialog = infoDialog;

            _infoDialogLabel = new Label
            {
                Name = "InfoDialogLabel",
                OffsetLeft = 16f,
                OffsetTop = 16f,
                OffsetRight = 440f,
                OffsetBottom = 120f,
                AutowrapMode = TextServer.AutowrapMode.WordSmart
            };
            infoDialog.AddChild(_infoDialogLabel);
        }

        if (GetNodeOrNull<FileDialog>("ExportFileDialog") is null)
        {
            _exportFileDialog = new FileDialog
            {
                Name = "ExportFileDialog",
                FileMode = FileDialog.FileModeEnum.SaveFile,
                Access = FileDialog.AccessEnum.Filesystem,
                UseNativeDialog = true,
                Size = new Vector2I(920, 640)
            };
            overlayRoot.AddChild(_exportFileDialog);
        }

        if (GetNodeOrNull<PopupMenu>("LibraryContextMenu") is null)
        {
            _libraryContextMenu = new PopupMenu
            {
                Name = "LibraryContextMenu"
            };
            overlayRoot.AddChild(_libraryContextMenu);
        }
    }

    private void WireEvents()
    {
        BindButtonPressed("NewFolderButton", OnNewFolderPressed);
        BindButtonPressed("NewDocumentButton", OnNewDocumentPressed);
        BindButtonPressed("DeleteButton", OnDeletePressed);
        BindButtonPressed("ClearSearchButton", OnClearSearchPressed);
        BindButtonPressed("GenerateMindMapButton", OnGenerateMindMapPressed);
        BindButtonPressed("ExportOutlineMdButton", OnExportOutlineMdPressed);
        BindButtonPressed("ExportMindMapButton", OnExportMindMapPressed);
        BindButtonPressed("ExportCsvButton", OnExportCsvPressed);
        BindButtonPressed("RestoreButton", OnRestoreTrashPressed);
        BindButtonPressed("PurgeButton", OnPurgeTrashPressed);
        _addMindMapChildButton.Pressed += OnAddMindMapChildPressed;
        _addMindMapSiblingButton.Pressed += OnAddMindMapSiblingPressed;
        _renameMindMapNodeButton.Pressed += OnRenameMindMapNodePressed;
        _deleteMindMapNodeButton.Pressed += OnDeleteMindMapNodePressed;

        _searchBox.TextChanged += OnSearchChanged;
        _contentSearchCheck.Toggled += _ => RefreshLibraryView(GetCurrentSelectionPath());
        _saveTimer.Timeout += OnSaveTimerTimeout;
        _inputDialog.Confirmed += OnInputDialogConfirmed;
        _deleteDialog.Confirmed += OnDeleteDialogConfirmed;
        _exportFileDialog.FileSelected += OnExportFileSelected;
        _libraryTree.ItemSelected += OnLibraryTreeItemSelected;
        _libraryTree.ItemActivated += OnLibraryTreeItemActivated;
        _mainTabs.TabChanged += OnMainTabChanged;
        _libraryContextMenu.IdPressed += OnLibraryContextMenuIdPressed;
        _mindMapCanvas.NodeMoved += OnMindMapNodeMoved;
        _mindMapCanvas.NodeReparented += OnMindMapNodeReparented;
        _mindMapCanvas.ConnectionChanged += OnMindMapConnectionChanged;
        _mindMapCanvas.ZoomChanged += OnMindMapZoomChanged;
        if (GetWindow() is { } window)
        {
            window.FilesDropped += OnWindowFilesDropped;
        }
        if (_localeSwitcher is not null)
        {
            _localeSwitcher.ItemSelected += OnLocaleSelected;
        }
        if (_themeSwitcher is not null)
        {
            _themeSwitcher.ItemSelected += OnThemeSelected;
        }
        _libraryListPanel.GuiInput += OnLibraryListBackgroundGuiInput;
        _libraryListScroll.Resized += OnScrollContentViewportResized;
        _outlineScroll.Resized += OnScrollContentViewportResized;
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

    private T? TryNode<T>(string nodeName) where T : Node
    {
        var node = FindChild(nodeName, true, false);
        return node as T;
    }

    private void BindButtonPressed(string nodeName, Action handler)
    {
        if (TryNode<Button>(nodeName) is { } button)
        {
            button.Pressed += handler;
        }
    }

    private void SetLabelTextIfPresent(string nodeName, string text)
    {
        if (TryNode<Label>(nodeName) is { } label)
        {
            label.Text = text;
        }
    }

    private void SetButtonTextIfPresent(string nodeName, string text)
    {
        if (TryNode<Button>(nodeName) is { } button)
        {
            button.Text = text;
        }
    }

    private void ApplyLabelColorIfPresent(string nodeName, Color color)
    {
        if (TryNode<Label>(nodeName) is { } label)
        {
            label.AddThemeColorOverride("font_color", color);
        }
    }

    private Label EnsureLabel(string nodeName, string parentNodeName)
    {
        if (TryNode<Label>(nodeName) is { } existing)
        {
            return existing;
        }

        var parent = RequireNode<Control>(parentNodeName);
        var label = new Label
        {
            Name = nodeName
        };
        parent.AddChild(label);
        return label;
    }

    private LibraryRootDropZone EnsureLibraryRootDropZone()
    {
        var sidebarPanel = RequireNode<Control>("SidebarPanel");
        if (sidebarPanel.GetNodeOrNull<LibraryRootDropZone>("LibraryRootDropZone") is { } existing)
        {
            return existing;
        }

        var dropZone = new LibraryRootDropZone
        {
            Name = "LibraryRootDropZone"
        };
        dropZone.LayoutMode = 1;
        dropZone.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        dropZone.OffsetLeft = 0f;
        dropZone.OffsetTop = 0f;
        dropZone.OffsetRight = 0f;
        dropZone.OffsetBottom = 0f;
        dropZone.MouseFilter = Control.MouseFilterEnum.Stop;
        sidebarPanel.AddChild(dropZone);
        sidebarPanel.MoveChild(dropZone, sidebarPanel.GetChildCount() - 1);
        dropZone.LibraryItemMoveRequested += OnLibraryRowLibraryItemMoveRequested;
        return dropZone;
    }

    private (ScrollContainer Scroll, VBoxContainer Rows) EnsureCsvSheetBody()
    {
        if (TryNode<ScrollContainer>("CsvSheetScroll") is { } sceneScroll
            && sceneScroll.GetNodeOrNull<VBoxContainer>("CsvSheetRows") is { } sceneRows)
        {
            return (sceneScroll, sceneRows);
        }

        var csvTab = RequireNode<Control>("CsvTab");
        if (csvTab.GetNodeOrNull<ScrollContainer>("CsvSheetScroll") is { } existingScroll
            && existingScroll.GetNodeOrNull<VBoxContainer>("CsvSheetRows") is { } existingRows)
        {
            return (existingScroll, existingRows);
        }

        var scroll = new ScrollContainer
        {
            Name = "CsvSheetScroll",
            LayoutMode = 1
        };
        scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        scroll.OffsetTop = 52f;
        scroll.OffsetBottom = -40f;
        csvTab.AddChild(scroll);

        var rows = new VBoxContainer
        {
            Name = "CsvSheetRows",
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        rows.AddThemeConstantOverride("separation", 0);
        scroll.AddChild(rows);
        return (scroll, rows);
    }

    private void EnsureCsvBackgroundSurface()
    {
        var csvTab = RequireNode<Control>("CsvTab");
        var background = csvTab.GetNodeOrNull<ColorRect>("CsvBackgroundSurface");
        if (background is null)
        {
            background = new ColorRect
            {
                Name = "CsvBackgroundSurface",
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            csvTab.AddChild(background);
        }

        background.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        background.OffsetLeft = 0f;
        background.OffsetTop = 0f;
        background.OffsetRight = 0f;
        background.OffsetBottom = 0f;
        background.Color = _csvPanelStyle.BgColor;
        csvTab.MoveChild(background, 0);
    }

    private void ConfigureStaticUi()
    {
        _toolbarSurfaceStyle.BgColor = new Color("f3f5f8");
        _toolbarSurfaceStyle.BorderColor = new Color("d5dbe5");
        _toolbarSurfaceStyle.BorderWidthBottom = 1;
        _toolbarSurfaceStyle.ContentMarginLeft = 0;
        _toolbarSurfaceStyle.ContentMarginTop = 6;
        _toolbarSurfaceStyle.ContentMarginRight = 0;
        _toolbarSurfaceStyle.ContentMarginBottom = 6;

        _inputSurfaceStyle.BgColor = Colors.White;
        _inputSurfaceStyle.BorderColor = new Color("d6dde8");
        _inputSurfaceStyle.BorderWidthLeft = 1;
        _inputSurfaceStyle.BorderWidthTop = 1;
        _inputSurfaceStyle.BorderWidthRight = 1;
        _inputSurfaceStyle.BorderWidthBottom = 1;
        _inputSurfaceStyle.CornerRadiusTopLeft = 8;
        _inputSurfaceStyle.CornerRadiusTopRight = 8;
        _inputSurfaceStyle.CornerRadiusBottomLeft = 8;
        _inputSurfaceStyle.CornerRadiusBottomRight = 8;
        _inputSurfaceStyle.ContentMarginLeft = 12;
        _inputSurfaceStyle.ContentMarginTop = 10;
        _inputSurfaceStyle.ContentMarginRight = 12;
        _inputSurfaceStyle.ContentMarginBottom = 10;

        _inputFocusSurfaceStyle.BgColor = Colors.White;
        _inputFocusSurfaceStyle.BorderColor = new Color("5b7cfa");
        _inputFocusSurfaceStyle.BorderWidthLeft = 2;
        _inputFocusSurfaceStyle.BorderWidthTop = 2;
        _inputFocusSurfaceStyle.BorderWidthRight = 2;
        _inputFocusSurfaceStyle.BorderWidthBottom = 2;
        _inputFocusSurfaceStyle.CornerRadiusTopLeft = 8;
        _inputFocusSurfaceStyle.CornerRadiusTopRight = 8;
        _inputFocusSurfaceStyle.CornerRadiusBottomLeft = 8;
        _inputFocusSurfaceStyle.CornerRadiusBottomRight = 8;
        _inputFocusSurfaceStyle.ContentMarginLeft = 11;
        _inputFocusSurfaceStyle.ContentMarginTop = 9;
        _inputFocusSurfaceStyle.ContentMarginRight = 11;
        _inputFocusSurfaceStyle.ContentMarginBottom = 9;

        _mainTabs.SetTabTitle(0, "澶х翰");
        _mainTabs.SetTabTitle(1, "Mind Map");
        _mainTabs.SetTabTitle(2, "CSV");
        _libraryTree.AddThemeFontSizeOverride("font_size", 15);
        _libraryTree.HideRoot = false;
        ConfigureCsvSheetChrome();

        _outlineItemStyle.BgColor = new Color("f7f7f4");
        _outlineItemStyle.BgColor = new Color(1f, 1f, 1f, 0f);
        _outlineItemStyle.BorderWidthLeft = 0;
        _outlineItemStyle.BorderWidthTop = 0;
        _outlineItemStyle.BorderWidthRight = 0;
        _outlineItemStyle.BorderWidthBottom = 0;
        _outlineItemStyle.ContentMarginLeft = 0;
        _outlineItemStyle.ContentMarginTop = 4;
        _outlineItemStyle.ContentMarginRight = 0;
        _outlineItemStyle.ContentMarginBottom = 4;

        _outlineItemFocusStyle.BgColor = new Color("eef3ff");
        _outlineItemFocusStyle.BorderColor = new Color("5b7cfa");
        _outlineItemFocusStyle.BorderWidthLeft = 3;
        _outlineItemFocusStyle.BorderWidthTop = 0;
        _outlineItemFocusStyle.BorderWidthRight = 0;
        _outlineItemFocusStyle.BorderWidthBottom = 0;
        _outlineItemFocusStyle.ContentMarginLeft = 8;
        _outlineItemFocusStyle.ContentMarginTop = 4;
        _outlineItemFocusStyle.ContentMarginRight = 0;
        _outlineItemFocusStyle.ContentMarginBottom = 4;

        _inputDialogLineEdit.AddThemeStyleboxOverride("normal", _inputSurfaceStyle);
        _inputDialogLineEdit.AddThemeStyleboxOverride("focus", _inputFocusSurfaceStyle);
        _inputDialogLineEdit.AddThemeStyleboxOverride("read_only", _inputSurfaceStyle);
        _inputDialogLineEdit.AddThemeColorOverride("font_color", _primaryTextColor);
        _inputDialogLineEdit.AddThemeColorOverride("font_placeholder_color", _subtleTextColor);
        _searchBox.AddThemeStyleboxOverride("normal", _inputSurfaceStyle);
        _searchBox.AddThemeStyleboxOverride("focus", _inputFocusSurfaceStyle);
        _searchBox.AddThemeStyleboxOverride("read_only", _inputSurfaceStyle);
        _searchBox.AddThemeColorOverride("font_color", _primaryTextColor);
        _searchBox.AddThemeColorOverride("font_placeholder_color", _subtleTextColor);
        _saveStatusLabel.AddThemeColorOverride("font_color", _secondaryTextColor);
        _statusLabel.AddThemeColorOverride("font_color", _secondaryTextColor);
        _csvExportPathLabel.AddThemeColorOverride("font_color", _secondaryTextColor);
        _outlineHintLabel?.AddThemeColorOverride("font_color", _subtleTextColor);
        _mindMapHintLabel?.AddThemeColorOverride("font_color", _subtleTextColor);
        if (_mindMapHintLabel is not null)
        {
            _mindMapHintLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            _mindMapHintLabel.ClipText = false;
        }
        RequireNode<ScrollContainer>("MindMapScroll").OffsetTop = 52f;
        _contentSearchCheck.AddThemeColorOverride("font_color", _primaryTextColor);
        _contentSearchCheck.AddThemeColorOverride("font_pressed_color", _primaryTextColor);
        _contentSearchCheck.AddThemeColorOverride("font_hover_color", _primaryTextColor);
        RequireNode<Label>("BrandTitle")
            .AddThemeColorOverride("font_color", _primaryTextColor);
        TryNode<Label>("BrandSubtitle")
            ?.AddThemeColorOverride("font_color", _secondaryTextColor);
        RequireNode<Label>("VaultLabel")
            .AddThemeColorOverride("font_color", _primaryTextColor);
        RequireNode<Label>("SidebarMiniInfo")
            .AddThemeColorOverride("font_color", _secondaryTextColor);
        RequireNode<Label>("LibraryHeader")
            .AddThemeColorOverride("font_color", _secondaryTextColor);
        TryNode<Label>("SidebarRuleLabel")
            ?.AddThemeColorOverride("font_color", _secondaryTextColor);
        RequireNode<PanelContainer>("OutlineViewport")
            .AddThemeStyleboxOverride("panel", _toolbarSurfaceStyle);
        RequireNode<Control>("OutlineTab")
            .AddThemeStyleboxOverride("panel", _toolbarSurfaceStyle);
        TryNode<Label>("CsvHintLabel")
            ?.AddThemeColorOverride("font_color", _secondaryTextColor);
        TryNode<Label>("MindMapHint")
            ?.AddThemeColorOverride("font_color", _secondaryTextColor);
        RequireNode<Button>("TrashButton")
            .AddThemeColorOverride("font_color", _secondaryTextColor);
        RequireNode<Button>("DeleteButton")
            .AddThemeColorOverride("font_color", _secondaryTextColor);
        RequireNode<Button>("NewFolderButton")
            .AddThemeColorOverride("font_color", _secondaryTextColor);
        RequireNode<Button>("GenerateMindMapButton")
            .AddThemeColorOverride("font_color", _primaryTextColor);
        _outlineHintLabel?.AddThemeColorOverride("font_color", _subtleTextColor);
        _mindMapHintLabel?.AddThemeColorOverride("font_color", _subtleTextColor);
    }

    private void ConfigureModernUi()
    {
        _toolbarSurfaceStyle.BgColor = Colors.White;
        _toolbarSurfaceStyle.BorderColor = new Color("e1e9f2");
        _toolbarSurfaceStyle.BorderWidthLeft = 1;
        _toolbarSurfaceStyle.BorderWidthTop = 1;
        _toolbarSurfaceStyle.BorderWidthRight = 1;
        _toolbarSurfaceStyle.BorderWidthBottom = 1;
        _toolbarSurfaceStyle.CornerRadiusTopLeft = 18;
        _toolbarSurfaceStyle.CornerRadiusTopRight = 18;
        _toolbarSurfaceStyle.CornerRadiusBottomLeft = 18;
        _toolbarSurfaceStyle.CornerRadiusBottomRight = 18;
        _toolbarSurfaceStyle.ContentMarginTop = 10;
        _toolbarSurfaceStyle.ContentMarginBottom = 10;

        _inputSurfaceStyle.BgColor = Colors.White;
        _inputSurfaceStyle.BorderColor = new Color("d8e3ef");
        _inputSurfaceStyle.BorderWidthLeft = 1;
        _inputSurfaceStyle.BorderWidthTop = 1;
        _inputSurfaceStyle.BorderWidthRight = 1;
        _inputSurfaceStyle.BorderWidthBottom = 1;
        _inputSurfaceStyle.CornerRadiusTopLeft = 14;
        _inputSurfaceStyle.CornerRadiusTopRight = 14;
        _inputSurfaceStyle.CornerRadiusBottomLeft = 14;
        _inputSurfaceStyle.CornerRadiusBottomRight = 14;
        _inputSurfaceStyle.ContentMarginLeft = 14;
        _inputSurfaceStyle.ContentMarginTop = 10;
        _inputSurfaceStyle.ContentMarginRight = 14;
        _inputSurfaceStyle.ContentMarginBottom = 10;

        _inputFocusSurfaceStyle.BgColor = Colors.White;
        _inputFocusSurfaceStyle.BorderColor = new Color("7c9cff");
        _inputFocusSurfaceStyle.BorderWidthLeft = 2;
        _inputFocusSurfaceStyle.BorderWidthTop = 2;
        _inputFocusSurfaceStyle.BorderWidthRight = 2;
        _inputFocusSurfaceStyle.BorderWidthBottom = 2;
        _inputFocusSurfaceStyle.CornerRadiusTopLeft = 14;
        _inputFocusSurfaceStyle.CornerRadiusTopRight = 14;
        _inputFocusSurfaceStyle.CornerRadiusBottomLeft = 14;
        _inputFocusSurfaceStyle.CornerRadiusBottomRight = 14;
        _inputFocusSurfaceStyle.ContentMarginLeft = 13;
        _inputFocusSurfaceStyle.ContentMarginTop = 9;
        _inputFocusSurfaceStyle.ContentMarginRight = 13;
        _inputFocusSurfaceStyle.ContentMarginBottom = 9;

        _outlineItemStyle.BgColor = new Color(1f, 1f, 1f, 0f);
        _outlineItemStyle.BorderWidthLeft = 0;
        _outlineItemStyle.BorderWidthTop = 0;
        _outlineItemStyle.BorderWidthRight = 0;
        _outlineItemStyle.BorderWidthBottom = 0;
        _outlineItemStyle.ContentMarginLeft = 10;
        _outlineItemStyle.ContentMarginRight = 10;
        _outlineItemStyle.ContentMarginTop = 4;
        _outlineItemStyle.ContentMarginBottom = 4;

        _outlineItemFocusStyle.BgColor = new Color("f4f8ff");
        _outlineItemFocusStyle.BorderColor = new Color("d6e2f5");
        _outlineItemFocusStyle.BorderWidthLeft = 1;
        _outlineItemFocusStyle.BorderWidthTop = 1;
        _outlineItemFocusStyle.BorderWidthRight = 1;
        _outlineItemFocusStyle.BorderWidthBottom = 1;
        _outlineItemFocusStyle.CornerRadiusTopLeft = 14;
        _outlineItemFocusStyle.CornerRadiusTopRight = 14;
        _outlineItemFocusStyle.CornerRadiusBottomLeft = 14;
        _outlineItemFocusStyle.CornerRadiusBottomRight = 14;
        _outlineItemFocusStyle.ContentMarginLeft = 10;
        _outlineItemFocusStyle.ContentMarginRight = 10;
        _outlineItemFocusStyle.ContentMarginTop = 4;
        _outlineItemFocusStyle.ContentMarginBottom = 4;

        _treePanelStyle.BgColor = Colors.White;
        _treePanelStyle.BorderColor = new Color("dbe5f0");
        _treePanelStyle.BorderWidthLeft = 1;
        _treePanelStyle.BorderWidthTop = 1;
        _treePanelStyle.BorderWidthRight = 1;
        _treePanelStyle.BorderWidthBottom = 1;
        _treePanelStyle.CornerRadiusTopLeft = 18;
        _treePanelStyle.CornerRadiusTopRight = 18;
        _treePanelStyle.CornerRadiusBottomLeft = 18;
        _treePanelStyle.CornerRadiusBottomRight = 18;
        _treePanelStyle.ContentMarginLeft = 10;
        _treePanelStyle.ContentMarginTop = 10;
        _treePanelStyle.ContentMarginRight = 10;
        _treePanelStyle.ContentMarginBottom = 10;

        _trashPanelStyle.BgColor = Colors.White;
        _trashPanelStyle.BorderColor = new Color("dbe5f0");
        _trashPanelStyle.BorderWidthLeft = 1;
        _trashPanelStyle.BorderWidthTop = 1;
        _trashPanelStyle.BorderWidthRight = 1;
        _trashPanelStyle.BorderWidthBottom = 1;
        _trashPanelStyle.CornerRadiusTopLeft = 18;
        _trashPanelStyle.CornerRadiusTopRight = 18;
        _trashPanelStyle.CornerRadiusBottomLeft = 18;
        _trashPanelStyle.CornerRadiusBottomRight = 18;

        _csvPanelStyle.BgColor = Colors.White;
        _csvPanelStyle.BorderColor = new Color("dbe5f0");
        _csvPanelStyle.BorderWidthLeft = 1;
        _csvPanelStyle.BorderWidthTop = 1;
        _csvPanelStyle.BorderWidthRight = 1;
        _csvPanelStyle.BorderWidthBottom = 1;
        _csvPanelStyle.CornerRadiusTopLeft = 16;
        _csvPanelStyle.CornerRadiusTopRight = 16;
        _csvPanelStyle.CornerRadiusBottomLeft = 16;
        _csvPanelStyle.CornerRadiusBottomRight = 16;

        _csvHeaderStyle.BgColor = new Color("f7fbff");
        _csvHeaderStyle.BorderColor = new Color("d8e6f5");
        _csvHeaderStyle.BorderWidthLeft = 1;
        _csvHeaderStyle.BorderWidthTop = 1;
        _csvHeaderStyle.BorderWidthRight = 1;
        _csvHeaderStyle.BorderWidthBottom = 1;
        _csvHeaderStyle.CornerRadiusTopLeft = 10;
        _csvHeaderStyle.CornerRadiusTopRight = 10;
        _csvHeaderStyle.CornerRadiusBottomLeft = 0;
        _csvHeaderStyle.CornerRadiusBottomRight = 0;
        _csvHeaderStyle.ContentMarginLeft = 10;
        _csvHeaderStyle.ContentMarginTop = 8;
        _csvHeaderStyle.ContentMarginRight = 10;
        _csvHeaderStyle.ContentMarginBottom = 8;

        _csvHeaderHoverStyle.BgColor = new Color("eef6ff");
        _csvHeaderHoverStyle.BorderColor = new Color("c8daef");
        _csvHeaderHoverStyle.BorderWidthLeft = 1;
        _csvHeaderHoverStyle.BorderWidthTop = 1;
        _csvHeaderHoverStyle.BorderWidthRight = 1;
        _csvHeaderHoverStyle.BorderWidthBottom = 1;
        _csvHeaderHoverStyle.CornerRadiusTopLeft = 10;
        _csvHeaderHoverStyle.CornerRadiusTopRight = 10;
        _csvHeaderHoverStyle.CornerRadiusBottomLeft = 0;
        _csvHeaderHoverStyle.CornerRadiusBottomRight = 0;
        _csvHeaderHoverStyle.ContentMarginLeft = 10;
        _csvHeaderHoverStyle.ContentMarginTop = 8;
        _csvHeaderHoverStyle.ContentMarginRight = 10;
        _csvHeaderHoverStyle.ContentMarginBottom = 8;

        _csvHeaderPressedStyle.BgColor = new Color("e6f0ff");
        _csvHeaderPressedStyle.BorderColor = new Color("b8cdec");
        _csvHeaderPressedStyle.BorderWidthLeft = 1;
        _csvHeaderPressedStyle.BorderWidthTop = 1;
        _csvHeaderPressedStyle.BorderWidthRight = 1;
        _csvHeaderPressedStyle.BorderWidthBottom = 1;
        _csvHeaderPressedStyle.CornerRadiusTopLeft = 10;
        _csvHeaderPressedStyle.CornerRadiusTopRight = 10;
        _csvHeaderPressedStyle.CornerRadiusBottomLeft = 0;
        _csvHeaderPressedStyle.CornerRadiusBottomRight = 0;
        _csvHeaderPressedStyle.ContentMarginLeft = 10;
        _csvHeaderPressedStyle.ContentMarginTop = 8;
        _csvHeaderPressedStyle.ContentMarginRight = 10;
        _csvHeaderPressedStyle.ContentMarginBottom = 8;

        _csvRowSelectedStyle.BgColor = new Color("eef5ff");
        _csvRowSelectedStyle.BorderColor = new Color("c9ddf5");
        _csvRowSelectedStyle.BorderWidthLeft = 1;
        _csvRowSelectedStyle.BorderWidthTop = 1;
        _csvRowSelectedStyle.BorderWidthRight = 1;
        _csvRowSelectedStyle.BorderWidthBottom = 1;
        _csvRowSelectedStyle.CornerRadiusTopLeft = 6;
        _csvRowSelectedStyle.CornerRadiusTopRight = 6;
        _csvRowSelectedStyle.CornerRadiusBottomLeft = 6;
        _csvRowSelectedStyle.CornerRadiusBottomRight = 6;

        _csvRowSelectedFocusStyle.BgColor = new Color("e3efff");
        _csvRowSelectedFocusStyle.BorderColor = new Color("b9d0f1");
        _csvRowSelectedFocusStyle.BorderWidthLeft = 1;
        _csvRowSelectedFocusStyle.BorderWidthTop = 1;
        _csvRowSelectedFocusStyle.BorderWidthRight = 1;
        _csvRowSelectedFocusStyle.BorderWidthBottom = 1;
        _csvRowSelectedFocusStyle.CornerRadiusTopLeft = 6;
        _csvRowSelectedFocusStyle.CornerRadiusTopRight = 6;
        _csvRowSelectedFocusStyle.CornerRadiusBottomLeft = 6;
        _csvRowSelectedFocusStyle.CornerRadiusBottomRight = 6;

        _csvBodyPanelStyle.BgColor = Colors.White;
        _csvBodyPanelStyle.BorderColor = new Color("dfe5ec");
        _csvBodyPanelStyle.BorderWidthLeft = 1;
        _csvBodyPanelStyle.BorderWidthTop = 1;
        _csvBodyPanelStyle.BorderWidthRight = 1;
        _csvBodyPanelStyle.BorderWidthBottom = 1;
        _csvBodyPanelStyle.CornerRadiusTopLeft = 0;
        _csvBodyPanelStyle.CornerRadiusTopRight = 0;
        _csvBodyPanelStyle.CornerRadiusBottomLeft = 16;
        _csvBodyPanelStyle.CornerRadiusBottomRight = 16;

        _csvCellStyle.BgColor = Colors.White;
        _csvCellStyle.BorderColor = new Color("e3e8ef");
        _csvCellStyle.BorderWidthRight = 1;
        _csvCellStyle.BorderWidthBottom = 1;
        _csvCellStyle.ContentMarginLeft = 10;
        _csvCellStyle.ContentMarginTop = 8;
        _csvCellStyle.ContentMarginRight = 10;
        _csvCellStyle.ContentMarginBottom = 8;

        _csvCellAltStyle.BgColor = new Color("f8fafc");
        _csvCellAltStyle.BorderColor = new Color("e3e8ef");
        _csvCellAltStyle.BorderWidthRight = 1;
        _csvCellAltStyle.BorderWidthBottom = 1;
        _csvCellAltStyle.ContentMarginLeft = 10;
        _csvCellAltStyle.ContentMarginTop = 8;
        _csvCellAltStyle.ContentMarginRight = 10;
        _csvCellAltStyle.ContentMarginBottom = 8;

        _softButtonStyle.BgColor = Colors.White;
        _softButtonStyle.BorderColor = new Color("d8e2ef");
        _softButtonStyle.BorderWidthLeft = 1;
        _softButtonStyle.BorderWidthTop = 1;
        _softButtonStyle.BorderWidthRight = 1;
        _softButtonStyle.BorderWidthBottom = 1;
        _softButtonStyle.CornerRadiusTopLeft = 14;
        _softButtonStyle.CornerRadiusTopRight = 14;
        _softButtonStyle.CornerRadiusBottomLeft = 14;
        _softButtonStyle.CornerRadiusBottomRight = 14;
        _softButtonStyle.ContentMarginLeft = 12;
        _softButtonStyle.ContentMarginTop = 6;
        _softButtonStyle.ContentMarginRight = 12;
        _softButtonStyle.ContentMarginBottom = 6;

        _softButtonHoverStyle.BgColor = new Color("eef4ff");
        _softButtonHoverStyle.BorderColor = new Color("c6d7f3");
        _softButtonHoverStyle.BorderWidthLeft = 1;
        _softButtonHoverStyle.BorderWidthTop = 1;
        _softButtonHoverStyle.BorderWidthRight = 1;
        _softButtonHoverStyle.BorderWidthBottom = 1;
        _softButtonHoverStyle.CornerRadiusTopLeft = 14;
        _softButtonHoverStyle.CornerRadiusTopRight = 14;
        _softButtonHoverStyle.CornerRadiusBottomLeft = 14;
        _softButtonHoverStyle.CornerRadiusBottomRight = 14;
        _softButtonHoverStyle.ContentMarginLeft = 12;
        _softButtonHoverStyle.ContentMarginTop = 6;
        _softButtonHoverStyle.ContentMarginRight = 12;
        _softButtonHoverStyle.ContentMarginBottom = 6;

        _softButtonDisabledStyle.BgColor = new Color("f6f8fb");
        _softButtonDisabledStyle.BorderColor = new Color("e0e7f0");
        _softButtonDisabledStyle.BorderWidthLeft = 1;
        _softButtonDisabledStyle.BorderWidthTop = 1;
        _softButtonDisabledStyle.BorderWidthRight = 1;
        _softButtonDisabledStyle.BorderWidthBottom = 1;
        _softButtonDisabledStyle.CornerRadiusTopLeft = 14;
        _softButtonDisabledStyle.CornerRadiusTopRight = 14;
        _softButtonDisabledStyle.CornerRadiusBottomLeft = 14;
        _softButtonDisabledStyle.CornerRadiusBottomRight = 14;
        _softButtonDisabledStyle.ContentMarginLeft = 12;
        _softButtonDisabledStyle.ContentMarginTop = 6;
        _softButtonDisabledStyle.ContentMarginRight = 12;
        _softButtonDisabledStyle.ContentMarginBottom = 6;

        _destructiveButtonStyle.BgColor = new Color("fff6f3");
        _destructiveButtonStyle.BorderColor = new Color("efcfc4");
        _destructiveButtonStyle.BorderWidthLeft = 1;
        _destructiveButtonStyle.BorderWidthTop = 1;
        _destructiveButtonStyle.BorderWidthRight = 1;
        _destructiveButtonStyle.BorderWidthBottom = 1;
        _destructiveButtonStyle.CornerRadiusTopLeft = 14;
        _destructiveButtonStyle.CornerRadiusTopRight = 14;
        _destructiveButtonStyle.CornerRadiusBottomLeft = 14;
        _destructiveButtonStyle.CornerRadiusBottomRight = 14;
        _destructiveButtonStyle.ContentMarginLeft = 12;
        _destructiveButtonStyle.ContentMarginTop = 6;
        _destructiveButtonStyle.ContentMarginRight = 12;
        _destructiveButtonStyle.ContentMarginBottom = 6;

        _destructiveButtonDisabledStyle.BgColor = new Color("f9f6f5");
        _destructiveButtonDisabledStyle.BorderColor = new Color("ebe2df");
        _destructiveButtonDisabledStyle.BorderWidthLeft = 1;
        _destructiveButtonDisabledStyle.BorderWidthTop = 1;
        _destructiveButtonDisabledStyle.BorderWidthRight = 1;
        _destructiveButtonDisabledStyle.BorderWidthBottom = 1;
        _destructiveButtonDisabledStyle.CornerRadiusTopLeft = 14;
        _destructiveButtonDisabledStyle.CornerRadiusTopRight = 14;
        _destructiveButtonDisabledStyle.CornerRadiusBottomLeft = 14;
        _destructiveButtonDisabledStyle.CornerRadiusBottomRight = 14;
        _destructiveButtonDisabledStyle.ContentMarginLeft = 12;
        _destructiveButtonDisabledStyle.ContentMarginTop = 6;
        _destructiveButtonDisabledStyle.ContentMarginRight = 12;
        _destructiveButtonDisabledStyle.ContentMarginBottom = 6;

        _treeSelectedStyle.BgColor = new Color("edf4ff");
        _treeSelectedStyle.BorderColor = new Color("d5e1f7");
        _treeSelectedStyle.BorderWidthLeft = 1;
        _treeSelectedStyle.BorderWidthTop = 1;
        _treeSelectedStyle.BorderWidthRight = 1;
        _treeSelectedStyle.BorderWidthBottom = 1;
        _treeSelectedStyle.CornerRadiusTopLeft = 12;
        _treeSelectedStyle.CornerRadiusTopRight = 12;
        _treeSelectedStyle.CornerRadiusBottomLeft = 12;
        _treeSelectedStyle.CornerRadiusBottomRight = 12;

        _treeCursorStyle.BgColor = new Color(1f, 1f, 1f, 0f);
        _treeCursorStyle.BorderColor = new Color("b7c9ea");
        _treeCursorStyle.BorderWidthLeft = 1;
        _treeCursorStyle.BorderWidthTop = 1;
        _treeCursorStyle.BorderWidthRight = 1;
        _treeCursorStyle.BorderWidthBottom = 1;
        _treeCursorStyle.CornerRadiusTopLeft = 12;
        _treeCursorStyle.CornerRadiusTopRight = 12;
        _treeCursorStyle.CornerRadiusBottomLeft = 12;
        _treeCursorStyle.CornerRadiusBottomRight = 12;

        _listSelectedStyle.BgColor = new Color("edf4ff");
        _listSelectedStyle.BorderColor = new Color("d5e1f7");
        _listSelectedStyle.BorderWidthLeft = 1;
        _listSelectedStyle.BorderWidthTop = 1;
        _listSelectedStyle.BorderWidthRight = 1;
        _listSelectedStyle.BorderWidthBottom = 1;
        _listSelectedStyle.CornerRadiusTopLeft = 12;
        _listSelectedStyle.CornerRadiusTopRight = 12;
        _listSelectedStyle.CornerRadiusBottomLeft = 12;
        _listSelectedStyle.CornerRadiusBottomRight = 12;

        var shellStyle = new StyleBoxFlat
        {
            BgColor = Colors.White,
            BorderColor = new Color("dbe5f0"),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 20,
            CornerRadiusTopRight = 20,
            CornerRadiusBottomLeft = 20,
            CornerRadiusBottomRight = 20
        };

        var sidebarShellStyle = new StyleBoxFlat
        {
            BgColor = new Color("f3f7fc"),
            BorderColor = new Color("dbe5f0"),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 20,
            CornerRadiusTopRight = 20,
            CornerRadiusBottomLeft = 20,
            CornerRadiusBottomRight = 20
        };

        var topBarShellStyle = new StyleBoxFlat
        {
            BgColor = Colors.White,
            BorderColor = new Color("dbe5f0"),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 16,
            CornerRadiusTopRight = 16,
            CornerRadiusBottomLeft = 16,
            CornerRadiusBottomRight = 16
        };

        var tabSelectedStyle = new StyleBoxFlat
        {
            BgColor = new Color("edf4ff"),
            BorderColor = new Color("cedcf3"),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 12,
            CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12,
            CornerRadiusBottomRight = 12,
            ContentMarginLeft = 14,
            ContentMarginTop = 8,
            ContentMarginRight = 14,
            ContentMarginBottom = 8
        };

        var tabUnselectedStyle = new StyleBoxFlat
        {
            BgColor = new Color("f6f8fb"),
            BorderColor = new Color("dbe5f0"),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 12,
            CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12,
            CornerRadiusBottomRight = 12,
            ContentMarginLeft = 14,
            ContentMarginTop = 8,
            ContentMarginRight = 14,
            ContentMarginBottom = 8
        };

        var tabHoverStyle = new StyleBoxFlat
        {
            BgColor = new Color("f1f6ff"),
            BorderColor = new Color("c9d8f1"),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 12,
            CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12,
            CornerRadiusBottomRight = 12,
            ContentMarginLeft = 14,
            ContentMarginTop = 8,
            ContentMarginRight = 14,
            ContentMarginBottom = 8
        };

        RequireNode<Panel>("TopBar").AddThemeStyleboxOverride("panel", topBarShellStyle);
        RequireNode<Panel>("SidebarPanel").AddThemeStyleboxOverride("panel", sidebarShellStyle);
        RequireNode<Panel>("ContentPanel").AddThemeStyleboxOverride("panel", shellStyle);
        _mainTabs.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(1f, 1f, 1f, 0f) });
        _mainTabs.AddThemeStyleboxOverride("tab_selected", tabSelectedStyle);
        _mainTabs.AddThemeStyleboxOverride("tab_unselected", tabUnselectedStyle);
        _mainTabs.AddThemeStyleboxOverride("tab_hovered", tabHoverStyle);
        _mainTabs.AddThemeStyleboxOverride("tab_disabled", tabUnselectedStyle);
        _mainTabs.AddThemeColorOverride("font_selected_color", _primaryTextColor);
        _mainTabs.AddThemeColorOverride("font_unselected_color", _secondaryTextColor);
        _mainTabs.AddThemeColorOverride("font_hovered_color", _primaryTextColor);
        _mainTabs.AddThemeColorOverride("font_disabled_color", _subtleTextColor);
        _mainTabs.AddThemeConstantOverride("icon_separation", 8);

        _libraryTree.AddThemeFontSizeOverride("font_size", 15);
        _libraryTree.HideRoot = true;
        _libraryTree.AddThemeStyleboxOverride("panel", _treePanelStyle);
        _libraryTree.AddThemeStyleboxOverride("selected", _treeSelectedStyle);
        _libraryTree.AddThemeStyleboxOverride("selected_focus", _treeSelectedStyle);
        _libraryTree.AddThemeStyleboxOverride("cursor", _treeCursorStyle);
        _libraryTree.AddThemeStyleboxOverride("cursor_unfocused", _treeCursorStyle);
        _libraryTree.AddThemeColorOverride("font_color", _primaryTextColor);
        _libraryTree.AddThemeColorOverride("guide_color", new Color("e4ebf4"));
        _libraryListPanel.AddThemeStyleboxOverride("panel", _treePanelStyle);

        _trashList.AddThemeStyleboxOverride("panel", _trashPanelStyle);
        _trashList.AddThemeStyleboxOverride("focus", _treeCursorStyle);
        _trashList.AddThemeStyleboxOverride("selected", _listSelectedStyle);
        _trashList.AddThemeColorOverride("font_color", _primaryTextColor);
        _trashList.AddThemeColorOverride("font_selected_color", _primaryTextColor);

        _inputDialogLineEdit.AddThemeStyleboxOverride("normal", _inputSurfaceStyle);
        _inputDialogLineEdit.AddThemeStyleboxOverride("focus", _inputFocusSurfaceStyle);
        _inputDialogLineEdit.AddThemeStyleboxOverride("read_only", _inputSurfaceStyle);
        _inputDialogLineEdit.AddThemeColorOverride("font_color", _primaryTextColor);
        _inputDialogLineEdit.AddThemeColorOverride("font_placeholder_color", _subtleTextColor);
        _searchBox.AddThemeStyleboxOverride("normal", _inputSurfaceStyle);
        _searchBox.AddThemeStyleboxOverride("focus", _inputFocusSurfaceStyle);
        _searchBox.AddThemeStyleboxOverride("read_only", _inputSurfaceStyle);
        _searchBox.AddThemeColorOverride("font_color", _primaryTextColor);
        _searchBox.AddThemeColorOverride("font_placeholder_color", _subtleTextColor);
        _saveStatusLabel.AddThemeColorOverride("font_color", _secondaryTextColor);
        _statusLabel.AddThemeColorOverride("font_color", _secondaryTextColor);
        _csvExportPathLabel.AddThemeColorOverride("font_color", _secondaryTextColor);
        _outlineHintLabel?.AddThemeColorOverride("font_color", _subtleTextColor);
        _mindMapHintLabel?.AddThemeColorOverride("font_color", _subtleTextColor);
        _contentSearchCheck.AddThemeColorOverride("font_color", _primaryTextColor);
        _contentSearchCheck.AddThemeColorOverride("font_pressed_color", _primaryTextColor);
        _localeLabel?.AddThemeColorOverride("font_color", _secondaryTextColor);
        _localeSwitcher?.AddThemeStyleboxOverride("normal", _softButtonStyle);
        _localeSwitcher?.AddThemeStyleboxOverride("hover", _softButtonHoverStyle);
        _localeSwitcher?.AddThemeStyleboxOverride("pressed", _softButtonHoverStyle);
        _localeSwitcher?.AddThemeStyleboxOverride("focus", _softButtonStyle);
        _localeSwitcher?.AddThemeColorOverride("font_color", _primaryTextColor);
        ConfigureOverlayDialogs();
        _libraryContextMenu.AddThemeStyleboxOverride("panel", _treePanelStyle);
        _libraryContextMenu.AddThemeColorOverride("font_color", _primaryTextColor);
        _libraryContextMenu.AddThemeColorOverride("font_hover_color", _primaryTextColor);
        _libraryContextMenu.AddThemeColorOverride("font_disabled_color", _subtleTextColor);
        _libraryContextMenu.AddThemeColorOverride("separator_color", new Color("dbe5f0"));

        ApplyLabelColorIfPresent("BrandTitle", _primaryTextColor);
        ApplyLabelColorIfPresent("BrandSubtitle", _secondaryTextColor);
        ApplyLabelColorIfPresent("VaultLabel", _primaryTextColor);
        ApplyLabelColorIfPresent("SidebarMiniInfo", _secondaryTextColor);
        ApplyLabelColorIfPresent("LibraryHeader", _secondaryTextColor);
        ApplyLabelColorIfPresent("SidebarRuleLabel", _secondaryTextColor);
        ApplyLabelColorIfPresent("TrashLabel", _primaryTextColor);
        RequireNode<PanelContainer>("OutlineViewport")
            .AddThemeStyleboxOverride("panel", _toolbarSurfaceStyle);

        if (TryNode<Button>("ClearSearchButton") is { } clearSearchButton)
        {
            ApplySoftButtonStyle(clearSearchButton);
        }
        ApplySoftButtonStyle(_contentSearchCheck);
        if (TryNode<Button>("NewFolderButton") is { } newFolderButton)
        {
            ApplySoftButtonStyle(newFolderButton);
        }
        if (TryNode<Button>("DeleteButton") is { } deleteButton)
        {
            ApplySoftButtonStyle(deleteButton, useDestructiveStyle: true);
        }
        if (TryNode<Button>("RestoreButton") is { } restoreButton)
        {
            ApplySoftButtonStyle(restoreButton);
        }
        if (TryNode<Button>("PurgeButton") is { } purgeButton)
        {
            ApplySoftButtonStyle(purgeButton, useDestructiveStyle: true);
        }
        if (TryNode<Button>("AddMindMapChildButton") is { } addChildButton)
        {
            ApplySoftButtonStyle(addChildButton);
        }
        if (TryNode<Button>("AddMindMapSiblingButton") is { } addSiblingButton)
        {
            ApplySoftButtonStyle(addSiblingButton);
        }
        if (TryNode<Button>("RenameMindMapNodeButton") is { } renameMindNodeButton)
        {
            ApplySoftButtonStyle(renameMindNodeButton);
        }
        if (TryNode<Button>("DeleteMindMapNodeButton") is { } deleteMindNodeButton)
        {
            ApplySoftButtonStyle(deleteMindNodeButton, useDestructiveStyle: true);
        }
        if (TryNode<Button>("GenerateMindMapButton") is { } generateMindMapButton)
        {
            ApplySoftButtonStyle(generateMindMapButton);
        }
        if (TryNode<Button>("ExportOutlineMdButton") is { } exportOutlineMdButton)
        {
            ApplySoftButtonStyle(exportOutlineMdButton);
        }
        if (TryNode<Button>("ExportMindMapButton") is { } exportMindMapButton)
        {
            ApplySoftButtonStyle(exportMindMapButton);
        }
        if (TryNode<Button>("ExportCsvButton") is { } exportCsvButton)
        {
            ApplySoftButtonStyle(exportCsvButton);
        }
        if (TryNode<Button>("NewDocumentButton") is { } newDocumentButton)
        {
            ApplyPrimaryButtonStyle(newDocumentButton);
        }

        ConfigureThemeSwitcher();
        ConfigureLocaleSwitcher();
        ApplyLocalizedText();
    }

    private void ConfigureThemeSwitcher()
    {
        if (_themeSwitcher is null)
        {
            return;
        }

        _suppressEvents = true;
        _themeSwitcher.Clear();
        _themeSwitcher.AddItem(T("theme.system"));
        _themeSwitcher.AddItem(T("theme.light"));
        _themeSwitcher.AddItem(T("theme.dark"));

        var currentMode = LoadThemeMode();
        _themeSwitcher.Select((int)currentMode);
        _suppressEvents = false;
        ApplyThemeMode(currentMode);
    }

    private ThemeMode LoadThemeMode()
    {
        return _themeService.CurrentMode;
    }

    private void SaveThemeMode(ThemeMode mode)
    {
        _themeService.SetMode(mode);
        ApplyThemeMode(mode);
    }

    private void ApplyThemeMode(ThemeMode mode)
    {
        var effectiveMode = mode == ThemeMode.System ? (_themeService.ResolveEffectiveMode()) : mode;
        var isDark = effectiveMode == ThemeMode.Dark;

        ApplyThemePalette(isDark);
    }

    private void ApplyThemePalette(bool isDark)
    {
        var background = isDark ? new Color("111827") : new Color("f5f8fc");
        var panel = isDark ? new Color("17212f") : Colors.White;
        var panelSoft = isDark ? new Color("1e293b") : new Color("f3f7fc");
        var border = isDark ? new Color("334155") : new Color("dbe5f0");
        var borderSoft = isDark ? new Color("2a3a4f") : new Color("e1e9f2");
        var primaryText = isDark ? new Color("f8fafc") : new Color("233247");
        var secondaryText = isDark ? new Color("cbd5e1") : new Color("6a7a90");
        var subtleText = isDark ? new Color("94a3b8") : new Color("8d9ab0");
        var inputBg = isDark ? new Color("0f172a") : Colors.White;
        var inputHover = isDark ? new Color("1e293b") : new Color("eef4ff");
        var selected = isDark ? new Color("24324a") : new Color("edf4ff");
        var selectedBorder = isDark ? new Color("475569") : new Color("cedcf3");
        var cellAlt = isDark ? new Color("0f172a") : new Color("f8fafc");
        var listBg = isDark ? new Color("101826") : new Color("f3f7fc");
        var guideColor = isDark ? new Color("334155") : new Color("e4ebf4");
        var destructiveText = isDark ? new Color("f3b1a5") : new Color("b35a48");

        if (TryNode<ColorRect>("Background") is { } backgroundRect)
        {
            backgroundRect.Color = background;
        }

        _toolbarSurfaceStyle.BgColor = panel;
        _toolbarSurfaceStyle.BorderColor = borderSoft;
        _inputSurfaceStyle.BgColor = inputBg;
        _inputSurfaceStyle.BorderColor = border;
        _inputFocusSurfaceStyle.BgColor = inputBg;
        _inputFocusSurfaceStyle.BorderColor = isDark ? new Color("7d9cff") : new Color("7c9cff");
        _outlineItemStyle.BgColor = new Color(1f, 1f, 1f, 0f);
        _outlineItemFocusStyle.BgColor = inputHover;
        _outlineItemFocusStyle.BorderColor = border;
        _treePanelStyle.BgColor = listBg;
        _treePanelStyle.BorderColor = border;
        _trashPanelStyle.BgColor = listBg;
        _trashPanelStyle.BorderColor = border;
        _csvPanelStyle.BgColor = panel;
        _csvPanelStyle.BorderColor = border;
        _csvHeaderStyle.BgColor = isDark ? new Color("1e2a3c") : new Color("f7fbff");
        _csvHeaderStyle.BorderColor = border;
        _csvHeaderHoverStyle.BgColor = isDark ? new Color("253247") : new Color("eef6ff");
        _csvHeaderHoverStyle.BorderColor = border;
        _csvHeaderPressedStyle.BgColor = isDark ? new Color("2a3a52") : new Color("e6f0ff");
        _csvHeaderPressedStyle.BorderColor = border;
        _csvRowSelectedStyle.BgColor = selected;
        _csvRowSelectedStyle.BorderColor = selectedBorder;
        _csvRowSelectedFocusStyle.BgColor = isDark ? new Color("24324a") : new Color("e3efff");
        _csvRowSelectedFocusStyle.BorderColor = selectedBorder;
        _csvBodyPanelStyle.BgColor = panel;
        _csvBodyPanelStyle.BorderColor = border;
        _csvCellStyle.BgColor = panel;
        _csvCellStyle.BorderColor = borderSoft;
        _csvCellAltStyle.BgColor = cellAlt;
        _csvCellAltStyle.BorderColor = borderSoft;
        _softButtonStyle.BgColor = panel;
        _softButtonStyle.BorderColor = border;
        _softButtonHoverStyle.BgColor = inputHover;
        _softButtonHoverStyle.BorderColor = border;
        _softButtonDisabledStyle.BgColor = isDark ? new Color("1f2937") : new Color("f6f8fb");
        _softButtonDisabledStyle.BorderColor = borderSoft;
        _destructiveButtonStyle.BgColor = isDark ? new Color("2f1f22") : new Color("fff6f3");
        _destructiveButtonStyle.BorderColor = isDark ? new Color("6d3f45") : new Color("efcfc4");
        _destructiveButtonDisabledStyle.BgColor = isDark ? new Color("23181a") : new Color("f9f6f5");
        _destructiveButtonDisabledStyle.BorderColor = isDark ? new Color("4c373a") : new Color("ebe2df");
        _treeSelectedStyle.BgColor = selected;
        _treeSelectedStyle.BorderColor = selectedBorder;
        _treeCursorStyle.BorderColor = border;
        _listSelectedStyle.BgColor = selected;
        _listSelectedStyle.BorderColor = selectedBorder;

        var shellStyle = new StyleBoxFlat
        {
            BgColor = panel,
            BorderColor = border,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 20,
            CornerRadiusTopRight = 20,
            CornerRadiusBottomLeft = 20,
            CornerRadiusBottomRight = 20
        };

        var sidebarShellStyle = new StyleBoxFlat
        {
            BgColor = panelSoft,
            BorderColor = border,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 20,
            CornerRadiusTopRight = 20,
            CornerRadiusBottomLeft = 20,
            CornerRadiusBottomRight = 20
        };

        var topBarShellStyle = new StyleBoxFlat
        {
            BgColor = panel,
            BorderColor = border,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 16,
            CornerRadiusTopRight = 16,
            CornerRadiusBottomLeft = 16,
            CornerRadiusBottomRight = 16
        };

        _primaryTextColor = primaryText;
        _secondaryTextColor = secondaryText;
        _subtleTextColor = subtleText;

        RequireNode<Panel>("TopBar").AddThemeStyleboxOverride("panel", topBarShellStyle);
        RequireNode<Panel>("SidebarPanel").AddThemeStyleboxOverride("panel", sidebarShellStyle);
        RequireNode<Panel>("ContentPanel").AddThemeStyleboxOverride("panel", shellStyle);
        var tabSelectedStyle = new StyleBoxFlat
        {
            BgColor = selected,
            BorderColor = selectedBorder,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 12,
            CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12,
            CornerRadiusBottomRight = 12,
            ContentMarginLeft = 14,
            ContentMarginTop = 8,
            ContentMarginRight = 14,
            ContentMarginBottom = 8
        };
        var tabUnselectedStyle = new StyleBoxFlat
        {
            BgColor = panelSoft,
            BorderColor = border,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 12,
            CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12,
            CornerRadiusBottomRight = 12,
            ContentMarginLeft = 14,
            ContentMarginTop = 8,
            ContentMarginRight = 14,
            ContentMarginBottom = 8
        };
        var tabHoverStyle = new StyleBoxFlat
        {
            BgColor = inputHover,
            BorderColor = border,
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 12,
            CornerRadiusTopRight = 12,
            CornerRadiusBottomLeft = 12,
            CornerRadiusBottomRight = 12,
            ContentMarginLeft = 14,
            ContentMarginTop = 8,
            ContentMarginRight = 14,
            ContentMarginBottom = 8
        };
        _mainTabs.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(1f, 1f, 1f, 0f) });
        _mainTabs.AddThemeStyleboxOverride("tab_selected", tabSelectedStyle);
        _mainTabs.AddThemeStyleboxOverride("tab_unselected", tabUnselectedStyle);
        _mainTabs.AddThemeStyleboxOverride("tab_hovered", tabHoverStyle);
        _mainTabs.AddThemeStyleboxOverride("tab_disabled", tabUnselectedStyle);
        _mainTabs.AddThemeColorOverride("font_selected_color", primaryText);
        _mainTabs.AddThemeColorOverride("font_unselected_color", secondaryText);
        _mainTabs.AddThemeColorOverride("font_hovered_color", primaryText);
        _mainTabs.AddThemeColorOverride("font_disabled_color", subtleText);

        _inputDialogLineEdit.AddThemeStyleboxOverride("normal", _inputSurfaceStyle);
        _inputDialogLineEdit.AddThemeStyleboxOverride("focus", _inputFocusSurfaceStyle);
        _inputDialogLineEdit.AddThemeStyleboxOverride("read_only", _inputSurfaceStyle);
        _inputDialogLineEdit.AddThemeColorOverride("font_color", primaryText);
        _inputDialogLineEdit.AddThemeColorOverride("font_placeholder_color", subtleText);
        _searchBox.AddThemeStyleboxOverride("normal", _inputSurfaceStyle);
        _searchBox.AddThemeStyleboxOverride("focus", _inputFocusSurfaceStyle);
        _searchBox.AddThemeStyleboxOverride("read_only", _inputSurfaceStyle);
        _searchBox.AddThemeColorOverride("font_color", primaryText);
        _searchBox.AddThemeColorOverride("font_placeholder_color", subtleText);
        _libraryTree.AddThemeColorOverride("font_color", primaryText);
        _libraryTree.AddThemeColorOverride("guide_color", guideColor);
        _trashList.AddThemeColorOverride("font_color", primaryText);
        _trashList.AddThemeColorOverride("font_selected_color", primaryText);
        _saveStatusLabel.AddThemeColorOverride("font_color", secondaryText);
        _statusLabel.AddThemeColorOverride("font_color", secondaryText);
        _csvExportPathLabel.AddThemeColorOverride("font_color", secondaryText);
        _trashCountLabel.AddThemeColorOverride("font_color", secondaryText);
        _outlineHintLabel?.AddThemeColorOverride("font_color", subtleText);
        _mindMapHintLabel?.AddThemeColorOverride("font_color", subtleText);
        TryNode<Label>("CsvHintLabel")?.AddThemeColorOverride("font_color", secondaryText);
        TryNode<Label>("MindMapHint")?.AddThemeColorOverride("font_color", secondaryText);
        ApplyLabelColorIfPresent("BrandTitle", primaryText);
        ApplyLabelColorIfPresent("BrandSubtitle", secondaryText);
        ApplyLabelColorIfPresent("VaultLabel", primaryText);
        ApplyLabelColorIfPresent("SidebarMiniInfo", secondaryText);
        ApplyLabelColorIfPresent("LibraryHeader", primaryText);
        ApplyLabelColorIfPresent("SidebarRuleLabel", secondaryText);
        ApplyLabelColorIfPresent("TrashLabel", primaryText);
        _localeLabel?.AddThemeColorOverride("font_color", secondaryText);
        _themeLabel?.AddThemeColorOverride("font_color", secondaryText);
        _localeSwitcher?.AddThemeStyleboxOverride("normal", _softButtonStyle);
        _localeSwitcher?.AddThemeStyleboxOverride("hover", _softButtonHoverStyle);
        _localeSwitcher?.AddThemeStyleboxOverride("pressed", _softButtonHoverStyle);
        _localeSwitcher?.AddThemeStyleboxOverride("focus", _softButtonStyle);
        _localeSwitcher?.AddThemeColorOverride("font_color", primaryText);
        _localeSwitcher?.AddThemeColorOverride("font_hover_color", primaryText);
        _localeSwitcher?.AddThemeColorOverride("font_pressed_color", primaryText);
        _localeSwitcher?.AddThemeColorOverride("font_focus_color", primaryText);
        _localeSwitcher?.AddThemeColorOverride("font_disabled_color", subtleText);
        _themeSwitcher?.AddThemeStyleboxOverride("normal", _softButtonStyle);
        _themeSwitcher?.AddThemeStyleboxOverride("hover", _softButtonHoverStyle);
        _themeSwitcher?.AddThemeStyleboxOverride("pressed", _softButtonHoverStyle);
        _themeSwitcher?.AddThemeStyleboxOverride("focus", _softButtonStyle);
        _themeSwitcher?.AddThemeColorOverride("font_color", primaryText);
        _themeSwitcher?.AddThemeColorOverride("font_hover_color", primaryText);
        _themeSwitcher?.AddThemeColorOverride("font_pressed_color", primaryText);
        _themeSwitcher?.AddThemeColorOverride("font_focus_color", primaryText);
        _themeSwitcher?.AddThemeColorOverride("font_disabled_color", subtleText);
        _contentSearchCheck.AddThemeColorOverride("font_color", primaryText);
        _contentSearchCheck.AddThemeColorOverride("font_hover_color", primaryText);
        _contentSearchCheck.AddThemeColorOverride("font_pressed_color", primaryText);
        _contentSearchCheck.AddThemeColorOverride("font_focus_color", primaryText);
        _contentSearchCheck.AddThemeColorOverride("font_disabled_color", subtleText);
        _libraryContextMenu.AddThemeStyleboxOverride("panel", _treePanelStyle);
        _libraryContextMenu.AddThemeColorOverride("font_color", primaryText);
        _libraryContextMenu.AddThemeColorOverride("font_hover_color", primaryText);
        _libraryContextMenu.AddThemeColorOverride("font_disabled_color", subtleText);
        _libraryContextMenu.AddThemeColorOverride("separator_color", border);
        RequireNode<PanelContainer>("OutlineViewport").AddThemeStyleboxOverride("panel", _toolbarSurfaceStyle);
        ConfigureCsvSheetChrome();
        ConfigureOverlayDialogs();

        if (TryNode<Button>("ClearSearchButton") is { } clearSearchButton)
        {
            ApplySoftButtonStyle(clearSearchButton);
        }
        ApplySoftButtonStyle(_contentSearchCheck);
        if (TryNode<Button>("NewFolderButton") is { } newFolderButton)
        {
            ApplySoftButtonStyle(newFolderButton);
        }
        if (TryNode<Button>("DeleteButton") is { } deleteButton)
        {
            ApplySoftButtonStyle(deleteButton, useDestructiveStyle: true);
        }
        if (TryNode<Button>("RestoreButton") is { } restoreButton)
        {
            ApplySoftButtonStyle(restoreButton);
        }
        if (TryNode<Button>("PurgeButton") is { } purgeButton)
        {
            ApplySoftButtonStyle(purgeButton, useDestructiveStyle: true);
        }
        if (TryNode<Button>("AddMindMapChildButton") is { } addChildButton)
        {
            ApplySoftButtonStyle(addChildButton);
        }
        if (TryNode<Button>("AddMindMapSiblingButton") is { } addSiblingButton)
        {
            ApplySoftButtonStyle(addSiblingButton);
        }
        if (TryNode<Button>("RenameMindMapNodeButton") is { } renameMindNodeButton)
        {
            ApplySoftButtonStyle(renameMindNodeButton);
        }
        if (TryNode<Button>("DeleteMindMapNodeButton") is { } deleteMindNodeButton)
        {
            ApplySoftButtonStyle(deleteMindNodeButton, useDestructiveStyle: true);
        }
        if (TryNode<Button>("GenerateMindMapButton") is { } generateMindMapButton)
        {
            ApplySoftButtonStyle(generateMindMapButton);
        }
        if (TryNode<Button>("ExportOutlineMdButton") is { } exportOutlineMdButton)
        {
            ApplySoftButtonStyle(exportOutlineMdButton);
        }
        if (TryNode<Button>("ExportMindMapButton") is { } exportMindMapButton)
        {
            ApplySoftButtonStyle(exportMindMapButton);
        }
        if (TryNode<Button>("ExportCsvButton") is { } exportCsvButton)
        {
            ApplySoftButtonStyle(exportCsvButton);
        }
        if (TryNode<Button>("NewDocumentButton") is { } newDocumentButton)
        {
            ApplyPrimaryButtonStyle(newDocumentButton);
        }

        foreach (var child in _libraryList.GetChildren())
        {
            if (child is LibraryItemRow row)
            {
                row.ApplyTheme(primaryText, secondaryText, secondaryText, subtleText, selected, selectedBorder, inputHover, selectedBorder);
            }
        }

        _mindMapCanvas.ApplyTheme(isDark, panel, border, borderSoft, primaryText, secondaryText, selectedBorder);
        RenderOutlineList(_focusedOutlineItemId);
        RenderCsvPreview(_structureParser.Parse(BuildMarkdownFromState()).ToArray());

        ApplyLocalizedText();
    }

    private void ConfigureLocaleSwitcher()
    {
        if (_localeSwitcher is null)
        {
            return;
        }

        _suppressEvents = true;
        _localeSwitcher.Clear();
        foreach (var locale in _localization.SupportedLocales)
        {
            _localeSwitcher.AddItem(_localization.Get($"locale.{locale}"));
        }

        var selectedIndex = 0;
        for (var i = 0; i < _localization.SupportedLocales.Count; i++)
        {
            if (_localization.SupportedLocales[i] == _localization.CurrentLocale)
            {
                selectedIndex = i;
                break;
            }
        }
        _localeSwitcher.Select(selectedIndex);
        _suppressEvents = false;
    }

    private void OnThemeSelected(long index)
    {
        if (_suppressEvents)
        {
            return;
        }

        var mode = (ThemeMode)Math.Clamp((int)index, 0, 2);
        ApplyThemeMode(mode);
        SaveThemeMode(mode);
        SetStatusKey("status.ready");
    }

    private void ApplyLocalizedText()
    {
        if (_localeLabel is not null)
        {
            _localeLabel.Text = T("locale.label");
        }
        if (_themeLabel is not null)
        {
            _themeLabel.Text = T("theme.label");
        }

        _mainTabs.SetTabTitle(0, T("tabs.outline"));
        _mainTabs.SetTabTitle(1, T("tabs.mindmap"));
        _mainTabs.SetTabTitle(2, T("tabs.csv"));
        SetLabelTextIfPresent("BrandSubtitle", T("app.subtitle"));
        SetLabelTextIfPresent("VaultLabel", T("sidebar.title"));
        SetLabelTextIfPresent("SidebarMiniInfo", T("sidebar.subtitle"));
        SetLabelTextIfPresent("LibraryHeader", T("sidebar.libraryHeader"));
        SetLabelTextIfPresent("TrashLabel", T("trash.title"));
        SetLabelTextIfPresent("SidebarRuleLabel", T("sidebar.rule"));
        SetLabelTextIfPresent("OutlineHintLabel", T("outline.hint"));
        SetLabelTextIfPresent("CsvHintLabel", T("csv.hint"));
        SetButtonTextIfPresent("ClearSearchButton", T("sidebar.clearSearch"));
        SetButtonTextIfPresent("NewFolderButton", T("sidebar.newFolder"));
        SetButtonTextIfPresent("NewDocumentButton", T("sidebar.newDocument"));
        SetButtonTextIfPresent("DeleteButton", T("sidebar.delete"));
        SetButtonTextIfPresent("RestoreButton", T("trash.restore"));
        SetButtonTextIfPresent("PurgeButton", T("trash.purge"));
        SetButtonTextIfPresent("AddMindMapChildButton", T("mindmap.addChild"));
        SetButtonTextIfPresent("AddMindMapSiblingButton", T("mindmap.addSibling"));
        SetButtonTextIfPresent("RenameMindMapNodeButton", T("mindmap.rename"));
        SetButtonTextIfPresent("DeleteMindMapNodeButton", T("mindmap.delete"));
        SetButtonTextIfPresent("GenerateMindMapButton", T("mindmap.regenerate"));
        SetButtonTextIfPresent("ExportOutlineMdButton", T("outline.exportMd"));
        SetButtonTextIfPresent("ExportMindMapButton", T("mindmap.export"));
        SetButtonTextIfPresent("ExportCsvButton", T("csv.export"));
        _searchBox.PlaceholderText = T("sidebar.searchPlaceholder");
        _contentSearchCheck.Text = T("sidebar.searchContent");
        _inputDialog.Title = T("dialog.inputTitle");
        _inputDialogLabel.Text = T("dialog.inputLabel");
        _deleteDialog.Title = T("dialog.deleteTitle");
        _infoDialog.Title = T("dialog.infoTitle");
        _exportFileDialog.Title = T("dialog.exportTitle");
        ConfigureContextMenus();
        UpdateDocumentChrome();
        RefreshTrashList();
        RefreshLibraryView(GetCurrentSelectionPath());
        UpdateMindMapActionButtons();
    }

    private void ApplySoftButtonStyle(Button button, bool useDestructiveStyle = false)
    {
        var baseStyle = useDestructiveStyle ? _destructiveButtonStyle : _softButtonStyle;
        var disabledStyle = useDestructiveStyle ? _destructiveButtonDisabledStyle : _softButtonDisabledStyle;
        var destructiveText = _themeService.ResolveEffectiveMode() == ThemeMode.Dark ? new Color("f3b1a5") : new Color("b35a48");
        button.AddThemeStyleboxOverride("normal", baseStyle);
        button.AddThemeStyleboxOverride("hover", _softButtonHoverStyle);
        button.AddThemeStyleboxOverride("pressed", _softButtonHoverStyle);
        button.AddThemeStyleboxOverride("focus", baseStyle);
        button.AddThemeStyleboxOverride("disabled", disabledStyle);
        button.AddThemeColorOverride("font_color", useDestructiveStyle ? destructiveText : _primaryTextColor);
        button.AddThemeColorOverride("font_hover_color", useDestructiveStyle ? destructiveText : _primaryTextColor);
        button.AddThemeColorOverride("font_pressed_color", useDestructiveStyle ? Colors.White : _primaryTextColor);
        button.AddThemeColorOverride("font_focus_color", useDestructiveStyle ? destructiveText : _primaryTextColor);
        button.AddThemeColorOverride("font_disabled_color", _subtleTextColor);
    }

    private void ApplyPrimaryButtonStyle(Button button)
    {
        var normalStyle = new StyleBoxFlat
        {
            BgColor = new Color("4d7bf3"),
            BorderColor = new Color("3f6de8"),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 14,
            CornerRadiusTopRight = 14,
            CornerRadiusBottomLeft = 14,
            CornerRadiusBottomRight = 14,
            ContentMarginLeft = 14,
            ContentMarginTop = 7,
            ContentMarginRight = 14,
            ContentMarginBottom = 7
        };

        var hoverStyle = new StyleBoxFlat
        {
            BgColor = new Color("5a86f6"),
            BorderColor = new Color("4874ea"),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 14,
            CornerRadiusTopRight = 14,
            CornerRadiusBottomLeft = 14,
            CornerRadiusBottomRight = 14,
            ContentMarginLeft = 14,
            ContentMarginTop = 7,
            ContentMarginRight = 14,
            ContentMarginBottom = 7
        };

        button.AddThemeStyleboxOverride("normal", normalStyle);
        button.AddThemeStyleboxOverride("hover", hoverStyle);
        button.AddThemeStyleboxOverride("pressed", hoverStyle);
        button.AddThemeStyleboxOverride("focus", normalStyle);
        button.AddThemeColorOverride("font_color", Colors.White);
        button.AddThemeColorOverride("font_hover_color", Colors.White);
        button.AddThemeColorOverride("font_pressed_color", Colors.White);
        button.AddThemeColorOverride("font_focus_color", Colors.White);
        button.AddThemeColorOverride("font_disabled_color", new Color("d9e2f7"));
    }

    private void ConfigureOverlayDialogs()
    {
        var dialogStyle = new StyleBoxFlat
        {
            BgColor = Colors.White,
            BorderColor = new Color("dbe5f0"),
            BorderWidthLeft = 1,
            BorderWidthTop = 1,
            BorderWidthRight = 1,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 20,
            CornerRadiusTopRight = 20,
            CornerRadiusBottomLeft = 20,
            CornerRadiusBottomRight = 20,
            ContentMarginLeft = 16,
            ContentMarginTop = 16,
            ContentMarginRight = 16,
            ContentMarginBottom = 16
        };

        ConfigureDialogWindow(_inputDialog, dialogStyle);
        ConfigureDialogWindow(_deleteDialog, dialogStyle);
        ConfigureDialogWindow(_infoDialog, dialogStyle);

        _inputDialogLabel.AddThemeColorOverride("font_color", _primaryTextColor);
        _deleteDialogLabel.AddThemeColorOverride("font_color", _primaryTextColor);
        _infoDialogLabel.AddThemeColorOverride("font_color", _primaryTextColor);
        _inputDialogLabel.AddThemeFontSizeOverride("font_size", 16);
        _deleteDialogLabel.AddThemeFontSizeOverride("font_size", 16);
        _infoDialogLabel.AddThemeFontSizeOverride("font_size", 16);

        ApplyPrimaryButtonStyle(_inputDialog.GetOkButton());
        ApplyPrimaryButtonStyle(_deleteDialog.GetOkButton());
        ApplyPrimaryButtonStyle(_infoDialog.GetOkButton());

        var inputCancelButton = _inputDialog.GetCancelButton();
        if (inputCancelButton is not null)
        {
            ApplySoftButtonStyle(inputCancelButton);
        }

        var deleteCancelButton = _deleteDialog.GetCancelButton();
        if (deleteCancelButton is not null)
        {
            ApplySoftButtonStyle(deleteCancelButton);
        }
    }

    private void ConfigureDialogWindow(Window dialog, StyleBoxFlat style)
    {
        dialog.Borderless = true;
        dialog.Unresizable = true;
        dialog.Exclusive = true;
        dialog.TransparentBg = true;
        dialog.AddThemeStyleboxOverride("panel", style);
    }

    private void ConfigureCsvSheetChrome()
    {
        EnsureCsvBackgroundSurface();
        _csvSheetScroll.OffsetTop = 52f;
        _csvSheetScroll.OffsetBottom = -40f;
        _csvSheetScroll.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _csvSheetScroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        _csvSheetScroll.AddThemeStyleboxOverride("panel", _csvBodyPanelStyle);
        if (TryNode<Panel>("CsvTab") is { } csvTabPanel)
        {
            csvTabPanel.AddThemeStyleboxOverride("panel", _csvPanelStyle);
        }
    }

    private void OnLocaleSelected(long index)
    {
        if (_suppressEvents)
        {
            return;
        }

        if (index < 0 || index >= _localization.SupportedLocales.Count)
        {
            return;
        }

        if (!_localization.SetCurrentLocale(_localization.SupportedLocales[(int)index]))
        {
            return;
        }

        ConfigureLocaleSwitcher();
        ApplyLocalizedText();
        SetStatusKey("status.ready");
    }

    private void RefreshAll()
    {
        RefreshLibraryView(_currentDocument?.RelativePath);
        RefreshTrashList();
        if (_currentDocument is null)
        {
            OpenFirstDocumentIfAvailable();
        }
    }

    private void RefreshLibraryTree(string? preferredSelection)
    {
        RefreshLibraryView(preferredSelection);
    }

    private void RefreshTrashList()
    {
        _trashEntries = _libraryService.GetTrashEntries().ToList();
        _trashList.Clear();
        _trashCountLabel.Text = _trashEntries.Count.ToString();
        foreach (var entry in _trashEntries)
        {
            var kind = entry.IsDirectory ? T("trash.kind.folder") : T("trash.kind.document");
            var deletedAt = entry.DeletedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
            _trashList.AddItem(Tf("trash.entry",
                ("kind", kind),
                ("path", entry.OriginalRelativePath),
                ("deletedAt", deletedAt)));
        }
    }

    private void RefreshLibraryView(string? preferredSelection)
    {
        var query = _searchBox.Text.Trim();
        var includeContent = _contentSearchCheck.ButtonPressed;
        var rootNode = _libraryService.BuildLibraryTree();
        ApplyCurrentDocumentOverride(rootNode);

        if (!string.IsNullOrWhiteSpace(preferredSelection))
        {
            UpdateLibrarySelection(preferredSelection!, FindNodeIsDirectory(rootNode, preferredSelection!));
        }

        foreach (var child in _libraryList.GetChildren())
        {
            child.QueueFree();
        }

        foreach (var child in rootNode.Children)
        {
            BuildLibraryRows(child, depth: 0, query, includeContent);
        }

        _libraryRootDropZone = EnsureLibraryRootDropZone();

        CallDeferred(nameof(SyncScrollableContentWidths));
    }

    private void BuildLibraryRows(LibraryNode node, int depth, string query, bool includeContent)
    {
        var matchingChildren = node.IsDirectory
            ? node.Children.Where(child => NodeMatches(child, query, includeContent) || DescendantMatches(child, query, includeContent)).ToList()
            : [];

        var isVisible = string.IsNullOrWhiteSpace(query)
            || NodeMatches(node, query, includeContent)
            || matchingChildren.Count > 0;

        if (!isVisible)
        {
            return;
        }

        var isExpanded = !node.IsDirectory
            || !string.IsNullOrWhiteSpace(query)
            || !_collapsedLibraryFolders.Contains(node.RelativePath);
        var isEditing = _pendingTreeEditPath == node.RelativePath;
        var isSelected = _selectedLibraryPath == node.RelativePath;

        var row = _libraryItemRowScene.Instantiate<LibraryItemRow>();
        _libraryList.AddChild(row);
        row.ApplyTheme(
            _primaryTextColor,
            _secondaryTextColor,
            _secondaryTextColor,
            _subtleTextColor,
            _treeSelectedStyle.BgColor,
            _treeSelectedStyle.BorderColor,
            _softButtonHoverStyle.BgColor,
            _treeSelectedStyle.BorderColor);
        row.ApplyState(
            node.RelativePath,
            node.IsDirectory,
            node.DisplayTitle,
            depth,
            isExpanded,
            isSelected,
            isEditing);
        row.Selected += OnLibraryRowSelected;
        row.Activated += OnLibraryRowActivated;
        row.SecondaryActionRequested += OnLibraryRowSecondaryActionRequested;
        row.ExpansionToggled += OnLibraryRowExpansionToggled;
        row.RenameSubmitted += OnLibraryRowRenameSubmitted;
        row.DragStarted += OnLibraryRowDragStarted;
        row.LibraryItemMoveRequested += OnLibraryRowLibraryItemMoveRequested;

        if (node.IsDirectory && isExpanded)
        {
            foreach (var child in node.Children)
            {
                BuildLibraryRows(child, depth + 1, query, includeContent);
            }
        }
    }

    private void OnLibraryRowSelected(string relativePath, bool isDirectory)
    {
        UpdateLibrarySelection(relativePath, isDirectory);
        SyncLibraryRowSelection();
    }

    private void OnLibraryRowDragStarted(string sourceRelativePath, bool sourceIsDirectory)
    {
        _isLibraryDragActive = true;
        _dragSourceRelativePath = sourceRelativePath;
        _dragSourceIsDirectory = sourceIsDirectory;
    }

    private void OnLibraryRowActivated(string relativePath, bool isDirectory)
    {
        UpdateLibrarySelection(relativePath, isDirectory);
        if (isDirectory)
        {
            ToggleLibraryFolderExpansion(relativePath);
            return;
        }

        OpenDocument(relativePath);
    }

    private void OnLibraryRowSecondaryActionRequested(string relativePath, bool isDirectory, Vector2 globalPosition)
    {
        UpdateLibrarySelection(relativePath, isDirectory);
        SyncLibraryRowSelection();
        _libraryContextMenu.SetItemDisabled(_libraryContextMenu.GetItemIndex((int)LibraryMenuAction.Rename), false);
        _libraryContextMenu.SetItemDisabled(_libraryContextMenu.GetItemIndex((int)LibraryMenuAction.Delete), false);
        _libraryContextMenu.SetItemDisabled(_libraryContextMenu.GetItemIndex((int)LibraryMenuAction.OpenLocation), false);
        _libraryContextMenu.Position = new Vector2I((int)globalPosition.X, (int)globalPosition.Y);
        _libraryContextMenu.Popup();
    }

    private void OnLibraryRowExpansionToggled(string relativePath)
    {
        ToggleLibraryFolderExpansion(relativePath);
    }

    private void OnLibraryRowRenameSubmitted(string relativePath, bool isDirectory, string newName)
    {
        if (string.IsNullOrWhiteSpace(_pendingTreeEditPath))
        {
            return;
        }

        try
        {
            if (isDirectory)
            {
                var renamedPath = _libraryService.RenameFolder(relativePath, newName);
                UpdateLibrarySelection(renamedPath, true);
                RefreshLibraryView(renamedPath);
                SetStatusKey("status.renamedFolder", ("path", renamedPath));
            }
            else
            {
                SaveCurrentDocumentIfNeeded();
                var renamedDocument = _libraryService.RenameDocument(relativePath, newName);
                _currentDocument = renamedDocument;
                _currentMindMapState = _libraryService.ReadMindMapState(renamedDocument.RelativePath);
                UpdateLibrarySelection(renamedDocument.RelativePath, false);
                RefreshLibraryView(renamedDocument.RelativePath);
                SetStatusKey("status.renamedDocument", ("path", renamedDocument.RelativePath));
            }
        }
        finally
        {
            _pendingTreeEditPath = string.Empty;
            _pendingTreeEditIsDirectory = false;
        }
    }

    private void OnLibraryRowLibraryItemMoveRequested(string sourceRelativePath, bool sourceIsDirectory, string targetFolderRelativePath)
    {
        if (string.IsNullOrWhiteSpace(sourceRelativePath))
        {
            return;
        }

        try
        {
            SaveCurrentDocumentIfNeeded();
            if (sourceIsDirectory)
            {
                var movedFolderPath = _libraryService.MoveFolderToFolder(sourceRelativePath, targetFolderRelativePath);
                if (_currentDocument is not null
                    && (_currentDocument.RelativePath.Equals(sourceRelativePath, StringComparison.OrdinalIgnoreCase)
                        || _currentDocument.RelativePath.StartsWith($"{sourceRelativePath}/", StringComparison.OrdinalIgnoreCase)))
                {
                    var rebasedRelativePath = movedFolderPath + _currentDocument.RelativePath[sourceRelativePath.Length..];
                    _currentDocument = _libraryService.ReadDocument(rebasedRelativePath);
                    _currentMindMapState = _libraryService.ReadMindMapState(rebasedRelativePath);
                }

                UpdateLibrarySelection(movedFolderPath, true);
                RefreshLibraryView(movedFolderPath);
                UpdateDocumentChrome();
                SetStatus($"已移动到：{movedFolderPath}");
                return;
            }

            var movedDocument = _libraryService.MoveDocumentToFolder(sourceRelativePath, targetFolderRelativePath);
            if (string.Equals(_currentDocument?.RelativePath, sourceRelativePath, StringComparison.OrdinalIgnoreCase))
            {
                _currentDocument = movedDocument;
                _currentMindMapState = _libraryService.ReadMindMapState(movedDocument.RelativePath);
            }

            UpdateLibrarySelection(movedDocument.RelativePath, false);
            RefreshLibraryView(movedDocument.RelativePath);
            UpdateDocumentChrome();
            SetStatus($"已移动到：{movedDocument.RelativePath}");
        }
        catch (Exception ex)
        {
            ShowInfo(ex.Message);
        }
    }

    private void OnLibraryListBackgroundGuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton button || !button.Pressed || button.ButtonIndex != MouseButton.Right)
        {
            return;
        }

        _libraryContextMenu.SetItemDisabled(_libraryContextMenu.GetItemIndex((int)LibraryMenuAction.Rename), true);
        _libraryContextMenu.SetItemDisabled(_libraryContextMenu.GetItemIndex((int)LibraryMenuAction.Delete), true);
        _libraryContextMenu.SetItemDisabled(_libraryContextMenu.GetItemIndex((int)LibraryMenuAction.OpenLocation), true);
        _libraryContextMenu.Position = DisplayServer.MouseGetPosition();
        _libraryContextMenu.Popup();
    }

    private void ToggleLibraryFolderExpansion(string relativePath)
    {
        if (_collapsedLibraryFolders.Contains(relativePath))
        {
            _collapsedLibraryFolders.Remove(relativePath);
        }
        else
        {
            _collapsedLibraryFolders.Add(relativePath);
        }

        RefreshLibraryView(relativePath);
    }

    private void UpdateLibrarySelection(string relativePath, bool isDirectory)
    {
        _selectedLibraryPath = relativePath;
        _selectedLibraryIsDirectory = isDirectory;
    }

    private void SyncLibraryRowSelection()
    {
        foreach (var child in _libraryList.GetChildren())
        {
            if (child is LibraryItemRow row)
            {
                row.SetSelectedState(string.Equals(row.RelativePath, _selectedLibraryPath, StringComparison.Ordinal));
            }
        }
    }

    private void TryCompleteLibraryDragToRoot()
    {
        if (!_isLibraryDragActive || string.IsNullOrWhiteSpace(_dragSourceRelativePath))
        {
            return;
        }

        try
        {
            if (IsPointerInLibraryBlankArea(GetGlobalMousePosition()))
            {
                OnLibraryRowLibraryItemMoveRequested(_dragSourceRelativePath, _dragSourceIsDirectory, string.Empty);
            }
        }
        finally
        {
            _isLibraryDragActive = false;
            _dragSourceRelativePath = string.Empty;
            _dragSourceIsDirectory = false;
        }
    }

    private bool IsPointerInLibraryBlankArea(Vector2 globalMousePosition)
    {
        var panelRect = new Rect2(_libraryListPanel.GlobalPosition, _libraryListPanel.Size);
        if (!panelRect.HasPoint(globalMousePosition))
        {
            return false;
        }

        foreach (var child in _libraryList.GetChildren())
        {
            if (child is not LibraryItemRow row || !row.Visible)
            {
                continue;
            }

            var rowRect = new Rect2(row.GlobalPosition, row.Size);
            if (rowRect.HasPoint(globalMousePosition))
            {
                return false;
            }
        }

        return true;
    }

    private static bool FindNodeIsDirectory(LibraryNode rootNode, string relativePath)
    {
        foreach (var child in rootNode.Children)
        {
            var found = FindNodeIsDirectoryOrNull(child, relativePath);
            if (found.HasValue)
            {
                return found.Value;
            }
        }

        return false;
    }

    private static bool? FindNodeIsDirectoryOrNull(LibraryNode node, string relativePath)
    {
        if (node.RelativePath == relativePath)
        {
            return node.IsDirectory;
        }

        foreach (var child in node.Children)
        {
            var found = FindNodeIsDirectoryOrNull(child, relativePath);
            if (found.HasValue)
            {
                return found.Value;
            }
        }

        return null;
    }

    private void OpenFirstDocumentIfAvailable()
    {
        var root = _libraryService.BuildLibraryTree();
        var firstDocument = FindFirstDocument(root);
        if (firstDocument is not null)
        {
            OpenDocument(firstDocument.RelativePath);
            return;
        }

        _currentDocument = null;
        _currentMindMapState = new MindMapDocumentState();
        _outlineItems.Clear();
        _outlineItems.Add(CreateEmptyItem(0));
        _outlineTitleText = string.Empty;
        _suppressEvents = true;
        _suppressEvents = false;
        _hasUnsavedDocumentChanges = false;
        _hasUnsavedMindMapChanges = false;
        RenderOutlineList();
        RefreshDerivedViews();
        UpdateDocumentChrome();
    }

    private void OpenDocument(string relativePath)
    {
        SaveCurrentDocumentIfNeeded();
        UpdateLibrarySelection(relativePath, false);
        _currentDocument = _libraryService.ReadDocument(relativePath);
        _currentMindMapState = _libraryService.ReadMindMapState(relativePath);

        var outlineModel = _outlineSerializer.Parse(_currentDocument.Content, _currentDocument.FileName);
        _outlineItems.Clear();
        _outlineItems.AddRange(outlineModel.Items.Select(item => new OutlineItem
        {
            Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id,
            Depth = item.Depth,
            Text = NormalizeOutlineText(item.Text)
        }));
        EnsureAtLeastOneItem();

        _outlineTitleText = outlineModel.Title;

        _hasUnsavedDocumentChanges = false;
        _hasUnsavedMindMapChanges = false;
        RenderOutlineList();
        RefreshDerivedViews();
        UpdateDocumentChrome();
        RefreshLibraryView(relativePath);
        SetStatusKey("status.opened", ("path", relativePath));
    }

    private void EnsureAtLeastOneItem()
    {
        if (_outlineItems.Count == 0)
        {
            _outlineItems.Add(CreateEmptyItem(0));
        }
    }

    private void RenderOutlineList(string? focusItemId = null)
    {
        RenderOutlineListUsingScenes(focusItemId);
        return;

#if false
        _outlineEditors.Clear();
        foreach (var child in _outlineList.GetChildren())
        {
            child.QueueFree();
        }

        EnsureAtLeastOneItem();
        var titleEditor = new LineEdit
        {
            Text = _outlineTitleText,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 48),
            PlaceholderText = "杈撳叆鏍囬",
            Flat = true
        };
        titleEditor.PlaceholderText = T("outline.titlePlaceholder");
        titleEditor.AddThemeStyleboxOverride("normal", _outlineItemStyle);
        titleEditor.AddThemeStyleboxOverride("focus", _outlineItemFocusStyle);
        titleEditor.AddThemeStyleboxOverride("read_only", _outlineItemStyle);
        titleEditor.AddThemeColorOverride("font_color", _primaryTextColor);
        titleEditor.AddThemeColorOverride("font_placeholder_color", _subtleTextColor);
        titleEditor.AddThemeColorOverride("font_selected_color", Colors.White);
        titleEditor.AddThemeColorOverride("selection_color", new Color("3d5afe"));
        titleEditor.AddThemeColorOverride("caret_color", _primaryTextColor);
        titleEditor.AddThemeFontSizeOverride("font_size", 30);
        titleEditor.TextChanged += OnOutlineTitleChanged;
        titleEditor.FocusEntered += () => _focusedOutlineItemId = OutlineTitleEditorId;
        titleEditor.GuiInput += @event => OnOutlineTitleGuiInput(titleEditor, @event);
        _outlineList.AddChild(titleEditor);
        _outlineEditors[OutlineTitleEditorId] = titleEditor;

        foreach (var item in _outlineItems)
        {
            var row = new HBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                Alignment = BoxContainer.AlignmentMode.Begin,
                CustomMinimumSize = new Vector2(0, 36)
            };
            row.AddThemeConstantOverride("separation", 8);

            var indent = BuildOutlineIndentGuides(item.Depth);
            row.AddChild(indent);

            var bullet = new Label
            {
                Text = "-",
                CustomMinimumSize = new Vector2(20, 0),
                VerticalAlignment = VerticalAlignment.Top
            };
            bullet.Text = "-";
            bullet.AddThemeColorOverride("font_color", item.Depth == 0 ? _primaryTextColor : _secondaryTextColor);
            bullet.AddThemeFontSizeOverride("font_size", item.Depth == 0 ? 18 : 16);
            row.AddChild(bullet);

            var editor = new LineEdit
            {
                Text = SanitizeOutlineTextForSingleLine(item.Text),
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                CustomMinimumSize = new Vector2(0, 34),
                PlaceholderText = "杈撳叆鏉＄洰",
                Flat = true
            };
            editor.PlaceholderText = T("outline.itemPlaceholder");
            editor.AddThemeStyleboxOverride("normal", _outlineItemStyle);
            editor.AddThemeStyleboxOverride("focus", _outlineItemFocusStyle);
            editor.AddThemeStyleboxOverride("read_only", _outlineItemStyle);
            editor.AddThemeColorOverride("font_color", _primaryTextColor);
            editor.AddThemeColorOverride("font_placeholder_color", _subtleTextColor);
            editor.AddThemeColorOverride("font_selected_color", Colors.White);
            editor.AddThemeColorOverride("selection_color", new Color("3d5afe"));
            editor.AddThemeColorOverride("caret_color", _primaryTextColor);
            editor.AddThemeFontSizeOverride("font_size", item.Depth == 0 ? 20 : 17);
            editor.TextChanged += text => OnOutlineItemTextChanged(item.Id, text);
            editor.FocusEntered += () => _focusedOutlineItemId = item.Id;
            editor.GuiInput += @event => OnOutlineItemGuiInput(item.Id, editor, @event);

            row.AddChild(editor);
            _outlineList.AddChild(row);
            _outlineEditors[item.Id] = editor;
        }

        var targetFocusId = focusItemId ?? _focusedOutlineItemId;
        FocusOutlineEditor(targetFocusId);
#endif
    }

    private void RenderOutlineListUsingScenes(string? focusItemId)
    {
        _outlineEditors.Clear();
        foreach (var child in _outlineList.GetChildren())
        {
            child.QueueFree();
        }

        EnsureAtLeastOneItem();

        var titleRow = _outlineTitleRowScene.Instantiate<OutlineTitleRow>();
        _outlineList.AddChild(titleRow);
        titleRow.ApplyState(_outlineTitleText, T("outline.titlePlaceholder"));
        var titleEditor = titleRow.Editor;
        titleEditor.AddThemeStyleboxOverride("normal", _outlineItemStyle);
        titleEditor.AddThemeStyleboxOverride("focus", _outlineItemFocusStyle);
        titleEditor.AddThemeStyleboxOverride("read_only", _outlineItemStyle);
        titleEditor.AddThemeColorOverride("font_color", _primaryTextColor);
        titleEditor.AddThemeColorOverride("font_placeholder_color", _subtleTextColor);
        titleEditor.AddThemeColorOverride("font_selected_color", Colors.White);
        titleEditor.AddThemeColorOverride("selection_color", new Color("3d5afe"));
        titleEditor.AddThemeColorOverride("caret_color", _primaryTextColor);
        titleEditor.AddThemeFontSizeOverride("font_size", 30);
        titleEditor.TextChanged += OnOutlineTitleChanged;
        titleEditor.FocusEntered += () => _focusedOutlineItemId = OutlineTitleEditorId;
        titleEditor.GuiInput += @event => OnOutlineTitleGuiInput(titleEditor, @event);
        _outlineEditors[OutlineTitleEditorId] = titleEditor;

        foreach (var item in _outlineItems)
        {
            var row = _outlineItemRowScene.Instantiate<OutlineItemRow>();
            _outlineList.AddChild(row);
            row.ApplyState(item.Depth, item.Text, T("outline.itemPlaceholder"));
            row.ApplyTheme(_primaryTextColor, _secondaryTextColor);
            var editor = row.Editor;
            editor.AddThemeStyleboxOverride("normal", _outlineItemStyle);
            editor.AddThemeStyleboxOverride("focus", _outlineItemFocusStyle);
            editor.AddThemeStyleboxOverride("read_only", _outlineItemStyle);
            editor.AddThemeColorOverride("font_color", _primaryTextColor);
            editor.AddThemeColorOverride("font_placeholder_color", _subtleTextColor);
            editor.AddThemeColorOverride("font_selected_color", Colors.White);
            editor.AddThemeColorOverride("selection_color", new Color("3d5afe"));
            editor.AddThemeColorOverride("caret_color", _primaryTextColor);
            editor.AddThemeFontSizeOverride("font_size", item.Depth == 0 ? 20 : 17);
            editor.TextChanged += text => OnOutlineItemTextChanged(item.Id, text);
            editor.FocusEntered += () => _focusedOutlineItemId = item.Id;
            editor.GuiInput += @event => OnOutlineItemGuiInput(item.Id, editor, @event);
            _outlineEditors[item.Id] = editor;
        }

        var targetFocusId = focusItemId ?? _focusedOutlineItemId;
        CallDeferred(nameof(SyncScrollableContentWidths));
        FocusOutlineEditor(targetFocusId);
    }

    private void OnOutlineItemTextChanged(string itemId, string text)
    {
        if (_suppressEvents)
        {
            return;
        }

        var item = _outlineItems.FirstOrDefault(entry => entry.Id == itemId);
        if (item is null)
        {
            return;
        }

        item.Text = NormalizeOutlineText(text);
        MarkDocumentDirty();
    }

    private void OnScrollContentViewportResized()
    {
        CallDeferred(nameof(SyncScrollableContentWidths));
    }

    private void SyncScrollableContentWidths()
    {
        if (IsInstanceValid(_outlineScroll) && IsInstanceValid(_outlineList))
        {
            var outlineWidth = Mathf.Max(0f, _outlineScroll.Size.X - 20f);
            _outlineList.CustomMinimumSize = new Vector2(outlineWidth, _outlineList.CustomMinimumSize.Y);
        }

        if (IsInstanceValid(_libraryListScroll) && IsInstanceValid(_libraryList))
        {
            var libraryWidth = Mathf.Max(0f, _libraryListScroll.Size.X - 12f);
            _libraryList.CustomMinimumSize = new Vector2(libraryWidth, _libraryList.CustomMinimumSize.Y);
        }

        if (IsInstanceValid(_libraryListPanel)
            && IsInstanceValid(_libraryListScroll)
            && IsInstanceValid(_libraryRootDropZone)
            && TryNode<Control>("SidebarPanel") is { } sidebarPanel)
        {
            var panelTop = _libraryListPanel.Position.Y;
            var scrollTop = panelTop + _libraryListScroll.Position.Y;
            var scrollBottom = scrollTop + _libraryListScroll.Size.Y;
            var contentBottom = scrollTop + Mathf.Max(_libraryList.Size.Y, _libraryList.CustomMinimumSize.Y);
            var emptyTop = Mathf.Clamp(contentBottom + 8f, scrollTop, scrollBottom);
            var emptyHeight = Mathf.Max(0f, scrollBottom - emptyTop);
            _libraryRootDropZone.OffsetTop = emptyTop;
            _libraryRootDropZone.OffsetBottom = sidebarPanel.Size.Y - scrollBottom;
            _libraryRootDropZone.Visible = emptyHeight > 12f;
        }
    }

    private void OnOutlineTitleChanged(string _newText)
    {
        if (_suppressEvents)
        {
            return;
        }

        _outlineTitleText = _newText;
        _currentDocument ??= new DocumentSnapshot();
        _currentDocument.DisplayTitle = BuildDisplayTitle();
        MarkDocumentDirty();
    }

    private void OnWindowFilesDropped(string[] files)
    {
        if (files is null || files.Length == 0)
        {
            return;
        }

        try
        {
            SaveCurrentDocumentIfNeeded();
            var importedDocuments = _libraryService.ImportExternalPaths(files, ResolveSelectedFolderPath());
            if (importedDocuments.Count == 0)
            {
                ShowInfo(T("info.noSupportedDropFiles"));
                return;
            }

            var focusPath = importedDocuments[0];
            var targetFolder = ResolveSelectedFolderPath();
            if (!string.IsNullOrWhiteSpace(targetFolder))
            {
                _collapsedLibraryFolders.Remove(targetFolder);
            }

            RefreshLibraryView(focusPath);
            OpenDocument(focusPath);
            SetStatusKey("status.importedExternalFiles", ("count", importedDocuments.Count.ToString()));
        }
        catch (Exception ex)
        {
            ShowInfo(ex.Message);
        }
    }

    private void OnOutlineTitleGuiInput(LineEdit editor, InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
        {
            return;
        }

        if (keyEvent.Keycode is not (Key.Enter or Key.KpEnter))
        {
            return;
        }

        EnsureAtLeastOneItem();
        var firstItem = _outlineItems[0];
        if (string.IsNullOrWhiteSpace(firstItem.Text))
        {
            _focusedOutlineItemId = firstItem.Id;
            RenderOutlineList(firstItem.Id);
        }
        else
        {
            var newItem = CreateEmptyItem(0);
            _outlineItems.Insert(0, newItem);
            MarkDocumentDirty(newItem.Id, rerender: true);
        }

        GetViewport().SetInputAsHandled();
    }

    private void OnOutlineItemGuiInput(string itemId, LineEdit editor, InputEvent @event)
    {
        if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
        {
            return;
        }

        if (keyEvent.Keycode is Key.Enter or Key.KpEnter)
        {
            InsertSiblingAfter(itemId);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (keyEvent.Keycode == Key.Tab && !keyEvent.ShiftPressed)
        {
            IndentItem(itemId);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (keyEvent.Keycode == Key.Tab && keyEvent.ShiftPressed)
        {
            OutdentItem(itemId);
            GetViewport().SetInputAsHandled();
            return;
        }

        if (keyEvent.Keycode == Key.Backspace && string.IsNullOrWhiteSpace(editor.Text))
        {
            TryRemoveEmptyItem(itemId);
            GetViewport().SetInputAsHandled();
        }
    }

    private void InsertSiblingAfter(string itemId)
    {
        var index = FindItemIndex(itemId);
        if (index < 0)
        {
            return;
        }

        var current = _outlineItems[index];
        var subtreeEnd = FindSubtreeEndIndex(index);
        var newItem = CreateEmptyItem(current.Depth);
        _outlineItems.Insert(subtreeEnd + 1, newItem);
        MarkDocumentDirty(newItem.Id, rerender: true);
    }

    private void IndentItem(string itemId)
    {
        var index = FindItemIndex(itemId);
        if (index <= 0)
        {
            return;
        }

        var previous = _outlineItems[index - 1];
        var current = _outlineItems[index];
        var newDepth = Math.Min(current.Depth + 1, previous.Depth + 1);
        var delta = newDepth - current.Depth;
        if (delta <= 0)
        {
            return;
        }

        ShiftSubtreeDepth(index, delta);
        _outlineItems[index].Depth = newDepth;
        MarkDocumentDirty(itemId, rerender: true);
    }

    private void OutdentItem(string itemId)
    {
        var index = FindItemIndex(itemId);
        if (index < 0)
        {
            return;
        }

        var current = _outlineItems[index];
        if (current.Depth <= 0)
        {
            return;
        }

        ShiftSubtreeDepth(index, -1);
        MarkDocumentDirty(itemId, rerender: true);
    }

    private void ShiftSubtreeDepth(int index, int delta)
    {
        var end = FindSubtreeEndIndex(index);
        for (var i = index; i <= end; i++)
        {
            _outlineItems[i].Depth = Math.Max(0, _outlineItems[i].Depth + delta);
        }
    }

    private void TryRemoveEmptyItem(string itemId)
    {
        if (_outlineItems.Count <= 1)
        {
            return;
        }

        var index = FindItemIndex(itemId);
        if (index < 0)
        {
            return;
        }

        var item = _outlineItems[index];
        if (!string.IsNullOrWhiteSpace(item.Text))
        {
            return;
        }

        var fallbackIndex = Math.Max(0, index - 1);
        var fallbackId = _outlineItems[fallbackIndex].Id;
        var end = FindSubtreeEndIndex(index);
        _outlineItems.RemoveRange(index, end - index + 1);
        EnsureAtLeastOneItem();
        MarkDocumentDirty(fallbackId, rerender: true);
    }

    private void RefreshDerivedViews()
    {
        var markdown = BuildMarkdownFromState();
        var markdownNodes = _structureParser.Parse(markdown).ToArray();
        RenderCsvPreview(markdownNodes);

        var mindMapNodes = BuildMindMapNodes().ToList();
        EnsureMindMapStateMatches(mindMapNodes);
        _mindMapCanvas.RenderNodes(mindMapNodes, _currentMindMapState.Zoom);
        if (_mindMapHintLabel is not null)
        {
            _mindMapHintLabel.Text = mindMapNodes.Count <= 1
                ? T("mindmap.hint.empty")
                : T("mindmap.hint.ready");
        }
        SetMindMapHint(mindMapNodes.Count > 1);
        UpdateMindMapActionButtons();
    }

    private void SaveCurrentDocumentIfNeeded()
    {
        if (_currentDocument is null)
        {
            return;
        }

        var savedAnything = false;
        if (_hasUnsavedDocumentChanges)
        {
            var markdown = BuildMarkdownFromState();
            _libraryService.SaveDocument(_currentDocument.RelativePath, markdown);
            _currentDocument = _libraryService.ReadDocument(_currentDocument.RelativePath);
            _hasUnsavedDocumentChanges = false;
            RefreshLibraryTree(_currentDocument.RelativePath);
            savedAnything = true;
        }

        if (_hasUnsavedMindMapChanges)
        {
            _libraryService.SaveMindMapState(_currentDocument.RelativePath, _currentMindMapState);
            _hasUnsavedMindMapChanges = false;
            savedAnything = true;
        }

        if (savedAnything)
        {
            UpdateDocumentChrome();
        }
    }

    private void UpdateDocumentChrome()
    {
        var displayTitle = BuildDisplayTitle();
        if (_currentDocument is null)
        {
            _saveStatusLabel.Text = T("save.idle");
            return;
        }

        _currentDocument.DisplayTitle = displayTitle;
        _saveStatusLabel.Text = _hasUnsavedDocumentChanges || _hasUnsavedMindMapChanges
            ? T("save.unsaved")
            : Tf("save.savedAt", ("time", DateTime.Now.ToString("HH:mm:ss")));
    }

    private string BuildDisplayTitle()
    {
        var normalized = MarkdownTitleHelper.NormalizeDisplayTitle(_outlineTitleText);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            return normalized;
        }

        foreach (var item in _outlineItems)
        {
            var firstMeaningfulLine = item.Text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n')
                .Select(line => MarkdownTitleHelper.NormalizeDisplayTitle(line))
                .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
            if (!string.IsNullOrWhiteSpace(firstMeaningfulLine))
            {
                return firstMeaningfulLine;
            }
        }

        return _currentDocument?.DisplayTitle ?? "Untitled";
    }

    private string BuildMarkdownFromState()
    {
        return _outlineSerializer.BuildMarkdown(_outlineTitleText, _outlineItems);
    }

    private void MarkDocumentDirty(string? focusItemId = null, bool rerender = false)
    {
        _hasUnsavedDocumentChanges = true;
        _saveTimer.Start();
        UpdateDocumentChrome();
        RefreshLibraryTree(_currentDocument?.RelativePath);
        RefreshDerivedViews();

        if (rerender)
        {
            RenderOutlineList(focusItemId);
        }
    }

    private void MarkMindMapDirty()
    {
        _hasUnsavedMindMapChanges = true;
        _saveTimer.Start();
        UpdateDocumentChrome();
    }

    private void ConfigureContextMenus()
    {
        _libraryContextMenu.Clear();
        _libraryContextMenu.AddItem(T("menu.newDocument"), (int)LibraryMenuAction.NewDocument);
        _libraryContextMenu.AddItem(T("menu.newFolder"), (int)LibraryMenuAction.NewFolder);
        _libraryContextMenu.AddSeparator();
        _libraryContextMenu.AddItem(T("menu.openLocation"), (int)LibraryMenuAction.OpenLocation);
        _libraryContextMenu.AddItem(T("menu.rename"), (int)LibraryMenuAction.Rename);
        _libraryContextMenu.AddItem(T("menu.delete"), (int)LibraryMenuAction.Delete);
    }

    private void UpdateMindMapActionButtons()
    {
        var selectedNodeId = _mindMapCanvas?.GetSelectedNodeId() ?? string.Empty;
        var hasRegularSelection = !string.IsNullOrWhiteSpace(selectedNodeId) && selectedNodeId != MindMapRootNodeId;
        var hasExportableMindMap = HasExportableMindMap();
        if (TryNode<Button>("ExportMindMapButton") is { } exportMindMapButton)
        {
            exportMindMapButton.Disabled = !hasExportableMindMap;
            exportMindMapButton.TooltipText = hasExportableMindMap ? T("mindmap.export") : T("mindmap.hint.empty");
        }
        _addMindMapChildButton.Disabled = _currentDocument is null;
        _addMindMapSiblingButton.Disabled = _currentDocument is null;
        _renameMindMapNodeButton.Disabled = !hasRegularSelection;
        _deleteMindMapNodeButton.Disabled = !hasRegularSelection;
    }

    private bool HasExportableMindMap()
    {
        return _currentDocument is not null && BuildMindMapNodes().Any(node => !node.IsRoot);
    }

    private void ConstrainViewportLayout()
    {
        return;
    }

    private void CreateFolderInline()
    {
        var relativePath = _libraryService.CreateFolder(ResolveSelectedFolderPath(), "New Folder");
        var selectedFolder = ResolveSelectedFolderPath();
        if (!string.IsNullOrWhiteSpace(selectedFolder))
        {
            _collapsedLibraryFolders.Remove(selectedFolder);
        }

        RefreshLibraryView(relativePath);
        BeginTreeRename(relativePath, true);
        SetStatusKey("status.createdFolder", ("path", relativePath));
    }

    private void CreateDocumentInline()
    {
        var document = _libraryService.CreateDocument(ResolveSelectedFolderPath(), "Untitled");
        var selectedFolder = ResolveSelectedFolderPath();
        if (!string.IsNullOrWhiteSpace(selectedFolder))
        {
            _collapsedLibraryFolders.Remove(selectedFolder);
        }

        RefreshLibraryView(document.RelativePath);
        OpenDocument(document.RelativePath);
        BeginTreeRename(document.RelativePath, false);
        SetStatusKey("status.createdDocument", ("path", document.RelativePath));
    }

    private void BeginTreeRename(string relativePath, bool isDirectory)
    {
        _pendingTreeEditPath = relativePath;
        _pendingTreeEditIsDirectory = isDirectory;
        UpdateLibrarySelection(relativePath, isDirectory);
        RefreshLibraryView(relativePath);
    }

    private void OnNewFolderPressed()
    {
        CreateFolderInline();
    }

    private void OnNewDocumentPressed()
    {
        CreateDocumentInline();
    }

    private void OnDeletePressed()
    {
        if (!string.IsNullOrWhiteSpace(_selectedLibraryPath))
        {
            _pendingDeletePath = _selectedLibraryPath;
            _pendingDeleteIsDirectory = _selectedLibraryIsDirectory;
            _deleteDialogLabel.Text = _selectedLibraryIsDirectory
                ? Tf("dialog.deleteFolder", ("path", _selectedLibraryPath))
                : Tf("dialog.deleteDocument", ("path", _selectedLibraryPath));
            _deleteDialog.PopupCentered(new Vector2I(480, 180));
            return;
        }

        var selection = ParseSelection(_libraryTree.GetSelected());
        if (selection is null)
        {
            ShowInfo(T("info.selectDocOrFolder"));
            return;
        }

        _pendingDeletePath = selection.Value.RelativePath;
        _pendingDeleteIsDirectory = selection.Value.IsDirectory;
        _deleteDialogLabel.Text = selection.Value.IsDirectory
            ? Tf("dialog.deleteFolder", ("path", selection.Value.RelativePath))
            : Tf("dialog.deleteDocument", ("path", selection.Value.RelativePath));
        _deleteDialog.PopupCentered(new Vector2I(480, 180));
    }

    private void OnClearSearchPressed()
    {
        _searchBox.Text = string.Empty;
        _contentSearchCheck.ButtonPressed = false;
        RefreshLibraryTree(GetCurrentSelectionPath());
    }

    private void OnTrashButtonPressed()
    {
        RefreshTrashList();
        if (_trashEntries.Count > 0)
        {
            _trashList.Select(0);
        }

        SetStatusKey("status.trashFocused");
    }

    private void OnGenerateMindMapPressed()
    {
        ResetMindMapLayoutToDefault();
        RefreshDerivedViews();
        _mainTabs.CurrentTab = 1;
        SetStatusKey("status.mindmapRegenerated");
    }

    private void OnExportOutlineMdPressed()
    {
        if (_currentDocument is null)
        {
            ShowInfo(T("info.openDocumentFirst"));
            return;
        }

        ShowExportDialog(ExportRequestKind.OutlineMarkdown);
    }

    private void OnExportCsvPressed()
    {
        if (_currentDocument is null)
        {
            ShowInfo(T("info.openDocumentFirst"));
            return;
        }

        ShowExportDialog(ExportRequestKind.Csv);
    }

    private void OnExportMindMapPressed()
    {
        if (_currentDocument is null)
        {
            ShowInfo(T("info.openDocumentFirst"));
            return;
        }

        ShowExportDialog(ExportRequestKind.MindMap);
    }

    private void ShowExportDialog(ExportRequestKind requestKind)
    {
        if (_currentDocument is null)
        {
            ShowInfo(T("info.openDocumentFirst"));
            return;
        }

        _pendingExportRequest = requestKind;
        _exportFileDialog.ClearFilters();

        switch (requestKind)
        {
            case ExportRequestKind.OutlineMarkdown:
                _exportFileDialog.AddFilter("*.md ; Markdown");
                _exportFileDialog.CurrentFile = BuildDefaultExportName(".md");
                break;
            case ExportRequestKind.Csv:
                _exportFileDialog.AddFilter("*.csv ; CSV");
                _exportFileDialog.CurrentFile = BuildDefaultExportName(".csv");
                break;
            case ExportRequestKind.MindMap:
                _exportFileDialog.AddFilter("*.png ; PNG");
                _exportFileDialog.AddFilter("*.jpg ; JPG");
                _exportFileDialog.AddFilter("*.svg ; SVG");
                _exportFileDialog.CurrentFile = BuildDefaultExportName(".png");
                break;
            default:
                return;
        }

        _exportFileDialog.CurrentDir = _libraryService.ExportRootPath;
        _exportFileDialog.PopupCenteredRatio(0.8f);
    }

    private async void OnExportFileSelected(string path)
    {
        if (_currentDocument is null || _pendingExportRequest == ExportRequestKind.None)
        {
            _pendingExportRequest = ExportRequestKind.None;
            return;
        }

        try
        {
            SaveCurrentDocumentIfNeeded();

            switch (_pendingExportRequest)
            {
                case ExportRequestKind.OutlineMarkdown:
                    ExportOutlineMarkdownTo(path);
                    break;
                case ExportRequestKind.Csv:
                    ExportCsvTo(path);
                    break;
                case ExportRequestKind.MindMap:
                    await ExportMindMapTo(path);
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowInfo($"{T("info.exportFailed")}\n{ex.Message}");
        }
        finally
        {
            _pendingExportRequest = ExportRequestKind.None;
        }
    }

    private void ExportOutlineMarkdownTo(string requestedPath)
    {
        var exportPath = EnsureExportPath(requestedPath, ".md");
        var markdown = BuildMarkdownFromState();
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(exportPath)!);
        System.IO.File.WriteAllText(exportPath, markdown);
        SetStatus(Tf("status.markdownExported", ("path", exportPath)));
    }

    private void ExportCsvTo(string requestedPath)
    {
        var exportPath = EnsureExportPath(requestedPath, ".csv");
        var csv = _structureParser.BuildCsv(BuildMarkdownFromState());
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(exportPath)!);
        System.IO.File.WriteAllText(exportPath, csv);
        _csvExportPathLabel.Text = Tf("csv.path", ("path", exportPath));
        _mainTabs.CurrentTab = 2;
        SetStatus(Tf("status.csvExported", ("path", exportPath)));
    }

    private async Task ExportMindMapTo(string requestedPath)
    {
        var extension = ResolveMindMapExportExtension(requestedPath);
        var exportPath = EnsureExportPath(requestedPath, extension);
        if (extension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
        {
            var svg = BuildMindMapSvg();
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(exportPath)!);
            System.IO.File.WriteAllText(exportPath, svg);
            SetStatus(Tf("status.mindmapSvgExported", ("path", exportPath)));
            return;
        }

        var error = await _mindMapCanvas.ExportRasterSnapshotAsync(exportPath, extension);
        if (error != Error.Ok)
        {
            ShowInfo($"{T("info.exportFailed")} ({error})");
            return;
        }

        SetStatus(Tf(
            extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ? "status.mindmapJpgExported" : "status.mindmapPngExported",
            ("path", exportPath)));
    }

    private string BuildDefaultExportName(string extension)
    {
        var slug = MarkdownTitleHelper.Slugify(BuildDisplayTitle());
        var safeSlug = string.IsNullOrWhiteSpace(slug) ? "untitled" : slug;
        return $"{safeSlug}{extension}";
    }

    private string EnsureExportPath(string requestedPath, string defaultExtension)
    {
        var trimmedPath = requestedPath.Trim();
        var extension = Path.GetExtension(trimmedPath);
        if (string.IsNullOrWhiteSpace(extension))
        {
            trimmedPath += defaultExtension;
        }

        return trimmedPath;
    }

    private string ResolveMindMapExportExtension(string requestedPath)
    {
        var extension = Path.GetExtension(requestedPath);
        if (!string.IsNullOrWhiteSpace(extension))
        {
            return extension.ToLowerInvariant();
        }

        return ".png";
    }

    private void OnRestoreTrashPressed()
    {
        if (_trashList.GetSelectedItems().Length == 0)
        {
            ShowInfo(T("info.selectTrashEntry"));
            return;
        }

        var index = _trashList.GetSelectedItems()[0];
        var entry = _trashEntries[index];
        _libraryService.RestoreTrashEntry(entry.TrashId);
        RefreshTrashList();
        RefreshLibraryTree(_currentDocument?.RelativePath);
        SetStatusKey("status.trashRestored", ("path", entry.OriginalRelativePath));
        SetStatusKey("status.trashRestored", ("path", entry.OriginalRelativePath));
    }

    private void OnPurgeTrashPressed()
    {
        if (_trashList.GetSelectedItems().Length == 0)
        {
            ShowInfo(T("info.selectTrashEntry"));
            return;
        }

        var index = _trashList.GetSelectedItems()[0];
        var entry = _trashEntries[index];
        _libraryService.PurgeTrashEntry(entry.TrashId);
        RefreshTrashList();
        SetStatusKey("status.trashPurged", ("path", entry.OriginalRelativePath));
        SetStatusKey("status.trashPurged", ("path", entry.OriginalRelativePath));
        SetStatusKey("status.trashPurged", ("path", entry.OriginalRelativePath));
    }

    private void OnLibraryTreeItemSelected()
    {
        var selection = ParseSelection(_libraryTree.GetSelected());
        if (selection is not null)
        {
            if (!selection.Value.IsDirectory
                && !string.Equals(_currentDocument?.RelativePath, selection.Value.RelativePath, StringComparison.Ordinal))
            {
                OpenDocument(selection.Value.RelativePath);
            }
        }
    }

    private void OnLibraryTreeItemMouseSelected(Vector2 _position, long buttonIndex)
    {
        if (buttonIndex != (long)MouseButton.Right)
        {
            return;
        }

        var selection = ParseSelection(_libraryTree.GetSelected());
        _libraryContextMenu.SetItemDisabled(_libraryContextMenu.GetItemIndex((int)LibraryMenuAction.Rename), selection is null);
        _libraryContextMenu.SetItemDisabled(_libraryContextMenu.GetItemIndex((int)LibraryMenuAction.Delete), selection is null);
        _libraryContextMenu.SetItemDisabled(_libraryContextMenu.GetItemIndex((int)LibraryMenuAction.OpenLocation), selection is null);
        _libraryContextMenu.Position = DisplayServer.MouseGetPosition();
        _libraryContextMenu.Popup();
    }

    private void OnLibraryTreeItemEdited()
    {
        var item = _libraryTree.GetEdited();
        if (item is null)
        {
            return;
        }

        var text = item.GetText(0).Trim();
        item.SetEditable(0, false);

        if (string.IsNullOrWhiteSpace(_pendingTreeEditPath))
        {
            return;
        }

        try
        {
            if (_pendingTreeEditIsDirectory)
            {
                var renamedPath = _libraryService.RenameFolder(_pendingTreeEditPath, text);
                RefreshLibraryTree(renamedPath);
                SetStatusKey("status.renamedFolder", ("path", renamedPath));
            }
            else
            {
                SaveCurrentDocumentIfNeeded();
                var renamedDocument = _libraryService.RenameDocument(_pendingTreeEditPath, text);
                _currentDocument = renamedDocument;
                _currentMindMapState = _libraryService.ReadMindMapState(renamedDocument.RelativePath);
                RefreshLibraryTree(renamedDocument.RelativePath);
                SetStatusKey("status.renamedDocument", ("path", renamedDocument.RelativePath));
            }
        }
        finally
        {
            _pendingTreeEditPath = string.Empty;
            _pendingTreeEditIsDirectory = false;
        }
    }

    private void OnLibraryTreeItemActivated()
    {
        var selection = ParseSelection(_libraryTree.GetSelected());
        if (selection is null || selection.Value.IsDirectory)
        {
            return;
        }

        OpenDocument(selection.Value.RelativePath);
    }

    private void OnSearchChanged(string _newText)
    {
        RefreshLibraryTree(GetCurrentSelectionPath());
    }

    private void OnSaveTimerTimeout()
    {
        SaveCurrentDocumentIfNeeded();
    }

    private void OnInputDialogConfirmed()
    {
        var input = _inputDialogLineEdit.Text.Trim();
        if (_promptMode == PromptMode.CreateFolder)
        {
            var relativePath = _libraryService.CreateFolder(ResolveSelectedFolderPath(), input);
            RefreshLibraryTree(relativePath);
            SetStatusKey("status.createdFolder", ("path", relativePath));
        }
        else if (_promptMode == PromptMode.CreateDocument)
        {
            var document = _libraryService.CreateDocument(ResolveSelectedFolderPath(), input);
            RefreshLibraryTree(document.RelativePath);
            OpenDocument(document.RelativePath);
            SetStatusKey("status.createdDocument", ("path", document.RelativePath));
        }
        else if (_promptMode == PromptMode.RenameFolder)
        {
            var renamedPath = _libraryService.RenameFolder(_pendingRenamePath, input);
            if (_currentDocument?.RelativePath.StartsWith($"{_pendingRenamePath}/", StringComparison.Ordinal) == true)
            {
                var suffix = _currentDocument.RelativePath[_pendingRenamePath.Length..].TrimStart('/');
                var nextPath = string.IsNullOrWhiteSpace(suffix) ? renamedPath : $"{renamedPath}/{suffix}";
                _currentDocument = _libraryService.ReadDocument(nextPath);
            }

            RefreshLibraryTree(renamedPath);
            SetStatusKey("status.renamedFolder", ("path", renamedPath));
        }
        else if (_promptMode == PromptMode.RenameDocument)
        {
            SaveCurrentDocumentIfNeeded();
            var renamedDocument = _libraryService.RenameDocument(_pendingRenamePath, input);
            _currentDocument = renamedDocument;
            _currentMindMapState = _libraryService.ReadMindMapState(renamedDocument.RelativePath);
            RefreshLibraryTree(renamedDocument.RelativePath);
            SetStatusKey("status.renamedDocument", ("path", renamedDocument.RelativePath));
        }
        else if (_promptMode == PromptMode.RenameMindMapNode)
        {
            ApplyMindMapNodeRename(input);
        }

        _pendingRenamePath = string.Empty;
        _pendingRenameIsDirectory = false;
        _promptMode = PromptMode.None;
    }

    private void OnDeleteDialogConfirmed()
    {
        if (string.IsNullOrWhiteSpace(_pendingDeletePath))
        {
            return;
        }

        SaveCurrentDocumentIfNeeded();
        var deletedPath = _pendingDeletePath;
        _libraryService.MoveToTrash(_pendingDeletePath);

        if (_currentDocument?.RelativePath == deletedPath
            || (_pendingDeleteIsDirectory && _currentDocument?.RelativePath.StartsWith($"{deletedPath}/", StringComparison.Ordinal) == true))
        {
            _currentDocument = null;
            _currentMindMapState = new MindMapDocumentState();
            _outlineItems.Clear();
            _outlineItems.Add(CreateEmptyItem(0));
            _outlineTitleText = string.Empty;
            _hasUnsavedDocumentChanges = false;
            _hasUnsavedMindMapChanges = false;
            RenderOutlineList();
        }

        _pendingDeletePath = string.Empty;
        _pendingDeleteIsDirectory = false;
        RefreshTrashList();
        RefreshLibraryTree(string.Empty);
        OpenFirstDocumentIfAvailable();
        SetStatusKey("status.movedToTrash", ("path", deletedPath));
        SetStatusKey("status.movedToTrash", ("path", deletedPath));
    }

    private void OnMainTabChanged(long tab)
    {
        if (tab == 1 || tab == 2)
        {
            RefreshDerivedViews();
        }

        if (tab == 1)
        {
            UpdateMindMapActionButtons();
        }
    }

    private void OnMindMapNodeMoved(string nodeId, Vector2 position)
    {
        if (_currentDocument is null)
        {
            return;
        }

        var layout = GetOrCreateNodeLayout(nodeId);
        layout.X = position.X;
        layout.Y = position.Y;
        MarkMindMapDirty();
        UpdateMindMapActionButtons();
    }

    private void OnMindMapNodeReparented(string nodeId, string newParentId)
    {
        if (_currentDocument is null || nodeId == MindMapRootNodeId)
        {
            return;
        }

        if (ReparentOutlineItem(nodeId, newParentId))
        {
            SaveEdgeLayout(nodeId, MindMapConnectorSide.Right, MindMapConnectorSide.Left);
            MarkDocumentDirty(nodeId, rerender: true);
            SetStatusKey("status.mindmapLevelsUpdated");
        }

        UpdateMindMapActionButtons();
        SetStatusKey("status.mindmapLevelsUpdated");
    }

    private void OnMindMapZoomChanged(float zoom)
    {
        if (_currentDocument is null)
        {
            return;
        }

        _currentMindMapState.Zoom = zoom;
        MarkMindMapDirty();
        UpdateMindMapActionButtons();
    }

    private void OnMindMapConnectionChanged(
        string childNodeId,
        string parentNodeId,
        int parentSide,
        int childSide)
    {
        if (_currentDocument is null || childNodeId == MindMapRootNodeId)
        {
            return;
        }

        var parsedParentSide = (MindMapConnectorSide)parentSide;
        var parsedChildSide = (MindMapConnectorSide)childSide;

        if (ReparentOutlineItem(childNodeId, parentNodeId))
        {
            SaveEdgeLayout(childNodeId, parsedParentSide, parsedChildSide);
            MarkDocumentDirty(childNodeId, rerender: true);
            SetStatusKey("status.mindmapConnectionUpdated");
            return;
        }

        SaveEdgeLayout(childNodeId, parsedParentSide, parsedChildSide);
        RefreshDerivedViews();
        MarkMindMapDirty();
        SetStatusKey("status.mindmapAnchorUpdated");
        UpdateMindMapActionButtons();
    }

    private void OnLibraryContextMenuIdPressed(long id)
    {
        switch ((LibraryMenuAction)id)
        {
            case LibraryMenuAction.NewDocument:
                CreateDocumentInline();
                break;
            case LibraryMenuAction.NewFolder:
                CreateFolderInline();
                break;
            case LibraryMenuAction.OpenLocation:
                OpenSelectedLibraryLocation();
                break;
            case LibraryMenuAction.Rename:
                BeginRenameSelectedTreeItem();
                break;
            case LibraryMenuAction.Delete:
                OnDeletePressed();
                break;
        }
    }

    private void OpenSelectedLibraryLocation()
    {
        if (string.IsNullOrWhiteSpace(_selectedLibraryPath) && !_selectedLibraryIsDirectory)
        {
            ShowInfo(T("info.selectDocOrFolder"));
            return;
        }

        var absolutePath = _libraryService.ResolveLibraryAbsolutePath(_selectedLibraryPath, _selectedLibraryIsDirectory);
        var shellPath = absolutePath.Replace('/', '\\');
        if (string.IsNullOrWhiteSpace(absolutePath)
            || (!System.IO.File.Exists(absolutePath) && !System.IO.Directory.Exists(absolutePath)))
        {
            ShowInfo(T("info.nodeNotFound"));
            return;
        }

        try
        {
            if (_selectedLibraryIsDirectory)
            {
                WindowsShellService.OpenDirectory(shellPath);
            }
            else
            {
                WindowsShellService.RevealInExplorer(shellPath);
            }

            SetStatusKey("status.openLocation", ("path", shellPath));
        }
        catch (Exception ex)
        {
            ShowInfo(ex.Message);
        }
    }

    private void OnAddMindMapChildPressed()
    {
        if (_currentDocument is null)
        {
            ShowInfo(T("info.openDocumentFirst"));
            return;
        }

        var selectedNodeId = _mindMapCanvas.GetSelectedNodeId();
        var parentId = string.IsNullOrWhiteSpace(selectedNodeId) || selectedNodeId == MindMapRootNodeId
            ? null
            : selectedNodeId;

        var focusId = AddOutlineItemFromMindMap(parentId, asSibling: false);
        if (!string.IsNullOrWhiteSpace(focusId))
        {
            _mainTabs.CurrentTab = 0;
        }
        SetStatusKey("status.addedChildNode");
    }

    private void OnAddMindMapSiblingPressed()
    {
        if (_currentDocument is null)
        {
            ShowInfo(T("info.openDocumentFirst"));
            return;
        }

        var selectedNodeId = _mindMapCanvas.GetSelectedNodeId();
        var focusId = AddOutlineItemFromMindMap(selectedNodeId, asSibling: true);
        if (!string.IsNullOrWhiteSpace(focusId))
        {
            _mainTabs.CurrentTab = 0;
            SetStatusKey("status.addedSiblingNode");
        }
        SetStatusKey("status.addedSiblingNode");
    }

    private void OnRenameMindMapNodePressed()
    {
        if (_currentDocument is null)
        {
            ShowInfo(T("info.openDocumentFirst"));
            return;
        }

        var selectedNodeId = _mindMapCanvas.GetSelectedNodeId();
        if (string.IsNullOrWhiteSpace(selectedNodeId) || selectedNodeId == MindMapRootNodeId)
        {
            ShowInfo(T("info.selectMindNode"));
            return;
        }

        var item = GetOutlineItem(selectedNodeId);
        if (item is null)
        {
            ShowInfo(T("info.nodeNotFound"));
            return;
        }

        _promptMode = PromptMode.RenameMindMapNode;
        _focusedOutlineItemId = selectedNodeId;
        _inputDialog.Title = T("dialog.renameMindNodeTitle");
        _inputDialogLabel.Text = T("dialog.renameMindNodeBody");
        _inputDialogLineEdit.Text = item.Text;
        _inputDialog.PopupCentered(new Vector2I(440, 180));
        _inputDialogLineEdit.GrabFocus();
        _inputDialogLineEdit.SelectAll();
    }

    private void OnDeleteMindMapNodePressed()
    {
        if (_currentDocument is null)
        {
            ShowInfo(T("info.openDocumentFirst"));
            return;
        }

        var selectedNodeId = _mindMapCanvas.GetSelectedNodeId();
        if (string.IsNullOrWhiteSpace(selectedNodeId) || selectedNodeId == MindMapRootNodeId)
        {
            ShowInfo(T("info.selectMindNode"));
            return;
        }

        RemoveOutlineSubtree(selectedNodeId);
    }

    private string ResolveSelectedFolderPath()
    {
        if (!string.IsNullOrWhiteSpace(_selectedLibraryPath))
        {
            if (_selectedLibraryIsDirectory)
            {
                return _selectedLibraryPath;
            }

            var selectedNormalized = _selectedLibraryPath.Replace('\\', '/');
            var selectedLastSlash = selectedNormalized.LastIndexOf('/');
            return selectedLastSlash >= 0 ? selectedNormalized[..selectedLastSlash] : string.Empty;
        }

        var selection = ParseSelection(_libraryTree.GetSelected());
        if (selection is null)
        {
            return string.Empty;
        }

        if (selection.Value.IsDirectory)
        {
            return selection.Value.RelativePath;
        }

        var normalized = selection.Value.RelativePath.Replace('\\', '/');
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash >= 0 ? normalized[..lastSlash] : string.Empty;
    }

    private string? GetCurrentSelectionPath()
    {
        if (!string.IsNullOrWhiteSpace(_selectedLibraryPath))
        {
            return _selectedLibraryPath;
        }

        var selection = ParseSelection(_libraryTree.GetSelected());
        return selection?.RelativePath;
    }

    private void BeginRenameSelectedTreeItem()
    {
        if (!string.IsNullOrWhiteSpace(_selectedLibraryPath))
        {
            BeginTreeRename(_selectedLibraryPath, _selectedLibraryIsDirectory);
            return;
        }

        var selection = ParseSelection(_libraryTree.GetSelected());
        if (selection is null)
        {
            ShowInfo(T("info.selectDocOrFolder"));
            return;
        }

        _pendingRenamePath = selection.Value.RelativePath;
        _pendingRenameIsDirectory = selection.Value.IsDirectory;
        _promptMode = selection.Value.IsDirectory ? PromptMode.RenameFolder : PromptMode.RenameDocument;
        _inputDialog.Title = selection.Value.IsDirectory ? T("dialog.renameFolderTitle") : T("dialog.renameDocumentTitle");
        _inputDialogLabel.Text = selection.Value.IsDirectory
            ? T("dialog.renameFolderBody")
            : T("dialog.renameDocumentBody");
        _inputDialogLineEdit.Text = selection.Value.IsDirectory
            ? selection.Value.RelativePath.Split('/').LastOrDefault() ?? string.Empty
            : System.IO.Path.GetFileNameWithoutExtension(selection.Value.RelativePath);
        _inputDialog.PopupCentered(new Vector2I(440, 180));
        _inputDialogLineEdit.GrabFocus();
        _inputDialogLineEdit.SelectAll();
    }

    private string AddOutlineItemFromMindMap(string? anchorNodeId, bool asSibling)
    {
        EnsureAtLeastOneItem();

        if (string.IsNullOrWhiteSpace(anchorNodeId) || anchorNodeId == MindMapRootNodeId)
        {
            var item = CreateEmptyItem(0);
            _outlineItems.Add(item);
            MarkDocumentDirty(item.Id, rerender: true);
            return item.Id;
        }

        var index = FindItemIndex(anchorNodeId);
        if (index < 0)
        {
            var item = CreateEmptyItem(0);
            _outlineItems.Add(item);
            MarkDocumentDirty(item.Id, rerender: true);
            return item.Id;
        }

        var anchor = _outlineItems[index];
        var insertIndex = asSibling ? FindSubtreeEndIndex(index) + 1 : index + 1;
        var depth = asSibling ? anchor.Depth : anchor.Depth + 1;
        var newItem = CreateEmptyItem(depth);
        _outlineItems.Insert(insertIndex, newItem);
        MarkDocumentDirty(newItem.Id, rerender: true);
        return newItem.Id;
    }

    private void ApplyMindMapNodeRename(string text)
    {
        var item = GetOutlineItem(_focusedOutlineItemId);
        if (item is null)
        {
            ShowInfo(T("info.nodeNotFound"));
            return;
        }

        item.Text = SanitizeOutlineTextForSingleLine(text);
        MarkDocumentDirty(item.Id, rerender: true);
        SetStatusKey("status.renamedMindNode");
        SetStatusKey("status.renamedMindNode");
    }

    private void RemoveOutlineSubtree(string itemId)
    {
        if (_outlineItems.Count == 0)
        {
            return;
        }

        var index = FindItemIndex(itemId);
        if (index < 0)
        {
            return;
        }

        var end = FindSubtreeEndIndex(index);
        var removedTitle = SplitOutlineText(_outlineItems[index].Text).Title;
        var fallbackIndex = Math.Max(0, index - 1);
        var fallbackId = fallbackIndex < _outlineItems.Count ? _outlineItems[fallbackIndex].Id : string.Empty;
        _outlineItems.RemoveRange(index, end - index + 1);
        EnsureAtLeastOneItem();
        var focusId = !string.IsNullOrWhiteSpace(fallbackId) && _outlineItems.Any(item => item.Id == fallbackId)
            ? fallbackId
            : _outlineItems[0].Id;
        MarkDocumentDirty(focusId, rerender: true);
        SetStatusKey("status.deletedMindNode", ("title", removedTitle));
        SetStatusKey("status.deletedMindNode", ("title", removedTitle));
    }

    private bool BuildTreeItems(
        IEnumerable<LibraryNode> nodes,
        TreeItem parent,
        string query,
        bool includeContent,
        string? preferredSelection)
    {
        var hasVisibleChild = false;
        foreach (var node in nodes)
        {
            var matchingChildren = node.IsDirectory
                ? node.Children.Where(child => NodeMatches(child, query, includeContent) || DescendantMatches(child, query, includeContent)).ToList()
                : [];

            var isVisible = string.IsNullOrWhiteSpace(query)
                || NodeMatches(node, query, includeContent)
                || matchingChildren.Count > 0;

            if (!isVisible)
            {
                continue;
            }

            hasVisibleChild = true;
            var item = _libraryTree.CreateItem(parent);
            item.SetText(0, node.DisplayTitle);
            item.SetMetadata(0, $"{(node.IsDirectory ? "dir" : "doc")}|{node.RelativePath}");
            item.SetTooltipText(0, node.RelativePath);
            item.SetEditable(0, false);

            if (node.IsDirectory)
            {
                BuildTreeItems(node.Children, item, query, includeContent, preferredSelection);
                item.Collapsed = false;
            }

            if (!string.IsNullOrWhiteSpace(preferredSelection) && node.RelativePath == preferredSelection)
            {
                item.Select(0);
            }
        }

        return hasVisibleChild;
    }

    private static TreeItem? FindTreeItemByPath(TreeItem? root, string relativePath)
    {
        if (root is null)
        {
            return null;
        }

        for (var child = root.GetFirstChild(); child is not null; child = child.GetNext())
        {
            var metadata = child.GetMetadata(0).AsString();
            if (!string.IsNullOrWhiteSpace(metadata) && metadata.EndsWith($"|{relativePath}", StringComparison.Ordinal))
            {
                return child;
            }

            var nested = FindTreeItemByPath(child, relativePath);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private void ApplyCurrentDocumentOverride(LibraryNode node)
    {
        if (_currentDocument is not null && node.RelativePath == _currentDocument.RelativePath && !node.IsDirectory)
        {
            node.DisplayTitle = BuildDisplayTitle();
        }

        foreach (var child in node.Children)
        {
            ApplyCurrentDocumentOverride(child);
        }
    }

    private bool NodeMatches(LibraryNode node, string query, bool includeContent)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var displayTitle = node.RelativePath == _currentDocument?.RelativePath
            ? BuildDisplayTitle()
            : node.DisplayTitle;

        if (displayTitle.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!includeContent || node.IsDirectory)
        {
            return false;
        }

        var content = node.RelativePath == _currentDocument?.RelativePath
            ? BuildMarkdownFromState()
            : _libraryService.ReadDocument(node.RelativePath).Content;

        return content.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private bool DescendantMatches(LibraryNode node, string query, bool includeContent)
    {
        foreach (var child in node.Children)
        {
            if (NodeMatches(child, query, includeContent) || DescendantMatches(child, query, includeContent))
            {
                return true;
            }
        }

        return false;
    }

    private static LibraryNode? FindFirstDocument(LibraryNode node)
    {
        foreach (var child in node.Children)
        {
            if (!child.IsDirectory)
            {
                return child;
            }

            var nested = FindFirstDocument(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static (bool IsDirectory, string RelativePath)? ParseSelection(TreeItem? item)
    {
        if (item is null)
        {
            return null;
        }

        var metadata = item.GetMetadata(0).AsString();
        if (string.IsNullOrWhiteSpace(metadata))
        {
            return null;
        }

        var separatorIndex = metadata.IndexOf('|');
        if (separatorIndex < 0)
        {
            return null;
        }

        var kind = metadata[..separatorIndex];
        var relativePath = metadata[(separatorIndex + 1)..];
        return (kind == "dir", relativePath);
    }

    private OutlineItem? GetOutlineItem(string itemId)
    {
        return _outlineItems.FirstOrDefault(item => item.Id == itemId);
    }

    private int FindItemIndex(string itemId)
    {
        for (var i = 0; i < _outlineItems.Count; i++)
        {
            if (_outlineItems[i].Id == itemId)
            {
                return i;
            }
        }

        return -1;
    }

    private int FindSubtreeEndIndex(int startIndex)
    {
        var baseDepth = _outlineItems[startIndex].Depth;
        var end = startIndex;
        while (end + 1 < _outlineItems.Count && _outlineItems[end + 1].Depth > baseDepth)
        {
            end++;
        }

        return end;
    }

    private static OutlineItem CreateEmptyItem(int depth)
    {
        return new OutlineItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Depth = Math.Max(0, depth),
            Text = string.Empty
        };
    }

    private void ShowInfo(string message)
    {
        _infoDialogLabel.Text = message;
        _infoDialog.PopupCentered(new Vector2I(460, 180));
    }

    private string T(string key)
    {
        return _localization.Get(key);
    }

    private string Tf(string key, params (string Token, string Value)[] replacements)
    {
        return _localization.Format(key, replacements);
    }

    private void SetStatus(string message)
    {
        _statusLabel.Text = message;
        GD.Print(message);
    }

    private void SetStatusKey(string key, params (string Token, string Value)[] replacements)
    {
        SetStatus(Tf(key, replacements));
    }

    private void SetMindMapHint(bool hasNodes)
    {
        if (_mindMapHintLabel is not null)
        {
            _mindMapHintLabel.Text = hasNodes ? T("mindmap.hint.ready") : T("mindmap.hint.empty");
        }
    }

    private void RenderCsvPreview(IEnumerable<MarkdownNode> nodes)
    {
        foreach (var child in _csvSheetRows.GetChildren())
        {
            child.QueueFree();
        }

        _csvSheetRows.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _csvSheetRows.SizeFlagsVertical = SizeFlags.ExpandFill;

        var headerRow = new HBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        headerRow.AddThemeConstantOverride("separation", 0);
        _csvSheetRows.AddChild(headerRow);
        for (var column = 0; column < CsvColumns.Length; column++)
        {
            var headerCell = new PanelContainer();
            headerCell.AddThemeStyleboxOverride("panel", _csvHeaderStyle);
            headerCell.CustomMinimumSize = new Vector2(CsvColumnMinimumWidths[column], 36f);
            headerCell.SizeFlagsHorizontal = column is 4 or 5 or 7 ? SizeFlags.ExpandFill : SizeFlags.Fill;

            var headerLabel = new Label
            {
                Text = CsvColumns[column],
                ClipText = true,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore
            };
            headerLabel.AddThemeColorOverride("font_color", _primaryTextColor);
            headerLabel.AddThemeFontSizeOverride("font_size", 13);
            headerCell.AddChild(headerLabel);
            headerRow.AddChild(headerCell);
        }

        var rowIndex = 0;
        foreach (var node in nodes)
        {
            var row = new HBoxContainer
            {
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            row.AddThemeConstantOverride("separation", 0);
            _csvSheetRows.AddChild(row);

            var values = new[]
            {
                node.Id,
                node.ParentId,
                node.Level.ToString(),
                node.Type,
                node.Title,
                node.Body,
                node.Order.ToString(),
                node.Path
            };

            var cellStyle = rowIndex % 2 == 0 ? _csvCellStyle : _csvCellAltStyle;
            for (var column = 0; column < CsvColumns.Length; column++)
            {
                var cell = new PanelContainer();
                cell.AddThemeStyleboxOverride("panel", cellStyle);
                cell.CustomMinimumSize = new Vector2(CsvColumnMinimumWidths[column], 34f);
                cell.SizeFlagsHorizontal = column is 4 or 5 or 7 ? SizeFlags.ExpandFill : SizeFlags.Fill;

                var label = new Label
                {
                    Text = values[column] ?? string.Empty,
                    ClipText = true,
                    VerticalAlignment = VerticalAlignment.Center
                };
                label.AddThemeColorOverride("font_color", _primaryTextColor);
                label.AddThemeFontSizeOverride("font_size", 13);
                cell.AddChild(label);
                row.AddChild(cell);
            }

            rowIndex++;
        }

        var filler = new PanelContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0f, 360f)
        };
        filler.AddThemeStyleboxOverride("panel", _csvBodyPanelStyle);
        _csvSheetRows.AddChild(filler);
    }

    private string BuildMindMapSvg()
    {
        var nodes = BuildMindMapNodes().ToList();
        EnsureMindMapStateMatches(nodes);
        var nodeLookup = nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var layoutLookup = _currentMindMapState.Nodes.ToDictionary(
            entry => entry.NodeId,
            entry => new Vector2(entry.X, entry.Y),
            StringComparer.Ordinal);

        const float padding = 48f;
        var bounds = new Rect2(new Vector2(float.MaxValue, float.MaxValue), Vector2.Zero);
        var first = true;
        foreach (var node in nodes)
        {
            if (!layoutLookup.TryGetValue(node.Id, out var position))
            {
                continue;
            }

            var size = node.IsRoot
                ? new Vector2(220f, 84f)
                : new Vector2(180f, string.IsNullOrWhiteSpace(node.Body) ? 72f : 96f);
            var rect = new Rect2(position, size);
            bounds = first ? rect : bounds.Merge(rect);
            first = false;
        }

        if (first)
        {
            bounds = new Rect2(0f, 0f, 960f, 640f);
        }

        bounds = new Rect2(bounds.Position - new Vector2(padding, padding), bounds.Size + new Vector2(padding * 2f, padding * 2f));
        var builder = new StringBuilder();
        builder.AppendLine($@"<svg xmlns=""http://www.w3.org/2000/svg"" width=""{bounds.Size.X:F0}"" height=""{bounds.Size.Y:F0}"" viewBox=""0 0 {bounds.Size.X:F0} {bounds.Size.Y:F0}"">");
        builder.AppendLine(@"  <rect width=""100%"" height=""100%"" fill=""#f7fafe"" />");

        foreach (var edge in _currentMindMapState.Edges)
        {
            if (!nodeLookup.TryGetValue(edge.ChildNodeId, out var child))
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(child.ParentId)
                || !nodeLookup.TryGetValue(child.ParentId, out var parent)
                || !layoutLookup.TryGetValue(parent.Id, out var parentPosition)
                || !layoutLookup.TryGetValue(child.Id, out var childPosition))
            {
                continue;
            }

            var parentRect = new Rect2(parentPosition - bounds.Position, parent.IsRoot ? new Vector2(220f, 84f) : new Vector2(180f, string.IsNullOrWhiteSpace(parent.Body) ? 72f : 96f));
            var childRect = new Rect2(childPosition - bounds.Position, child.IsRoot ? new Vector2(220f, 84f) : new Vector2(180f, string.IsNullOrWhiteSpace(child.Body) ? 72f : 96f));
            var parentSide = edge.ParentSide;
            var childSide = edge.ChildSide;
            var start = GetSvgAnchor(parentRect, parentSide);
            var end = GetSvgAnchor(childRect, childSide);
            var route = MindMapEdgeRouteBuilder.BuildRoute(start, parentSide, end, childSide, 1f);
            builder.Append(@"  <polyline fill=""none"" stroke=""#c7d6e9"" stroke-width=""1.5"" points=""");
            builder.Append(string.Join(" ", route.Select(point => $"{point.X:F1},{point.Y:F1}")));
            builder.AppendLine(@""" />");
        }

        foreach (var node in nodes)
        {
            if (!layoutLookup.TryGetValue(node.Id, out var position))
            {
                continue;
            }

            var rectPosition = position - bounds.Position;
            var size = node.IsRoot
                ? new Vector2(220f, 84f)
                : new Vector2(180f, string.IsNullOrWhiteSpace(node.Body) ? 72f : 96f);
            builder.AppendLine($@"  <rect x=""{rectPosition.X:F1}"" y=""{rectPosition.Y:F1}"" width=""{size.X:F1}"" height=""{size.Y:F1}"" rx=""12"" ry=""12"" fill=""#ffffff"" stroke=""#d9e3ef"" stroke-width=""1.5"" />");
            builder.AppendLine($@"  <text x=""{rectPosition.X + 16f:F1}"" y=""{rectPosition.Y + 30f:F1}"" font-size=""{(node.IsRoot ? 18 : 14)}"" font-family=""Segoe UI, Arial, sans-serif"" fill=""#223247"">{EscapeSvg(node.Title)}</text>");
            if (!string.IsNullOrWhiteSpace(node.Body))
            {
                builder.AppendLine($@"  <text x=""{rectPosition.X + 16f:F1}"" y=""{rectPosition.Y + 52f:F1}"" font-size=""11"" font-family=""Segoe UI, Arial, sans-serif"" fill=""#74849a"">{EscapeSvg(node.Body)}</text>");
            }
        }

        builder.AppendLine("</svg>");
        return builder.ToString();
    }

    private static Vector2 GetSvgAnchor(Rect2 rect, MindMapConnectorSide side)
    {
        return side switch
        {
            MindMapConnectorSide.Top => new Vector2(rect.Position.X + rect.Size.X / 2f, rect.Position.Y),
            MindMapConnectorSide.Right => new Vector2(rect.End.X, rect.Position.Y + rect.Size.Y / 2f),
            MindMapConnectorSide.Bottom => new Vector2(rect.Position.X + rect.Size.X / 2f, rect.End.Y),
            MindMapConnectorSide.Left => new Vector2(rect.Position.X, rect.Position.Y + rect.Size.Y / 2f),
            _ => rect.GetCenter()
        };
    }

    private static string EscapeSvg(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }

    private IEnumerable<MindMapNodeViewModel> BuildMindMapNodes()
    {
        var nodes = new List<MindMapNodeViewModel>
        {
            new()
            {
                Id = MindMapRootNodeId,
                ParentId = string.Empty,
                Title = BuildDisplayTitle(),
                Body = string.Empty,
                Level = 1,
                IsRoot = true
            }
        };

        var ancestorStack = new Stack<(int Depth, string Id)>();
        foreach (var item in _outlineItems)
        {
            while (ancestorStack.Count > 0 && ancestorStack.Peek().Depth >= item.Depth)
            {
                ancestorStack.Pop();
            }

            var (title, body) = SplitOutlineText(item.Text);
            var edgeLayout = _currentMindMapState.Edges.FirstOrDefault(edge => edge.ChildNodeId == item.Id);
            nodes.Add(new MindMapNodeViewModel
            {
                Id = item.Id,
                ParentId = ancestorStack.Count > 0 ? ancestorStack.Peek().Id : MindMapRootNodeId,
                Title = title,
                Body = body,
                Level = item.Depth + 2,
                IsRoot = false,
                IncomingParentSide = edgeLayout?.ParentSide,
                IncomingChildSide = edgeLayout?.ChildSide
            });

            ancestorStack.Push((item.Depth, item.Id));
        }

        return nodes;
    }

    private void ResetMindMapLayoutToDefault()
    {
        if (_currentDocument is null)
        {
            return;
        }

        var nodes = BuildMindMapNodes().ToList();
        var positions = BuildAutoMindMapLayout(nodes);
        _currentMindMapState.Nodes = nodes
            .Select(node => new MindMapNodeLayout
            {
                NodeId = node.Id,
                X = positions[node.Id].X,
                Y = positions[node.Id].Y
            })
            .ToList();

        _currentMindMapState.Edges.Clear();
        var nodeLookup = nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (node.IsRoot || string.IsNullOrWhiteSpace(node.ParentId) || !nodeLookup.TryGetValue(node.ParentId, out var parent))
            {
                continue;
            }

            var (parentSide, childSide) = ComputeDefaultConnectorSides(positions[parent.Id], positions[node.Id]);
            _currentMindMapState.Edges.Add(new MindMapEdgeLayout
            {
                ChildNodeId = node.Id,
                ParentSide = parentSide,
                ChildSide = childSide
            });
        }

        _hasUnsavedMindMapChanges = true;
    }

    private Dictionary<string, Vector2> BuildAutoMindMapLayout(IList<MindMapNodeViewModel> nodes)
    {
        var positions = new Dictionary<string, Vector2>(StringComparer.Ordinal);
        var sizeLookup = new Dictionary<string, float>(StringComparer.Ordinal);
        var nodeLookup = nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        var childrenLookup = nodes
            .GroupBy(node => node.ParentId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);

        const float rootCenterY = 360f;
        const float rootX = 180f;
        const float siblingGap = 34f;

        float MeasureSubtree(string nodeId)
        {
            if (sizeLookup.TryGetValue(nodeId, out var cached))
            {
                return cached;
            }

            var node = nodeLookup[nodeId];
            var ownHeight = GetMindMapNodeHeight(node);
            if (!childrenLookup.TryGetValue(nodeId, out var children) || children.Count == 0)
            {
                sizeLookup[nodeId] = ownHeight;
                return ownHeight;
            }

            var totalChildrenHeight = 0f;
            foreach (var child in children)
            {
                totalChildrenHeight += MeasureSubtree(child.Id);
            }

            totalChildrenHeight += siblingGap * Math.Max(0, children.Count - 1);
            sizeLookup[nodeId] = Math.Max(ownHeight, totalChildrenHeight);
            return sizeLookup[nodeId];
        }

        void PlaceNode(string nodeId, float x, float centerY)
        {
            var node = nodeLookup[nodeId];
            var ownHeight = GetMindMapNodeHeight(node);
            positions[nodeId] = new Vector2(x, centerY - ownHeight / 2f);

            if (!childrenLookup.TryGetValue(nodeId, out var children) || children.Count == 0)
            {
                return;
            }

            var totalChildrenHeight = 0f;
            foreach (var child in children)
            {
                totalChildrenHeight += MeasureSubtree(child.Id);
            }

            totalChildrenHeight += siblingGap * Math.Max(0, children.Count - 1);
            var cursor = centerY - totalChildrenHeight / 2f;
            foreach (var child in children)
            {
                var childHeight = MeasureSubtree(child.Id);
                var childCenterY = cursor + childHeight / 2f;
                PlaceNode(child.Id, x + GetMindMapColumnSpacing(child.Level), childCenterY);
                cursor += childHeight + siblingGap;
            }
        }

        foreach (var node in nodes)
        {
            MeasureSubtree(node.Id);
        }

        PlaceNode(MindMapRootNodeId, rootX, rootCenterY);
        return positions;
    }

    private static float GetMindMapNodeHeight(MindMapNodeViewModel node)
    {
        if (node.IsRoot)
        {
            return 84f;
        }

        return string.IsNullOrWhiteSpace(node.Body) ? 72f : 96f;
    }

    private static float GetMindMapColumnSpacing(int level)
    {
        return level <= 2 ? 330f : 270f;
    }

    private void EnsureMindMapStateMatches(IList<MindMapNodeViewModel> nodes)
    {
        var validIds = new HashSet<string>(nodes.Select(node => node.Id), StringComparer.Ordinal);
        _currentMindMapState.Nodes.RemoveAll(layout => !validIds.Contains(layout.NodeId));
        _currentMindMapState.Edges.RemoveAll(layout => !validIds.Contains(layout.ChildNodeId) || layout.ChildNodeId == MindMapRootNodeId);
        _currentMindMapState.Zoom = Math.Clamp(_currentMindMapState.Zoom, 0.5f, 2.5f);
        if (_currentMindMapState.Zoom <= 0f)
        {
            _currentMindMapState.Zoom = 1f;
        }

        var autoLayout = BuildAutoMindMapLayout(nodes);
        foreach (var node in nodes)
        {
            var layout = _currentMindMapState.Nodes.FirstOrDefault(entry => entry.NodeId == node.Id);
            if (layout is null)
            {
                var autoPosition = autoLayout[node.Id];
                layout = new MindMapNodeLayout
                {
                    NodeId = node.Id,
                    X = autoPosition.X,
                    Y = autoPosition.Y
                };
                _currentMindMapState.Nodes.Add(layout);
                _hasUnsavedMindMapChanges = true;
            }

            node.Position = new Vector2(layout.X, layout.Y);
        }

        var nodeLookup = nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
        foreach (var node in nodes)
        {
            if (node.IsRoot || string.IsNullOrWhiteSpace(node.ParentId) || !nodeLookup.TryGetValue(node.ParentId, out var parent))
            {
                continue;
            }

            if (_currentMindMapState.Edges.Any(edge => edge.ChildNodeId == node.Id))
            {
                continue;
            }

            var (parentSide, childSide) = ComputeDefaultConnectorSides(parent.Position, node.Position);
            _currentMindMapState.Edges.Add(new MindMapEdgeLayout
            {
                ChildNodeId = node.Id,
                ParentSide = parentSide,
                ChildSide = childSide
            });
            node.IncomingParentSide = parentSide;
            node.IncomingChildSide = childSide;
            _hasUnsavedMindMapChanges = true;
        }
    }

    private MindMapNodeLayout GetOrCreateNodeLayout(string nodeId)
    {
        var existing = _currentMindMapState.Nodes.FirstOrDefault(layout => layout.NodeId == nodeId);
        if (existing is not null)
        {
            return existing;
        }

        existing = new MindMapNodeLayout
        {
            NodeId = nodeId,
            X = 120f,
            Y = 120f
        };
        _currentMindMapState.Nodes.Add(existing);
        return existing;
    }

    private void SaveEdgeLayout(
        string childNodeId,
        MindMapConnectorSide parentSide,
        MindMapConnectorSide childSide)
    {
        var edge = _currentMindMapState.Edges.FirstOrDefault(entry => entry.ChildNodeId == childNodeId);
        if (edge is null)
        {
            edge = new MindMapEdgeLayout
            {
                ChildNodeId = childNodeId
            };
            _currentMindMapState.Edges.Add(edge);
        }

        edge.ParentSide = parentSide;
        edge.ChildSide = childSide;
    }

    private static (MindMapConnectorSide ParentSide, MindMapConnectorSide ChildSide) ComputeDefaultConnectorSides(
        Vector2 parentPosition,
        Vector2 childPosition)
    {
        var delta = childPosition - parentPosition;
        if (Math.Abs(delta.X) >= Math.Abs(delta.Y) * 0.55f)
        {
            return delta.X >= 0f
                ? (MindMapConnectorSide.Right, MindMapConnectorSide.Left)
                : (MindMapConnectorSide.Left, MindMapConnectorSide.Right);
        }

        return delta.Y >= 0f
            ? (MindMapConnectorSide.Bottom, MindMapConnectorSide.Top)
            : (MindMapConnectorSide.Top, MindMapConnectorSide.Bottom);
    }

    private bool ReparentOutlineItem(string itemId, string newParentId)
    {
        var sourceIndex = FindItemIndex(itemId);
        if (sourceIndex < 0)
        {
            return false;
        }

        var sourceEnd = FindSubtreeEndIndex(sourceIndex);
        var subtree = _outlineItems.Skip(sourceIndex).Take(sourceEnd - sourceIndex + 1).Select(item => new OutlineItem
        {
            Id = item.Id,
            Depth = item.Depth,
            Text = item.Text
        }).ToList();

        if (newParentId == itemId)
        {
            return false;
        }

        var targetDepth = -1;
        var insertIndex = _outlineItems.Count;
        if (!string.IsNullOrWhiteSpace(newParentId) && newParentId != MindMapRootNodeId)
        {
            var targetIndex = FindItemIndex(newParentId);
            if (targetIndex < 0 || (targetIndex >= sourceIndex && targetIndex <= sourceEnd))
            {
                return false;
            }

            targetDepth = _outlineItems[targetIndex].Depth;
            insertIndex = FindSubtreeEndIndex(targetIndex) + 1;
        }

        _outlineItems.RemoveRange(sourceIndex, subtree.Count);
        if (insertIndex > sourceIndex)
        {
            insertIndex -= subtree.Count;
        }

        var depthDelta = (targetDepth + 1) - subtree[0].Depth;
        foreach (var item in subtree)
        {
            item.Depth = Math.Max(0, item.Depth + depthDelta);
        }

        _outlineItems.InsertRange(insertIndex, subtree);
        return true;
    }

    private static (string Title, string Body) SplitOutlineText(string text)
    {
        var normalized = text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return ("Untitled", string.Empty);
        }

        var lines = normalized.Split('\n');
        var firstNonEmptyIndex = Array.FindIndex(lines, line => !string.IsNullOrWhiteSpace(line));
        if (firstNonEmptyIndex < 0)
        {
            return ("Untitled", string.Empty);
        }

        var title = MarkdownTitleHelper.NormalizeDisplayTitle(lines[firstNonEmptyIndex]);
        return (string.IsNullOrWhiteSpace(title) ? "Untitled" : title, string.Empty);
    }

    private void FocusOutlineEditor(string? focusItemId)
    {
        if (string.IsNullOrWhiteSpace(focusItemId) || !_outlineEditors.TryGetValue(focusItemId, out var editor))
        {
            return;
        }

        editor.GrabFocus();
    }

    private Control BuildOutlineIndentGuides(int depth)
    {
        if (depth <= 0)
        {
            return new Control
            {
                CustomMinimumSize = new Vector2(8, 0)
            };
        }

        var guides = new HBoxContainer
        {
            CustomMinimumSize = new Vector2(depth * 20, 0)
        };
        guides.AddThemeConstantOverride("separation", 11);

        for (var i = 0; i < depth; i++)
        {
            var slot = new CenterContainer
            {
                CustomMinimumSize = new Vector2(9, 0)
            };
            var guide = new ColorRect
            {
                Color = _softButtonDisabledStyle.BorderColor,
                CustomMinimumSize = new Vector2(1, 0),
                SizeFlagsVertical = SizeFlags.ExpandFill
            };
            slot.AddChild(guide);
            guides.AddChild(slot);
        }

        return guides;
    }

    private static string SanitizeOutlineTextForSingleLine(string text)
    {
        return text
            .Replace("\r\n", " ", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Trim();
    }

    private static string NormalizeOutlineText(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .TrimEnd();
    }
}


