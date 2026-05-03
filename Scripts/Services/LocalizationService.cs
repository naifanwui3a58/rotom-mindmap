using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

namespace RotomMindmap.Services;

public sealed class LocalizationService
{
    private const string DefaultLocale = "zh-CN";

    private readonly Dictionary<string, Dictionary<string, string>> _tables = new(StringComparer.OrdinalIgnoreCase);
    private string _currentLocale = DefaultLocale;

    public string CurrentLocale => _currentLocale;

    public IReadOnlyList<string> SupportedLocales => ["zh-CN", "en"];

    public void Initialize()
    {
        foreach (var locale in SupportedLocales)
        {
            LoadTable(locale);
        }

        var persistedLocale = LoadPersistedLocale();
        _currentLocale = _tables.ContainsKey(persistedLocale) ? persistedLocale : DefaultLocale;
    }

    public string Get(string key)
    {
        if (_tables.TryGetValue(_currentLocale, out var currentTable)
            && currentTable.TryGetValue(key, out var localizedValue))
        {
            return localizedValue;
        }

        if (_tables.TryGetValue(DefaultLocale, out var defaultTable)
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
        var table = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
            ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _tables[locale] = new Dictionary<string, string>(table, StringComparer.OrdinalIgnoreCase);
    }

    private static string LoadPersistedLocale()
    {
        AppPaths.SeedEditorWorkspaceFromLegacyIfNeeded();
        var path = AppPaths.UiSettingsAbsolutePath;
        if (!File.Exists(path))
        {
            return DefaultLocale;
        }

        try
        {
            var json = File.ReadAllText(path);
            var document = JsonDocument.Parse(json);
            if (document.RootElement.TryGetProperty("locale", out var localeElement))
            {
                return localeElement.GetString() ?? DefaultLocale;
            }
        }
        catch
        {
            return DefaultLocale;
        }

        return DefaultLocale;
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

        var payload = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["locale"] = locale
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(path, payload);
    }
}
