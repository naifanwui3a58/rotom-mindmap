using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using RotomMindmap.Domain;

namespace RotomMindmap.Services;

public sealed class LibraryService
{
    private const string WorkspaceFolderName = "workspace";
    private const string VaultFolderName = "vault";
    private const string TrashFolderName = ".trash";
    private const string ExportsFolderName = "exports";
    private const string MindMapFolderName = ".mindmap";
    private const string TrashMetaFileName = "meta.json";
    private static readonly HashSet<string> SupportedImportExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".md",
        ".markdown",
        ".txt"
    };

    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public string WorkspaceRootPath { get; }

    public string VaultRootPath { get; }

    public string TrashRootPath { get; }

    public string ExportRootPath { get; }

    public string MindMapRootPath { get; }

    public LibraryService()
    {
        AppPaths.SeedEditorWorkspaceFromLegacyIfNeeded();
        WorkspaceRootPath = AppPaths.WorkspaceRootPath;
        VaultRootPath = Path.Combine(WorkspaceRootPath, VaultFolderName);
        TrashRootPath = Path.Combine(WorkspaceRootPath, TrashFolderName);
        ExportRootPath = Path.Combine(WorkspaceRootPath, ExportsFolderName);
        MindMapRootPath = Path.Combine(WorkspaceRootPath, MindMapFolderName);
    }

    public void Initialize()
    {
        Directory.CreateDirectory(WorkspaceRootPath);
        Directory.CreateDirectory(VaultRootPath);
        Directory.CreateDirectory(TrashRootPath);
        Directory.CreateDirectory(ExportRootPath);
        Directory.CreateDirectory(MindMapRootPath);

        if (!Directory.EnumerateFileSystemEntries(VaultRootPath).Any())
        {
            SeedWelcomeDocument();
        }

        NormalizeWelcomeSeed();
    }

    public LibraryNode BuildLibraryTree()
    {
        var root = new LibraryNode
        {
            Name = "Library",
            RelativePath = string.Empty,
            DisplayTitle = "Library",
            IsDirectory = true
        };

        PopulateChildren(root, VaultRootPath);
        return root;
    }

    public DocumentSnapshot ReadDocument(string relativePath)
    {
        var fullPath = ToVaultAbsolutePath(relativePath);
        var content = File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;
        return new DocumentSnapshot
        {
            RelativePath = relativePath.Replace('\\', '/'),
            FileName = Path.GetFileName(fullPath),
            Content = content,
            DisplayTitle = MarkdownTitleHelper.ExtractDisplayTitle(content, Path.GetFileNameWithoutExtension(fullPath))
        };
    }

    public DocumentSnapshot CreateDocument(string selectedFolderRelativePath, string requestedName)
    {
        var folderPath = ResolveFolderAbsolutePath(selectedFolderRelativePath);
        Directory.CreateDirectory(folderPath);
        var cleanName = string.IsNullOrWhiteSpace(requestedName) ? "Untitled" : requestedName.Trim();
        var filePath = BuildUniqueFilePath(folderPath, cleanName, ".md");
        var content = MarkdownTitleHelper.CreateInitialContent(cleanName);
        File.WriteAllText(filePath, content);
        return ReadDocument(ToVaultRelativePath(filePath));
    }

    public string CreateFolder(string selectedFolderRelativePath, string requestedName)
    {
        var parentFolder = ResolveFolderAbsolutePath(selectedFolderRelativePath);
        var cleanName = string.IsNullOrWhiteSpace(requestedName) ? "New Folder" : requestedName.Trim();
        var folderPath = BuildUniqueFolderPath(parentFolder, cleanName);
        Directory.CreateDirectory(folderPath);
        return ToVaultRelativePath(folderPath);
    }

    public IReadOnlyList<string> ImportExternalPaths(IEnumerable<string> sourcePaths, string targetFolderRelativePath)
    {
        var importedDocuments = new List<string>();
        var targetFolderPath = ResolveFolderAbsolutePath(targetFolderRelativePath);
        Directory.CreateDirectory(targetFolderPath);

        foreach (var sourcePath in sourcePaths.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (Directory.Exists(sourcePath))
            {
                ImportExternalDirectory(sourcePath, targetFolderPath, importedDocuments);
                continue;
            }

            if (File.Exists(sourcePath))
            {
                var imported = ImportExternalFile(sourcePath, targetFolderPath);
                if (!string.IsNullOrWhiteSpace(imported))
                {
                    importedDocuments.Add(imported);
                }
            }
        }

        return importedDocuments;
    }

    public void SaveDocument(string relativePath, string content)
    {
        var fullPath = ToVaultAbsolutePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, content);
    }

    public string ResolveLibraryAbsolutePath(string relativePath, bool isDirectory)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return VaultRootPath;
        }

        var absolutePath = ToVaultAbsolutePath(relativePath);
        if (isDirectory)
        {
            return absolutePath;
        }

        return File.Exists(absolutePath)
            ? absolutePath
            : ResolveFolderAbsolutePath(relativePath);
    }

    public DocumentSnapshot RenameDocument(string relativePath, string requestedName)
    {
        var sourcePath = ToVaultAbsolutePath(relativePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Document was not found.", sourcePath);
        }

        var folderPath = Path.GetDirectoryName(sourcePath)!;
        var cleanName = string.IsNullOrWhiteSpace(requestedName) ? "Untitled" : requestedName.Trim();
        var targetPath = BuildUniqueFilePath(folderPath, cleanName, ".md", sourcePath);
        if (!string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            File.Move(sourcePath, targetPath);
            MoveMindMapStateFile(relativePath, ToVaultRelativePath(targetPath));
        }

        return ReadDocument(ToVaultRelativePath(targetPath));
    }

    public string RenameFolder(string relativePath, string requestedName)
    {
        var sourcePath = ToVaultAbsolutePath(relativePath);
        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException($"Folder was not found: {sourcePath}");
        }

        var parentPath = Path.GetDirectoryName(sourcePath) ?? VaultRootPath;
        var cleanName = string.IsNullOrWhiteSpace(requestedName) ? "New Folder" : requestedName.Trim();
        var targetPath = BuildUniqueFolderPath(parentPath, cleanName, sourcePath);
        if (!string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            Directory.Move(sourcePath, targetPath);
            MoveMindMapStateDirectory(relativePath, ToVaultRelativePath(targetPath));
        }

        return ToVaultRelativePath(targetPath);
    }

    public DocumentSnapshot MoveDocumentToFolder(string relativePath, string targetFolderRelativePath)
    {
        var sourcePath = ToVaultAbsolutePath(relativePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("Document was not found.", sourcePath);
        }

        var targetFolderPath = ResolveFolderAbsolutePath(targetFolderRelativePath);
        if (File.Exists(targetFolderPath))
        {
            throw new InvalidOperationException("Target folder path points to a file.");
        }

        Directory.CreateDirectory(targetFolderPath);
        var targetPath = BuildUniqueMovedPath(targetFolderPath, Path.GetFileName(sourcePath), sourcePath);
        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return ReadDocument(relativePath);
        }

        File.Move(sourcePath, targetPath);
        MoveMindMapStateFile(relativePath, ToVaultRelativePath(targetPath));
        return ReadDocument(ToVaultRelativePath(targetPath));
    }

    public string MoveFolderToFolder(string relativePath, string targetFolderRelativePath)
    {
        var normalizedSource = relativePath.Replace('\\', '/').Trim('/');
        var normalizedTarget = targetFolderRelativePath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalizedSource))
        {
            throw new InvalidOperationException("Root folder cannot be moved.");
        }

        if (string.Equals(normalizedSource, normalizedTarget, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A folder cannot be moved into itself.");
        }

        if (normalizedTarget.StartsWith($"{normalizedSource}/", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A folder cannot be moved into its own child folder.");
        }

        var sourcePath = ToVaultAbsolutePath(normalizedSource);
        if (!Directory.Exists(sourcePath))
        {
            throw new DirectoryNotFoundException($"Folder was not found: {sourcePath}");
        }

        var targetFolderPath = ResolveFolderAbsolutePath(normalizedTarget);
        if (File.Exists(targetFolderPath))
        {
            throw new InvalidOperationException("Target folder path points to a file.");
        }

        Directory.CreateDirectory(targetFolderPath);
        var targetPath = BuildUniqueFolderPath(targetFolderPath, Path.GetFileName(sourcePath), sourcePath);
        if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedSource;
        }

        Directory.Move(sourcePath, targetPath);
        MoveMindMapStateDirectory(normalizedSource, ToVaultRelativePath(targetPath));
        return ToVaultRelativePath(targetPath);
    }

    public TrashEntry MoveToTrash(string relativePath)
    {
        var sourcePath = ToVaultAbsolutePath(relativePath);
        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
        {
            throw new FileNotFoundException("Selected path does not exist.", sourcePath);
        }

        var trashId = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}";
        var entryRoot = Path.Combine(TrashRootPath, trashId);
        Directory.CreateDirectory(entryRoot);
        var payloadRoot = Path.Combine(entryRoot, "payload");
        Directory.CreateDirectory(payloadRoot);
        var destination = Path.Combine(payloadRoot, Path.GetFileName(sourcePath));

        var isDirectory = Directory.Exists(sourcePath);
        if (isDirectory)
        {
            Directory.Move(sourcePath, destination);
        }
        else
        {
            File.Move(sourcePath, destination);
        }

        var entry = new TrashEntry
        {
            TrashId = trashId,
            OriginalRelativePath = relativePath.Replace('\\', '/'),
            StoredName = Path.GetFileName(sourcePath),
            IsDirectory = isDirectory,
            DeletedAtUtc = DateTime.UtcNow
        };

        var metaPath = Path.Combine(entryRoot, TrashMetaFileName);
        File.WriteAllText(metaPath, JsonSerializer.Serialize(entry, _jsonOptions));
        return entry;
    }

    public IReadOnlyList<TrashEntry> GetTrashEntries()
    {
        var entries = new List<TrashEntry>();
        if (!Directory.Exists(TrashRootPath))
        {
            return entries;
        }

        foreach (var folder in Directory.EnumerateDirectories(TrashRootPath).OrderByDescending(path => path))
        {
            var metaPath = Path.Combine(folder, TrashMetaFileName);
            if (!File.Exists(metaPath))
            {
                continue;
            }

            var json = File.ReadAllText(metaPath);
            var entry = JsonSerializer.Deserialize<TrashEntry>(json);
            if (entry is not null)
            {
                entries.Add(entry);
            }
        }

        return entries;
    }

    public void RestoreTrashEntry(string trashId)
    {
        var entry = GetTrashEntry(trashId);
        var source = Path.Combine(TrashRootPath, trashId, "payload", entry.StoredName);
        var destination = ToVaultAbsolutePath(entry.OriginalRelativePath);
        var destinationFolder = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(destinationFolder))
        {
            Directory.CreateDirectory(destinationFolder);
        }

        if (entry.IsDirectory)
        {
            destination = BuildNonConflictingPath(destination, true);
            Directory.Move(source, destination);
        }
        else
        {
            destination = BuildNonConflictingPath(destination, false);
            File.Move(source, destination);
        }

        Directory.Delete(Path.Combine(TrashRootPath, trashId), true);
    }

    public void PurgeTrashEntry(string trashId)
    {
        var entryRoot = Path.Combine(TrashRootPath, trashId);
        if (Directory.Exists(entryRoot))
        {
            Directory.Delete(entryRoot, true);
        }
    }

    public string ExportCsv(string documentDisplayTitle, string csvContent)
    {
        return ExportText(documentDisplayTitle, csvContent, ".csv");
    }

    public string ExportMarkdown(string documentDisplayTitle, string markdownContent)
    {
        return ExportText(documentDisplayTitle, markdownContent, ".md");
    }

    public string ExportSvg(string documentDisplayTitle, string svgContent)
    {
        return ExportText(documentDisplayTitle, svgContent, ".svg");
    }

    public string CreateExportPath(string documentDisplayTitle, string extension)
    {
        Directory.CreateDirectory(ExportRootPath);
        var safeExtension = extension.StartsWith('.') ? extension : $".{extension}";
        var fileName = $"{MarkdownTitleHelper.Slugify(documentDisplayTitle)}-{DateTime.Now:yyyyMMdd-HHmmss}{safeExtension}";
        return Path.Combine(ExportRootPath, fileName);
    }

    private string ExportText(string documentDisplayTitle, string content, string extension)
    {
        var fullPath = CreateExportPath(documentDisplayTitle, extension);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public MindMapDocumentState ReadMindMapState(string relativePath)
    {
        var statePath = ToMindMapStatePath(relativePath);
        if (!File.Exists(statePath))
        {
            return new MindMapDocumentState();
        }

        try
        {
            var state = JsonSerializer.Deserialize<MindMapDocumentState>(File.ReadAllText(statePath), _jsonOptions);
            return state ?? new MindMapDocumentState();
        }
        catch (JsonException)
        {
            return new MindMapDocumentState();
        }
    }

    public void SaveMindMapState(string relativePath, MindMapDocumentState state)
    {
        var statePath = ToMindMapStatePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(statePath)!);
        File.WriteAllText(statePath, JsonSerializer.Serialize(state, _jsonOptions));
    }

    private void PopulateChildren(LibraryNode node, string absolutePath)
    {
        foreach (var directory in Directory.EnumerateDirectories(absolutePath).OrderBy(Path.GetFileName))
        {
            var child = new LibraryNode
            {
                Name = Path.GetFileName(directory),
                RelativePath = ToVaultRelativePath(directory),
                DisplayTitle = Path.GetFileName(directory),
                IsDirectory = true
            };
            PopulateChildren(child, directory);
            node.Children.Add(child);
        }

        foreach (var file in Directory.EnumerateFiles(absolutePath, "*.md").OrderBy(Path.GetFileName))
        {
            var content = File.ReadAllText(file);
            node.Children.Add(new LibraryNode
            {
                Name = Path.GetFileName(file),
                RelativePath = ToVaultRelativePath(file),
                DisplayTitle = MarkdownTitleHelper.ExtractDisplayTitle(content, Path.GetFileNameWithoutExtension(file)),
                IsDirectory = false
            });
        }
    }

    private TrashEntry GetTrashEntry(string trashId)
    {
        var metaPath = Path.Combine(TrashRootPath, trashId, TrashMetaFileName);
        if (!File.Exists(metaPath))
        {
            throw new FileNotFoundException("Trash entry metadata was not found.", metaPath);
        }

        var entry = JsonSerializer.Deserialize<TrashEntry>(File.ReadAllText(metaPath));
        return entry ?? throw new InvalidOperationException("Trash entry metadata is invalid.");
    }

    private string ResolveFolderAbsolutePath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return VaultRootPath;
        }

        var candidate = ToVaultAbsolutePath(normalized);
        if (File.Exists(candidate))
        {
            return Path.GetDirectoryName(candidate)!;
        }

        return candidate;
    }

    private string ToVaultAbsolutePath(string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);
        return Path.Combine(VaultRootPath, normalized);
    }

    private string ToVaultRelativePath(string absolutePath)
    {
        return Path.GetRelativePath(VaultRootPath, absolutePath).Replace('\\', '/');
    }

    private static string BuildUniqueFilePath(
        string folderPath,
        string rawName,
        string extension,
        string? existingPathToIgnore = null)
    {
        var slug = MarkdownTitleHelper.Slugify(rawName);
        var fullPath = Path.Combine(folderPath, $"{slug}{extension}");
        var counter = 2;
        while (PathExistsAndIsNotIgnored(fullPath, existingPathToIgnore))
        {
            fullPath = Path.Combine(folderPath, $"{slug}-{counter}{extension}");
            counter++;
        }

        return fullPath;
    }

    private static string BuildUniqueFolderPath(
        string parentPath,
        string rawName,
        string? existingPathToIgnore = null)
    {
        var slug = MarkdownTitleHelper.Slugify(rawName);
        var fullPath = Path.Combine(parentPath, slug);
        var counter = 2;
        while (PathExistsAndIsNotIgnored(fullPath, existingPathToIgnore))
        {
            fullPath = Path.Combine(parentPath, $"{slug}-{counter}");
            counter++;
        }

        return fullPath;
    }

    private static string BuildUniqueMovedPath(
        string folderPath,
        string fileName,
        string? existingPathToIgnore = null)
    {
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var fullPath = Path.Combine(folderPath, fileName);
        var counter = 2;
        while (PathExistsAndIsNotIgnored(fullPath, existingPathToIgnore))
        {
            fullPath = Path.Combine(folderPath, $"{baseName}-{counter}{extension}");
            counter++;
        }

        return fullPath;
    }

    private void ImportExternalDirectory(string sourceDirectoryPath, string targetParentFolderPath, ICollection<string> importedDocuments)
    {
        var folderName = Path.GetFileName(sourceDirectoryPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var targetFolderPath = BuildUniqueFolderPath(targetParentFolderPath, folderName);
        Directory.CreateDirectory(targetFolderPath);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectoryPath).OrderBy(Path.GetFileName))
        {
            ImportExternalDirectory(directory, targetFolderPath, importedDocuments);
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectoryPath).OrderBy(Path.GetFileName))
        {
            var imported = ImportExternalFile(file, targetFolderPath);
            if (!string.IsNullOrWhiteSpace(imported))
            {
                importedDocuments.Add(imported);
            }
        }
    }

    private string ImportExternalFile(string sourceFilePath, string targetFolderPath)
    {
        var extension = Path.GetExtension(sourceFilePath);
        if (!SupportedImportExtensions.Contains(extension))
        {
            return string.Empty;
        }

        Directory.CreateDirectory(targetFolderPath);
        var sourceName = Path.GetFileNameWithoutExtension(sourceFilePath);
        var targetFilePath = BuildUniqueFilePath(targetFolderPath, sourceName, ".md");
        var importedContent = BuildImportedMarkdownContent(sourceFilePath, extension);
        File.WriteAllText(targetFilePath, importedContent);
        return ToVaultRelativePath(targetFilePath);
    }

    private static string BuildImportedMarkdownContent(string sourceFilePath, string extension)
    {
        var rawText = File.ReadAllText(sourceFilePath);
        if (extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".markdown", StringComparison.OrdinalIgnoreCase))
        {
            return rawText;
        }

        var title = MarkdownTitleHelper.NormalizeDisplayTitle(Path.GetFileNameWithoutExtension(sourceFilePath));
        if (string.IsNullOrWhiteSpace(title))
        {
            title = "Untitled";
        }

        var builder = new StringBuilder();
        builder.Append("# ");
        builder.AppendLine(title);
        builder.AppendLine();

        var lines = rawText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var hasAnyItem = false;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            builder.Append("- ");
            builder.AppendLine(line);
            hasAnyItem = true;
        }

        if (!hasAnyItem)
        {
            builder.AppendLine("- ");
        }

        return builder.ToString();
    }

    private static string BuildNonConflictingPath(string destination, bool isDirectory)
    {
        if (!File.Exists(destination) && !Directory.Exists(destination))
        {
            return destination;
        }

        var folder = Path.GetDirectoryName(destination)!;
        var name = isDirectory
            ? Path.GetFileName(destination)
            : Path.GetFileNameWithoutExtension(destination);
        var extension = isDirectory ? string.Empty : Path.GetExtension(destination);
        var counter = 2;
        var candidate = destination;
        while (File.Exists(candidate) || Directory.Exists(candidate))
        {
            candidate = Path.Combine(folder, $"{name}-restored-{counter}{extension}");
            counter++;
        }

        return candidate;
    }

    private string ToMindMapStatePath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return Path.Combine(MindMapRootPath, "default.json");
        }

        var relativeFilePath = normalized.Replace('/', Path.DirectorySeparatorChar);
        var folder = Path.GetDirectoryName(relativeFilePath);
        var fileName = $"{Path.GetFileNameWithoutExtension(relativeFilePath)}.json";
        return string.IsNullOrWhiteSpace(folder)
            ? Path.Combine(MindMapRootPath, fileName)
            : Path.Combine(MindMapRootPath, folder, fileName);
    }

    private void NormalizeWelcomeSeed()
    {
        var welcomePath = Path.Combine(VaultRootPath, "inbox", "welcome.md");
        if (!File.Exists(welcomePath))
        {
            return;
        }

        var content = File.ReadAllText(welcomePath);
        if (!LooksLikeBrokenWelcome(content))
        {
            return;
        }

        File.WriteAllText(welcomePath,
            BuildWelcomeMarkdown());

        var welcomeMindMapStatePath = ToMindMapStatePath("inbox/welcome.md");
        if (File.Exists(welcomeMindMapStatePath))
        {
            File.Delete(welcomeMindMapStatePath);
        }
    }

    private void SeedWelcomeDocument()
    {
        var inboxRelativePath = CreateFolder(string.Empty, "Inbox");
        var inboxAbsolutePath = ResolveFolderAbsolutePath(inboxRelativePath);
        var welcomePath = Path.Combine(inboxAbsolutePath, "welcome.md");
        File.WriteAllText(welcomePath, BuildWelcomeMarkdown());
    }

    private static string BuildWelcomeMarkdown()
    {
        return
            """
            # 欢迎使用 Rotom Mindmap

            - 这是一个本地优先的大纲写作、脑图整理和 CSV 导出工具。
            - 上方标题区域可以直接改当前文档标题。
            - 中间直接输入条目即可，不需要手打 Markdown 的 #。
            - 按 Enter 新建同级条目。
            - 按 Tab 每次缩进一级。
            - 按 Shift+Tab 每次反缩进一级。
            - 左侧文库会和当前文档标题实时同步。
            - 上方脑图页可以重新排布、拖动节点，并导出图片或 SVG。
            - 上方 CSV 页可以把当前大纲导出成配置表。
            - 右键文档或文件夹可以重命名、删除、拖动整理，或打开所在位置。
            - 删除不会立刻永久丢失，内容会先进入回收站。
            - 左上角可以切换中英文界面。
            """;
    }

    private static bool LooksLikeBrokenWelcome(string markdown)
    {
        if (!markdown.StartsWith("# Welcome", StringComparison.Ordinal))
        {
            return false;
        }

        var listLines = markdown
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.StartsWith("-", StringComparison.Ordinal))
            .Select(line => line[1..].Trim())
            .ToList();

        return listLines.Count >= 4
            && listLines.All(line => string.IsNullOrWhiteSpace(line) || line.All(char.IsDigit));
    }

    private static bool PathExistsAndIsNotIgnored(string candidatePath, string? existingPathToIgnore)
    {
        if (!string.IsNullOrWhiteSpace(existingPathToIgnore)
            && string.Equals(candidatePath, existingPathToIgnore, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return File.Exists(candidatePath) || Directory.Exists(candidatePath);
    }

    private void MoveMindMapStateFile(string oldRelativePath, string newRelativePath)
    {
        var oldStatePath = ToMindMapStatePath(oldRelativePath);
        if (!File.Exists(oldStatePath))
        {
            return;
        }

        var newStatePath = ToMindMapStatePath(newRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(newStatePath)!);
        if (File.Exists(newStatePath))
        {
            File.Delete(newStatePath);
        }

        File.Move(oldStatePath, newStatePath);
        DeleteEmptyMindMapDirectories(Path.GetDirectoryName(oldStatePath));
    }

    private void MoveMindMapStateDirectory(string oldRelativePath, string newRelativePath)
    {
        var oldStateDirectory = ToMindMapDirectoryPath(oldRelativePath);
        if (!Directory.Exists(oldStateDirectory))
        {
            return;
        }

        var newStateDirectory = ToMindMapDirectoryPath(newRelativePath);
        var parentDirectory = Path.GetDirectoryName(newStateDirectory);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }

        if (Directory.Exists(newStateDirectory))
        {
            Directory.Delete(newStateDirectory, true);
        }

        Directory.Move(oldStateDirectory, newStateDirectory);
        DeleteEmptyMindMapDirectories(Path.GetDirectoryName(oldStateDirectory));
    }

    private string ToMindMapDirectoryPath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return MindMapRootPath;
        }

        return Path.Combine(MindMapRootPath, normalized.Replace('/', Path.DirectorySeparatorChar));
    }

    private void DeleteEmptyMindMapDirectories(string? directoryPath)
    {
        while (!string.IsNullOrWhiteSpace(directoryPath)
               && !string.Equals(directoryPath, MindMapRootPath, StringComparison.OrdinalIgnoreCase)
               && Directory.Exists(directoryPath)
               && !Directory.EnumerateFileSystemEntries(directoryPath).Any())
        {
            var parent = Path.GetDirectoryName(directoryPath);
            Directory.Delete(directoryPath);
            directoryPath = parent;
        }
    }
}
