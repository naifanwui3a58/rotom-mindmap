using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using RotomMindmap.Domain;

namespace RotomMindmap.UI;

public static class MindMapEdgeRouteBuilder
{
    public static Vector2[] BuildRoute(
        Vector2 start,
        MindMapConnectorSide startSide,
        Vector2 end,
        MindMapConnectorSide endSide,
        float zoom)
    {
        var distance = start.DistanceTo(end);
        var startNormal = GetSideNormal(startSide);
        var endNormal = GetSideNormal(endSide);
        var handleDistance = ComputeHandleDistance(start, startSide, end, endSide, zoom, distance);
        var c1 = start + startNormal * handleDistance;
        var c2 = end + endNormal * handleDistance;
        return SampleCubicBezier(start, c1, c2, end, zoom);
    }

    public static MindMapConnectorSide ResolveDirectionalSide(Vector2 direction)
    {
        if (direction == Vector2.Zero)
        {
            return MindMapConnectorSide.Right;
        }

        var normalized = direction.Normalized();
        if (Math.Abs(normalized.X) >= Math.Abs(normalized.Y))
        {
            return normalized.X >= 0f ? MindMapConnectorSide.Right : MindMapConnectorSide.Left;
        }

        return normalized.Y >= 0f ? MindMapConnectorSide.Bottom : MindMapConnectorSide.Top;
    }

    public static Vector2 GetSideNormal(MindMapConnectorSide side)
    {
        return side switch
        {
            MindMapConnectorSide.Top => new Vector2(0f, -1f),
            MindMapConnectorSide.Right => new Vector2(1f, 0f),
            MindMapConnectorSide.Bottom => new Vector2(0f, 1f),
            MindMapConnectorSide.Left => new Vector2(-1f, 0f),
            _ => Vector2.Right
        };
    }

    private static float ComputeHandleDistance(
        Vector2 start,
        MindMapConnectorSide startSide,
        Vector2 end,
        MindMapConnectorSide endSide,
        float zoom,
        float distance)
    {
        var baseDistance = Math.Max(18f * zoom, Math.Min(118f * zoom, distance * 0.34f));
        var delta = end - start;
        var horizontalTravel = Math.Abs(delta.X);
        var verticalTravel = Math.Abs(delta.Y);

        if (startSide is MindMapConnectorSide.Left or MindMapConnectorSide.Right
            && endSide is MindMapConnectorSide.Left or MindMapConnectorSide.Right)
        {
            return Math.Max(24f * zoom, Math.Min(baseDistance * 1.2f, horizontalTravel * 0.38f + 34f * zoom));
        }

        if (startSide is MindMapConnectorSide.Top or MindMapConnectorSide.Bottom
            && endSide is MindMapConnectorSide.Top or MindMapConnectorSide.Bottom)
        {
            return Math.Max(20f * zoom, Math.Min(baseDistance, verticalTravel * 0.4f + 24f * zoom));
        }

        return Math.Max(22f * zoom, Math.Min(baseDistance, (horizontalTravel + verticalTravel) * 0.18f + 28f * zoom));
    }

    private static Vector2[] SampleCubicBezier(Vector2 start, Vector2 c1, Vector2 c2, Vector2 end, float zoom)
    {
        var segments = Math.Max(16, Mathf.RoundToInt(22f * zoom));
        var points = new List<Vector2>(segments + 1);
        for (var i = 0; i <= segments; i++)
        {
            var t = i / (float)segments;
            AddRoutePoint(points, CubicBezier(start, c1, c2, end, t));
        }

        return points.ToArray();
    }

    private static Vector2 CubicBezier(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
    {
        var u = 1f - t;
        var uu = u * u;
        var tt = t * t;
        return uu * u * p0
            + 3f * uu * t * p1
            + 3f * u * tt * p2
            + tt * t * p3;
    }

    private static void AddRoutePoint(ICollection<Vector2> points, Vector2 point)
    {
        if (points.Count == 0)
        {
            points.Add(point);
            return;
        }

        var last = points.Last();
        if (last.DistanceTo(point) < 0.5f)
        {
            return;
        }

        points.Add(point);
    }
}
