using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RotomMindmap.Domain;

namespace RotomMindmap.Services;

public sealed class OutlineDocumentSerializer
{
    private readonly MarkdownStructureParser _markdownStructureParser = new();

    public OutlineDocumentModel Parse(string markdown, string fallbackFileName)
    {
        var model = new OutlineDocumentModel
        {
            Title = MarkdownTitleHelper.ExtractDisplayTitle(markdown, fallbackFileName)
        };

        var nodes = _markdownStructureParser.Parse(markdown);
        var depthLookup = new Dictionary<string, int>(StringComparer.Ordinal);
        var firstNode = nodes.FirstOrDefault();
        var skipFirstTitleNode = firstNode is not null
            && firstNode.Type == "heading"
            && firstNode.Level == 1
            && string.Equals(
                MarkdownTitleHelper.NormalizeDisplayTitle(firstNode.Title),
                MarkdownTitleHelper.NormalizeDisplayTitle(model.Title),
                StringComparison.Ordinal);

        foreach (var node in nodes)
        {
            var parentDepth = string.IsNullOrWhiteSpace(node.ParentId)
                ? -1
                : depthLookup.GetValueOrDefault(node.ParentId, -1);

            var depth = Math.Max(0, parentDepth + 1);

            if (skipFirstTitleNode && ReferenceEquals(node, firstNode))
            {
                depthLookup[node.Id] = -1;
                continue;
            }

            var text = ComposeOutlineText(node);
            model.Items.Add(new OutlineItem
            {
                Id = string.IsNullOrWhiteSpace(node.Id) ? Guid.NewGuid().ToString("N") : node.Id,
                Depth = depth,
                Text = text
            });
            depthLookup[node.Id] = depth;
        }

        if (model.Items.Count == 0)
        {
            model.Items.Add(new OutlineItem
            {
                Id = Guid.NewGuid().ToString("N"),
                Depth = 0,
                Text = string.Empty
            });
        }

        return model;
    }

    public string BuildMarkdown(string title, IReadOnlyList<OutlineItem> items)
    {
        var normalizedTitle = MarkdownTitleHelper.NormalizeDisplayTitle(title);
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            normalizedTitle = "Untitled";
        }

        var builder = new StringBuilder();
        builder.Append("# ");
        builder.AppendLine(normalizedTitle);
        builder.AppendLine();

        foreach (var item in items)
        {
            var safeDepth = Math.Max(0, item.Depth);
            var lines = item.Text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Split('\n')
                .Select(line => line.TrimEnd())
                .ToArray();

            var firstLine = lines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? string.Empty;
            builder.Append(' ', safeDepth * 2);
            builder.Append("- ");
            builder.AppendLine(firstLine);

            foreach (var continuationLine in lines.Skip(1))
            {
                builder.Append(' ', safeDepth * 2 + 2);
                builder.AppendLine(continuationLine);
            }
        }

        return builder.ToString();
    }

    private static string ComposeOutlineText(MarkdownNode node)
    {
        if (string.IsNullOrWhiteSpace(node.Body))
        {
            return node.Title;
        }

        return $"{node.Title}\n{node.Body.Replace("\r", string.Empty, StringComparison.Ordinal)}".TrimEnd();
    }
}
