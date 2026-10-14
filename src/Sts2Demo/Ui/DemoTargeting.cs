using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using Sts2Demo.Core;
using Sts2Demo.Networking;

namespace Sts2Demo.Ui;

public partial class DemoTargeting : Node2D
{
    private readonly List<Sprite2D> _parts = [];
    public override void _Ready()
    {
        ZIndex = 150;
        for (int i = 0; i < 20; i++)
        {
            var sprite = new Sprite2D { Texture = PreloadManager.Cache.GetTexture2D(i == 19
                ? "res://images/ui/combat/targeting_arrow_head.png" : "res://images/ui/combat/targeting_arrow_segment.png") };
            AddChild(sprite); _parts.Add(sprite);
        }
    }
    public static DemoTargetingState Capture(Control reference)
    {
        var native = DemoUi.Descendants<NTargetingArrow>(NGame.Instance!).FirstOrDefault(a => a.IsVisibleInTree());
        if (native == null || reference.Size.X <= 0 || reference.Size.Y <= 0) return default;
        var inverse = reference.GetGlobalTransformWithCanvas().AffineInverse();
        var from = (Vector2)AccessTools.Property(typeof(NTargetingArrow), "From").GetValue(native)!;
        var to = (Vector2)AccessTools.Field(typeof(NTargetingArrow), "_toPosition").GetValue(native)!;
        var result = new DemoTargetingState { Visible = true, From = (inverse * from / reference.Size).Clamp(-1, 2),
            To = (inverse * to / reference.Size).Clamp(-1, 2), TintRG = new(native.Modulate.R, native.Modulate.G), TintBA = new(native.Modulate.B, native.Modulate.A) };
        var hovered = AccessTools.Property(typeof(NTargetManager), "HoveredNode").GetValue(NTargetManager.Instance) as Node;
        Vector2? anchor = null;
        if (hovered is NCreature creature && creature.Entity.CombatId is uint combatId)
        { result.TargetKind = 2; result.TargetId = combatId; anchor = creature.VfxSpawnPosition; }
        else if (hovered is NMultiplayerPlayerState player)
        { result.TargetKind = 1; result.TargetId = player.Player.NetId; anchor = RowAnchor(player); }
        if (anchor is Vector2 point) result.Offset = (inverse.BasisXform(to - point) / reference.Size).Clamp(-2, 2);
        return result;
    }
    private static Vector2 RowAnchor(NMultiplayerPlayerState row) => row.GlobalPosition + new Vector2(row.Hitbox.Size.X, row.Hitbox.Size.Y / 2);
    public static Vector2 Resolve(DemoTargetingState state, Control reference)
    {
        Vector2? anchor = state.TargetKind switch
        {
            2 => NCombatRoom.Instance?.CreatureNodes.FirstOrDefault(n => n.Entity.CombatId == state.TargetId)?.VfxSpawnPosition,
            1 => DemoUi.Descendants<NMultiplayerPlayerState>(NRun.Instance!.GlobalUi.MultiplayerPlayerContainer)
                .Where(n => n.Player.NetId == state.TargetId).Select(n => (Vector2?)RowAnchor(n)).FirstOrDefault(),
            _ => null,
        };
        return anchor is Vector2 point ? reference.GetGlobalTransformWithCanvas().AffineInverse() * point + state.Offset * reference.Size : state.To * reference.Size;
    }
    public void Render(DemoTargetingState state, Control reference)
    {
        Visible = state.Visible && state.Valid;
        if (!Visible) return;
        Modulate = new(state.TintRG.X, state.TintRG.Y, state.TintBA.X, state.TintBA.Y);
        var poses = DemoArrowLayout.Build(state.From * reference.Size, Resolve(state, reference), reference.Size);
        for (int i = 0; i < _parts.Count; i++)
        { _parts[i].Position = poses[i].Position * reference.Size; _parts[i].Scale = poses[i].Scale; _parts[i].Rotation = poses[i].Rotation; }
    }
}
