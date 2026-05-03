using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RotomMindmap.Domain;

namespace RotomMindmap.Services;

public sealed class MarkdownStructureParser
{
    private static readonly Regex HeadingRegex = new(@"^(#{1,6})\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex ListRegex = new(@"^(\s*)([-*+]|\d+\.)\s+(.+)$", RegexOptions.Compiled);
    private static readonly Regex NodeIdRegex = new(@"\s*<!--\s*rotom-id:(?<id>[a-zA-Z0-9_-]+)\s*-->\s*$", RegexOptions.Compiled);

    public IReadOnlyList<MarkdownNode> Parse(string markdown)
    {
        var nodes = new List<MarkdownNode>();
        var nodeLookup = new Dictionary<string, MarkdownNode>();
        var headingStack = new Stack<MarkdownNode>();
        var listStack = new Stack<(int Indent, MarkdownNode Node)>();
        MarkdownNode? lastNode = null;
        var order = 0;

        foreach (var rawLine in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var headingMatch = HeadingRegex.Match(line.TrimStart());
            if (headingMatch.Success)
            {
                var headingLevel = headingMatch.Groups[1].Value.Length;
                var (title, explicitId) = ExtractNodeIdentity(headingMatch.Groups[2].Value);
                while (headingStack.Count > 0 && headingStack.Peek().Level >= headingLevel)
                {
                    headingStack.Pop();
                }

                listStack.Clear();
                var parent = headingStack.Count > 0 ? headingStack.Peek() : null;
                var node = CreateNode(++order, title, "heading", parent, explicitId);
                node.Level = headingLevel;
                nodes.Add(node);
                nodeLookup[node.Id] = node;
                headingStack.Push(node);
                lastNode = node;
                continue;
            }

            var listMatch = ListRegex.Match(line);
            if (listMatch.Success)
            {
                var indent = listMatch.Groups[1].Value.Length / 2;
                var (title, explicitId) = ExtractNodeIdentity(listMatch.Groups[3].Value);
                while (listStack.Count > 0 && listStack.Peek().Indent >= indent)
                {
                    listStack.Pop();
                }

                MarkdownNode? parent = null;
                if (listStack.Count > 0)
                {
                    parent = listStack.Peek().Node;
                }
                else if (headingStack.Count > 0)
                {
                    parent = headingStack.Peek();
                }

                var node = CreateNode(++order, title, "list", parent, explicitId);
                node.Level = parent is null ? 1 : parent.Level + 1;
                nodes.Add(node);
                nodeLookup[node.Id] = node;
                listStack.Push((indent, node));
                lastNode = node;
                continue;
            }

            if (lastNode is not null)
            {
                var cleanedLine = StripNodeIdComment(line.Trim());
                lastNode.Body = string.IsNullOrWhiteSpace(lastNode.Body)
                    ? cleanedLine
                    : $"{lastNode.Body}\n{cleanedLine}";
            }
        }

        foreach (var node in nodes)
        {
            node.Path = BuildPath(node, nodeLookup);
        }

        return nodes;
    }

    public string BuildCsv(string markdown)
    {
        var nodes = Parse(markdown);
        var builder = new StringBuilder();
        builder.AppendLine("id,parent_id,level,type,title,body,order,path");
        foreach (var node in nodes)
        {
            builder.AppendLine(string.Join(",",
                Escape(node.Id),
                Escape(node.ParentId),
                node.Level.ToString(),
                Escape(node.Type),
                Escape(node.Title),
                Escape(node.Body),
                node.Order.ToString(),
                Escape(node.Path)));
        }

        return builder.ToString();
    }

    private static MarkdownNode CreateNode(int order, string title, string type, MarkdownNode? parent, string explicitId)
    {
        return new MarkdownNode
        {
            Id = string.IsNullOrWhiteSpace(explicitId) ? $"n{order}" : explicitId,
            ParentId = parent?.Id ?? string.Empty,
            Order = order,
            Title = string.IsNullOrWhiteSpace(title) ? $"Node {order}" : title,
            Type = type,
            Level = parent is null ? 1 : parent.Level + 1
        };
    }

    private static (string Title, string ExplicitId) ExtractNodeIdentity(string rawText)
    {
        var match = NodeIdRegex.Match(rawText);
        var cleanedText = match.Success
            ? rawText[..match.Index]
            : rawText;
        var title = MarkdownTitleHelper.NormalizeDisplayTitle(cleanedText);
        var explicitId = match.Success ? match.Groups["id"].Value.Trim() : string.Empty;
        return (title, explicitId);
    }

    private static string StripNodeIdComment(string rawText)
    {
        return NodeIdRegex.Replace(rawText, string.Empty).TrimEnd();
    }

    private static string BuildPath(MarkdownNode node, IReadOnlyDictionary<string, MarkdownNode> lookup)
    {
        var titles = new List<string> { node.Title };
        var parentId = node.ParentId;
        while (!string.IsNullOrWhiteSpace(parentId) && lookup.TryGetValue(parentId, out var parent))
        {
            titles.Add(parent.Title);
            parentId = parent.ParentId;
        }

        titles.Reverse();
        return string.Join("/", titles);
    }

    private static string Escape(string value)
    {
        var safe = value.Replace("\"", "\"\"", StringComparison.Ordinal);
        return $"\"{safe}\"";
    }
}
