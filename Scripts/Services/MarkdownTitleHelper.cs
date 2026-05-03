using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RotomMindmap.Services;

public static class MarkdownTitleHelper
{
    private static readonly Regex HeadingRegex = new(@"^#{1,6}\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex ListRegex = new(@"^(\s*)([-*+]|\d+\.)\s+(.+)$", RegexOptions.Compiled);

    public static string ExtractDisplayTitle(string markdown, string fallbackFileName)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var headingMatch = HeadingRegex.Match(line);
            if (headingMatch.Success)
            {
                var headingTitle = NormalizeDisplayTitle(headingMatch.Groups[1].Value);
                if (!string.IsNullOrWhiteSpace(headingTitle))
                {
                    return headingTitle;
                }
            }

            var listMatch = ListRegex.Match(line);
            if (listMatch.Success)
            {
                var listTitle = NormalizeDisplayTitle(listMatch.Groups[3].Value);
                if (!string.IsNullOrWhiteSpace(listTitle))
                {
                    return listTitle;
                }
            }

            var plainTitle = NormalizeDisplayTitle(line.TrimStart('#').Trim());
            if (!string.IsNullOrWhiteSpace(plainTitle))
            {
                return plainTitle;
            }
        }

        var fallback = NormalizeDisplayTitle(Path.GetFileNameWithoutExtension(fallbackFileName));
        return string.IsNullOrWhiteSpace(fallback) ? "Untitled" : fallback;
    }

    public static string CreateInitialContent(string suggestedTitle)
    {
        var title = string.IsNullOrWhiteSpace(suggestedTitle) ? "Untitled" : suggestedTitle.Trim();
        return $"# {title}\n\n";
    }

    public static string Slugify(string rawName)
    {
        var normalized = NormalizeDisplayTitle(rawName).ToLowerInvariant();
        var invalid = Path.GetInvalidFileNameChars();
        var chars = normalized.Select(ch =>
        {
            if (invalid.Contains(ch) || char.IsControl(ch))
            {
                return '-';
            }

            return char.IsWhiteSpace(ch) ? '-' : ch;
        }).ToArray();

        var slug = new string(chars);
        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        slug = slug.Trim('-', '.', ' ');
        return string.IsNullOrWhiteSpace(slug) ? "untitled" : slug;
    }

    public static string NormalizeDisplayTitle(string title)
    {
        var compact = Regex.Replace(title, @"\s+", " ").Trim();
        if (compact.Length <= 80)
        {
            return compact;
        }

        return compact[..80].TrimEnd();
    }
}
