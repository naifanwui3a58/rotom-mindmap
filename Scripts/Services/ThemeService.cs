using System;
using System.IO;
using System.Text.Json;
using Godot;

namespace RotomMindmap.Services;

public enum ThemeMode
{
    System = 0,
    Light = 1,
    Dark = 2
}

public sealed class ThemeService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private ThemeMode _mode = ThemeMode.System;

    public ThemeMode CurrentMode => _mode;

    public ThemeMode Initialize()
    {
        _mode = LoadPersistedMode();
        return _mode;
    }

    public ThemeMode ResolveEffectiveMode()
    {
        if (_mode == ThemeMode.System)
        {
            return IsSystemDarkMode() ? ThemeMode.Dark : ThemeMode.Light;
        }

        return _mode;
    }

    public bool SetMode(ThemeMode mode)
    {
        _mode = mode;
        PersistMode(mode);
        return true;
    }

    public string GetModeLabel(ThemeMode mode)
    {
        return mode switch
        {
            ThemeMode.System => "跟随系统",
            ThemeMode.Light => "浅色",
            ThemeMode.Dark => "深色",
            _ => "跟随系统"
        };
    }

    public static bool IsSystemDarkMode()
    {
        try
        {
            return DisplayServer.IsDarkMode();
        }
        catch
        {
            return false;
        }
    }

    private static ThemeMode LoadPersistedMode()
    {
        AppPaths.SeedEditorWorkspaceFromLegacyIfNeeded();
        var path = AppPaths.UiSettingsAbsolutePath;
        if (!File.Exists(path))
        {
            return ThemeMode.System;
        }

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<UiSettings>(json);
            if (settings is null || string.IsNullOrWhiteSpace(settings.ThemeMode))
            {
                return ThemeMode.System;
            }

            return Enum.TryParse(settings.ThemeMode, ignoreCase: true, out ThemeMode parsed)
                ? parsed
                : ThemeMode.System;
        }
        catch
        {
            return ThemeMode.System;
        }
    }

    private static void PersistMode(ThemeMode mode)
    {
        AppPaths.SeedEditorWorkspaceFromLegacyIfNeeded();
        var path = AppPaths.UiSettingsAbsolutePath;
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        UiSettings settings;
        if (File.Exists(path))
        {
            try
            {
                settings = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(path)) ?? new UiSettings();
            }
            catch
            {
                settings = new UiSettings();
            }
        }
        else
        {
            settings = new UiSettings();
        }

        settings.ThemeMode = mode.ToString().ToLowerInvariant();
        File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
    }
}
