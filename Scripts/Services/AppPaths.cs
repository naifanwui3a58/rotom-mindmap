using System.IO;
using System.Linq;
using Godot;

namespace RotomMindmap.Services;

public static class AppPaths
{
    private const string LegacyWorkspaceFolderName = "workspace";
    private const string EditorWorkspaceFolderName = "workspace-editor";
    private const string RuntimeWorkspaceFolderName = "workspace-app";

    public static bool IsEditorSession => OS.HasFeature("editor");

    public static string WorkspaceFolderName => IsEditorSession
        ? EditorWorkspaceFolderName
        : RuntimeWorkspaceFolderName;

    public static string WorkspaceRootPath => ProjectSettings.GlobalizePath($"user://{WorkspaceFolderName}");

    public static string LegacyWorkspaceRootPath => ProjectSettings.GlobalizePath($"user://{LegacyWorkspaceFolderName}");

    public static string UiSettingsAbsolutePath => Path.Combine(WorkspaceRootPath, "ui-settings.json");

    public static void SeedEditorWorkspaceFromLegacyIfNeeded()
    {
        if (!IsEditorSession)
        {
            return;
        }

        if (Directory.Exists(WorkspaceRootPath) && Directory.EnumerateFileSystemEntries(WorkspaceRootPath).Any())
        {
            return;
        }

        if (!Directory.Exists(LegacyWorkspaceRootPath))
        {
            return;
        }

        CopyDirectoryContents(LegacyWorkspaceRootPath, WorkspaceRootPath);
    }

    private static void CopyDirectoryContents(string sourceRootPath, string destinationRootPath)
    {
        Directory.CreateDirectory(destinationRootPath);

        foreach (var directoryPath in Directory.EnumerateDirectories(sourceRootPath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRootPath, directoryPath);
            Directory.CreateDirectory(Path.Combine(destinationRootPath, relativePath));
        }

        foreach (var filePath in Directory.EnumerateFiles(sourceRootPath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceRootPath, filePath);
            var targetPath = Path.Combine(destinationRootPath, relativePath);
            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.Copy(filePath, targetPath, overwrite: true);
        }
    }
}
