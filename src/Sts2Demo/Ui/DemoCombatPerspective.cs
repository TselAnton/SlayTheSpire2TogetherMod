using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using System.Reflection;

namespace Sts2Demo.Ui;

// A thread-local, synchronous presentation scope. Never changes LocalContext's
// stored identity, never spans an await or changes game/network ownership.
internal sealed class DemoRenderScope : IDisposable
{
    [ThreadStatic] internal static ulong? PlayerId;
    private readonly ulong? _previous = PlayerId;
    public DemoRenderScope(ulong? id) { PlayerId = id; }
    public void Dispose() { PlayerId = _previous; }
}

[HarmonyPatch(typeof(LocalContext), "get_NetId")]
internal static class RenderIdentity
{
    static void Postfix(ref ulong? __result) { if (DemoRenderScope.PlayerId is ulong id) __result = id; }
}

[HarmonyPatch]
internal static class IntentPerspective
{
    static IEnumerable<MethodBase> TargetMethods() => new[] { "UpdateVisuals", "OnHovered" }
        .Select(name => AccessTools.Method(typeof(NIntent), name));
    static void Prefix(out DemoRenderScope __state) => __state = new(DemoController.ObservedPlayerId);
    static void Finalizer(DemoRenderScope? __state) => __state?.Dispose();
}

[HarmonyPatch]
internal static class OrbPerspective
{
    static IEnumerable<MethodBase> TargetMethods() => new[] { "AddSlotAnim", "AddOrbAnim", "EvokeOrbAnim" }
        .Select(name => AccessTools.Method(typeof(NOrbManager), name));
    static void Prefix(out DemoRenderScope __state) => __state = new(DemoController.ObservedPlayerId);
    static void Finalizer(DemoRenderScope? __state) => __state?.Dispose();
}

// These two native effects choose a local target before their asynchronous
// animation starts. Redirect only the visual target, never the attack command.
[HarmonyPatch]
internal static class EnemyVisualTarget
{
    static IEnumerable<MethodBase> TargetMethods() => new[]
    {
        AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.Vfx.NMyteVfx), "SetTarget"),
        AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.Vfx.NSnappingJaxfruitVfx), "SetTarget")
    };
    static void Prefix(ref MegaCrit.Sts2.Core.Entities.Creatures.Creature? target)
    { if (DemoController.IsViewing && DemoTopBar.ObservedPlayer is { } watched) target = watched.Creature; }
}

internal sealed class DemoCombatPerspective
{
    private sealed record Saved(bool Visible, Color Tint, Vector2 Position, bool Remote, bool? LocalOrbs, bool? IntentVisible);
    private readonly Dictionary<NCreature, Saved> _saved = [];
    private readonly Dictionary<CanvasItem, bool> _stolenHidden = [];
    private readonly Dictionary<NCreature, (CardModel Source, NCard View)> _stolen = [];
    private ulong? _target;
    public void Update(Player target)
    {
        if (_target != target.NetId) { Restore(); _target = target.NetId; RefreshIntents(); }
        if (NCombatRoom.Instance is not { } room) return;
        foreach (var node in room.CreatureNodes)
        {
            var owner = node.Entity.Player ?? node.Entity.PetOwner;
            if (owner == null) { UpdateStolenCard(node, target); continue; }
            var display = node.GetNode<NCreatureStateDisplay>("%HealthBar");
            bool selected = owner == target;
            if (!_saved.ContainsKey(node))
            {
                _saved[node] = new(display.Visible, display.Modulate, display.Position,
                    (bool)AccessTools.Field(typeof(NCreature), "_isRemotePlayerOrPet").GetValue(node)!, node.OrbManager?.IsLocal, node.PlayerIntentHandler?.Visible);
                display.HideImmediately(); // Finish the old perspective's visibility tween.
                if (node.OrbManager is { } orbs) RebuildOrbs(node, selected);
            }
            AccessTools.Field(typeof(NCreature), "_isRemotePlayerOrPet").SetValue(node, !selected);
            display.Visible = selected && node.Entity.IsAlive;
            display.Modulate = Colors.White;
            if (selected)
            {
                var hp = DemoUi.Descendants<NHealthBar>(display).FirstOrDefault();
                if (hp != null && AccessTools.Field(typeof(NHealthBar), "_hpForeground").GetValue(hp) is CanvasItem fill)
                    fill.SelfModulate = new Color("38D6C7");
            }
            if (node.OrbManager != null) node.OrbManager.Visible = selected;
            if (node.PlayerIntentHandler != null) node.PlayerIntentHandler.Visible = !selected;
        }
    }

