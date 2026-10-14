using Godot;

namespace Sts2Demo.Core;

public static class DemoRewardLayout
{
    public static float Spacing(float width, int count) => Math.Min(350, (width - 200) / Math.Max(1, count));
    public static Vector2 Center(float width, float height, int count, int index) =>
        new(width / 2 + (index - (count - 1) / 2f) * Spacing(width, count), height / 2);
    public static int Hover(Vector2 cursor, float width, float height, int count)
    {
        if (count == 0 || !float.IsFinite(cursor.X) || !float.IsFinite(cursor.Y)) return -1;
        var point = cursor * new Vector2(width, height);
        if (Math.Abs(point.Y - height / 2) > 230) return -1;
        int best = -1; float distance = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            float current = Math.Abs(point.X - Center(width, height, count, i).X);
            if (current < distance && current <= 145) { best = i; distance = current; }
        }
        return best;
    }
    public static int Hover(Vector2 cursor, Vector2 size, IReadOnlyList<Vector2> centers)
    {
        var point = cursor * size;
        int best = -1; float distance = float.MaxValue;
        for (int i = 0; i < centers.Count; i++)
        {
            var center = centers[i] * size;
            float current = Math.Abs(point.X - center.X);
            if (Math.Abs(point.Y - center.Y) <= 230 && current <= 145 && current < distance)
            { best = i; distance = current; }
        }
        return best;
    }
}
