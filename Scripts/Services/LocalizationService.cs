using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace RotomMindmap.Services;

public sealed class LocalizationService
{
    private readonly Dictionary<string, Dictionary<string, string>> _tables = new(StringComparer.OrdinalIgnoreCase);
    private string _currentLocale = "zh-CN";

    public string CurrentLocale => _currentLocale;

    public IReadOnlyList<string> SupportedLocales => ["zh-CN", "en"];

    public void Initialize()
    {
        foreach (var locale in SupportedLocales)
        {
            LoadTable(locale);
        }

        var persistedLocale = LoadPersistedLocale();
        _currentLocale = _tables.ContainsKey(persistedLocale) ? persistedLocale : "zh-CN";
    }

    public string Get(string key)
    {
        if (_tables.TryGetValue(_currentLocale, out var currentTable)
            && currentTable.TryGetValue(key, out var localizedValue))
        {
            return localizedValue;
        }

        if (_tables.TryGetValue("zh-CN", out var defaultTable)
            && defaultTable.TryGetValue(key, out var defaultValue))
        {
            return defaultValue;
        }

        return key;
    }

    public string Format(string key, params (string Token, string Value)[] replacements)
    {
        var text = Get(key);
        foreach (var (token, value) in replacements)
        {
            text = text.Replace($"{{{token}}}", value, StringComparison.Ordinal);
        }

        return text;
    }

    public bool SetCurrentLocale(string locale)
    {
        if (!_tables.ContainsKey(locale))
        {
            return false;
        }

        _currentLocale = locale;
        PersistLocale(locale);
        return true;
    }

    private void LoadTable(string locale)
    {
        var path = $"res://Data/Localization/{locale}.json";
        if (!Godot.FileAccess.FileExists(path))
        {
            _tables[locale] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        if (file is null)
        {
            _tables[locale] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return;
        }

        var json = file.GetAsText();
        try
        {
            var table = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            _tables[locale] = new Dictionary<string, string>(table, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            _tables[locale] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string LoadPersistedLocale()
    {
        AppPaths.SeedEditorWorkspaceFromLegacyIfNeeded();
        var path = AppPaths.UiSettingsAbsolutePath;
        if (!File.Exists(path))
        {
            return "zh-CN";
        }

        try
        {
            var settings = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(path));
            if (!string.IsNullOrWhiteSpace(settings?.Locale))
            {
                return settings.Locale;
            }
        }
        catch
        {
            return "zh-CN";
        }

        return "zh-CN";
    }

    private static void PersistLocale(string locale)
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

        settings.Locale = locale;
        File.WriteAllText(path, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
    }
}
