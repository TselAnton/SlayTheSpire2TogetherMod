using Godot;
using Sts2Demo.Networking;

namespace Sts2Demo.Core;

/// <summary>Local static native-art curve: no networked segment animations or elastic head tween.</summary>
public static class DemoArrowLayout
{
    public static List<DemoCardPose> Build(Vector2 from, Vector2 to, Vector2 reference)
    {
        var control = new Vector2(from.X - (to.X - from.X) * .25f,
            from.Y > reference.Y * .5f ? to.Y + (to.Y - from.Y) * .5f : to.Y * .75f + from.Y * .25f);
        float angle = (to - control).Angle() + Mathf.Pi / 2;
        // Native curvature is based on the offset head centre, not the target.
        // Resolve its settled pose locally instead of replaying frame-by-frame rotation/tweens.
        for (int iteration = 0; iteration < 16; iteration++)
        {
            var head = to + new Vector2(0, 88).Rotated(angle);
            control = new(from.X - (head.X - from.X) * .25f,
                from.Y > reference.Y * .5f ? head.Y + (head.Y - from.Y) * .5f : head.Y * .75f + from.Y * .25f);
            angle = (to - control).Angle() + Mathf.Pi / 2;
        }
        var end = to + new Vector2(0, 40).Rotated(angle);
        var poses = new List<DemoCardPose>();
        for (int i = 0; i < 19; i++)
        {
            float t = i / 20f;
            var point = from * ((1 - t) * (1 - t)) + control * (2 * (1 - t) * t) + end * (t * t);
            poses.Add(new DemoCardPose { CardId = (uint)i, Position = point / reference,
                Scale = Vector2.One * Mathf.Lerp(.28f, .42f, i * 2f / 19),
                Rotation = i == 0 ? 0 : (point - poses[i - 1].Position * reference).Angle() + Mathf.Pi / 2 });
        }
        var first = poses[0]; first.Rotation = (poses[0].Position - poses[1].Position).Angle() - Mathf.Pi / 2; poses[0] = first;
        poses.Add(new DemoCardPose { CardId = 19, Position = (to + new Vector2(0, 88).Rotated(angle)) / reference,
            Scale = Vector2.One * .95f, Rotation = angle });
        return poses;
    }
}