    private void UpdateStolenCard(NCreature node, Player target)
    {
        var source = node.Entity.Powers.OfType<SwipePower>().Select(p => p.StolenCard).FirstOrDefault(c => c?.Owner == target);
        if (node.Entity.Monster is not MegaCrit.Sts2.Core.Models.Monsters.ThievingHopper) return;
        var marker = node.GetSpecialNode<Marker2D>("%StolenCardPos");
        if (marker == null) return;
        foreach (var card in marker.GetChildren().OfType<NCard>())
        {
            if (_stolen.TryGetValue(node, out var own) && own.View == card) continue;
            _stolenHidden.TryAdd(card, card.Visible); card.Hide();
        }
        if (_stolen.TryGetValue(node, out var old))
        {
            if (old.Source == source) return;
            old.View.GetParent()?.RemoveChild(old.View); old.View.QueueFree(); _stolen.Remove(node);
        }
        if (source == null) return;
        var copy = NCard.Create((CardModel)source.ClonePreservingMutability());
        if (copy == null) return;
        marker.AddChild(copy); copy.Position += copy.Size * .5f;
        copy.UpdateVisuals(PileType.Deck, CardPreviewMode.Normal); DemoUi.ReadOnly(copy);
        _stolen[node] = (source, copy);
    }

    private static void RebuildOrbs(NCreature node, bool local)
    {
        if (node.OrbManager is not { } manager || node.Entity.Player?.PlayerCombatState is not { } state) return;
        var list = (List<NOrb>)AccessTools.Field(typeof(NOrbManager), "_orbs").GetValue(manager)!;
        AccessTools.Property(typeof(NOrbManager), nameof(NOrbManager.IsLocal)).SetValue(manager, local);
        // Death/victory clear the visual list before the model drops capacity.
        // Do not cancel that native cleanup or lay out already removed slots.
        if (!MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsInProgress || !node.Entity.IsAlive || list.Count == 0)
        { AccessTools.Method(typeof(NCreature), "SetOrbManagerPosition").Invoke(node, null); return; }
        var parent = manager.GetNode<Control>("%Orbs");
        (AccessTools.Field(typeof(NOrbManager), "_curTween").GetValue(manager) as Tween)?.Kill();
        var poses = list.Select(orb => (orb.Model, orb.Position)).ToArray();
        foreach (var orb in list) { orb.GetParent()?.RemoveChild(orb); orb.QueueFree(); }
        list.Clear();
        for (int i = 0; i < poses.Length; i++)
        {
            var orb = NOrb.Create(local, poses[i].Model);
            parent.AddChild(orb); orb.Position = poses[i].Position; list.Add(orb);
        }
        if (list.Count >= state.OrbQueue.Capacity) AccessTools.Method(typeof(NOrbManager), "TweenLayout").Invoke(manager, null);
        AccessTools.Method(typeof(NOrbManager), "UpdateControllerNavigation").Invoke(manager, null);
        AccessTools.Method(typeof(NCreature), "SetOrbManagerPosition").Invoke(node, null);
    }

    private static void RefreshIntents()
    {
        if (NCombatRoom.Instance is not { } room) return;
        foreach (var intent in DemoUi.Descendants<NIntent>(room))
            AccessTools.Method(typeof(NIntent), "UpdateVisuals").Invoke(intent, null);
    }

    public void Restore()
    {
        foreach (var (node, saved) in _saved)
        {
            if (!GodotObject.IsInstanceValid(node) || !node.IsInsideTree()) continue;
            var display = node.GetNode<NCreatureStateDisplay>("%HealthBar");
            display.HideImmediately(); display.Visible = saved.Visible && node.Entity.IsAlive;
            display.Modulate = new Color(saved.Tint, !saved.Remote && node.Entity.IsAlive ? 1 : 0); display.Position = saved.Position;
            AccessTools.Field(typeof(NCreature), "_isRemotePlayerOrPet").SetValue(node, saved.Remote);
            foreach (var hp in DemoUi.Descendants<NHealthBar>(display)) hp.RefreshValues();
            if (saved.IntentVisible is bool intentVisible && node.PlayerIntentHandler != null) node.PlayerIntentHandler.Visible = intentVisible;
            if (saved.LocalOrbs is bool local && node.OrbManager != null)
            { RebuildOrbs(node, local); node.OrbManager.Visible = true; }
        }
        foreach (var (node, visible) in _stolenHidden) if (GodotObject.IsInstanceValid(node)) node.Visible = visible;
        foreach (var (_, pair) in _stolen) if (GodotObject.IsInstanceValid(pair.View)) { pair.View.GetParent()?.RemoveChild(pair.View); pair.View.QueueFree(); }
        _stolen.Clear(); _stolenHidden.Clear(); _saved.Clear(); _target = null;
        RefreshIntents();
    }
}
