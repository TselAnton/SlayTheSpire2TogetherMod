using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;
using Sts2Demo;
using Sts2Demo.Core;
using Sts2Demo.Networking;
using Sts2Demo.Ui;

namespace Sts2DemoLab;

public partial class LabOracle
{
    private async Task AuditPatchCombat()
    {
        var controller = DemoController.Current!;
        controller.ExitDemo();
        var state = Players[1].PlayerCombatState!;
        var card = state.DrawPile.Cards.First();
        state.DrawPile.RemoveInternal(card); state.ExhaustPile.AddInternal(card); state.ExhaustPile.InvokeCardAddFinished();
        try
        {
            void CheckPiles(int player)
            {
                foreach (var button in Descendants<NCombatCardPile>(controller.Ui))
                {
                    var type = (PileType)AccessTools.Property(button.GetType(), "Pile").GetValue(button)!;
                    var expected = CardPile.Get(type, Players[player])!;
                    if (!ReferenceEquals(AccessTools.Field(typeof(NCombatCardPile), "_pile").GetValue(button), expected)
                        || button.GetNode<Label>("CountContainer/Count").Text != expected.Cards.Count.ToString())
                        throw new Exception("Pile owner/count mismatch: " + type);
                }
            }
            await Capture("patch-piles-source");
            if (!controller.EnterDemo(2)) throw new Exception("Combat fixture entry failed");
            CheckPiles(1);
            state.ExhaustPile.RemoveInternal(card); state.DiscardPile.AddInternal(card); state.DiscardPile.InvokeCardAddFinished();
            CheckPiles(1);
            await Capture("patch-piles");
            // Native arrow alpha tweens must never reveal a black duplicate line.
            var indicator = Descendants<NRemoteTargetingIndicator>(NGame.Instance!).First(n =>
                ((MegaCrit.Sts2.Core.Entities.Players.Player)AccessTools.Field(typeof(NRemoteTargetingIndicator), "_player").GetValue(n)!).NetId == 2);
            indicator.StartDrawingFrom(new(300, 600)); indicator.UpdateDrawingTo(new(1100, 400));
            indicator.Modulate = Colors.White;
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (Descendants<Line2D>(indicator).Any(line => line.SelfModulate.A != 0)) throw new Exception("Native duplicate arrow visible");
            controller.EnterDemo(3); CheckPiles(2);
            controller.ExitDemo(); CheckPiles(0);
            if (Descendants<Line2D>(indicator).Any(line => line.SelfModulate.A != 1)) throw new Exception("Native remote arrow not restored");
            indicator.StopDrawing();
            Log.Info("[DemoLab] PASS 1.3.1: draw/discard/exhaust native bindings, live count changes, peer switch, local restore, arrow alpha isolation.");
        }
        finally
        {
            controller.ExitDemo();
            if (state.ExhaustPile.Cards.Contains(card)) state.ExhaustPile.RemoveInternal(card);
            if (state.DiscardPile.Cards.Contains(card)) state.DiscardPile.RemoveInternal(card);
            state.DrawPile.AddInternal(card, 0); state.DrawPile.InvokeCardAddFinished();
        }
    }

    private async Task AuditCompletedRewards()
    {
        var screen = ActiveScreenContext.Instance.GetCurrentScreen() as NRewardsScreen
            ?? throw new Exception("Expected native rewards for completion fixture");
        var complete = AccessTools.Property(typeof(NRewardsScreen), "IsComplete");
        bool all = DemoSettings.AllActivities;
        try
        {
            // Isolate completion presentation from reward RNG. The actual native
            // reward acquisition remains covered by the existing reward scenarios.
            complete.SetValue(screen, true); DemoSettings.AllActivities = false;
            var published = await CapturePublishedFrame();
            var frame = Net.PublishedRewards.Last(m => m.Kind == DemoRewardKind.Waiting);
            frame.Sequence = ++_sequence; frame.Delta = false;
            Net.Inject(frame, 2);
            if (!RewardDemoController.Current!.Enter(2)) throw new Exception("Waiting observation rejected in battle-only mode");
            InjectNativeMetadata(published.Data);
            await Capture("patch-rewards-complete");
            if (Descendants<DemoRewardsWindow>(RewardDemoController.Current).Any()) throw new Exception("Empty rewards panel retained");
            if (!Descendants<DemoVisualProjection>(RewardDemoController.Current).Any(p => p.VisualCount > 0)) throw new Exception("Waiting battlefield missing");
            DemoSettings.AllActivities = true;
            frame.Kind = DemoRewardKind.Map; frame.Screen = "NMapScreen"; frame.Sequence = ++_sequence;
            Net.Inject(frame, 2);
            await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
            var row = Descendants<NMultiplayerPlayerState>(NRun.Instance!.GlobalUi.MultiplayerPlayerContainer).Single(r => r.Player.NetId == 2);
            // SeparateButton is deliberately true: an active observation still owns exit.
            AccessTools.Method(typeof(NMultiplayerPlayerState), "OnRelease").Invoke(row, [row.Hitbox]);
            if (RewardDemoController.IsViewing || NRun.Instance.GlobalUi.MapScreen.IsOpen
                || NRun.Instance.GlobalUi.CapstoneContainer.CurrentCapstoneScreen != null)
                throw new Exception("Map avatar exit fell through to native inspector");
            await Capture("patch-map-avatar-exit");
            Log.Info("[DemoLab] PASS 1.3.1: completed rewards become battlefield in battle-only mode; avatar exits map and restores local screen.");
        }
        finally { RewardDemoController.Current!.Exit(); complete.SetValue(screen, false); DemoSettings.AllActivities = all; }
    }

    private async Task AuditLocalHover(string name, DemoVisualUpdate state)
    {
        var candidates = state.Items.Where(v => v.Hover.Length == 9 && v.Hover[8] is 1 or 2).ToList();
        if (candidates.Count == 0) throw new Exception("Missing native hover groups: " + name);
        var projection = Descendants<DemoVisualProjection>(RewardDemoController.Current!).Single();
        var leafField = AccessTools.Field(typeof(DemoVisualProjection), "_leaves");
        var nodes = (Dictionary<string, Control>)leafField.GetValue(projection)!;
        var visual = candidates.First(); var node = nodes[visual.Id]; var instance = node.GetInstanceId();
        var h = visual.Hover;
        var frame = Net.PublishedRewards.Last(m => m.Screen == state.Screen);
        frame.Delta = false; frame.Cards = []; frame.Labels = []; frame.Kind = DemoRewardKind.Activity;
        frame.Chosen = -1; frame.Hovered = -1;
        frame.Cursor = new(-1, -1); frame.Sequence = ++_sequence; Net.Inject(frame, 2);
        await ToSignal(GetTree().CreateTimer(.4), SceneTreeTimer.SignalName.Timeout);
        var small = node.Scale;
        frame.Cursor = new Vector2(h[2] + h[4] / 2, h[3] + h[5] / 2) / new Vector2(state.Reference[0], state.Reference[1]);
        frame.Sequence = ++_sequence; Net.Inject(frame, 2);
        // No screen update while this tween advances. Frames prove local motion.
        await ToSignal(GetTree().CreateTimer(.04), SceneTreeTimer.SignalName.Timeout);
        float early = node.Scale.X;
        await ToSignal(GetTree().CreateTimer(.16), SceneTreeTimer.SignalName.Timeout);
        float late = node.Scale.X;
        if (!(late > early && early > small.X)) throw new Exception($"Hover did not animate locally: {name} {small.X}/{early}/{late}");
        await Capture("patch-hover-" + name);
        // A full recovery snapshot must not restart unchanged native effects.
        InjectNativeMetadata(DemoVisualCodec.Encode(state));
        if (nodes[visual.Id].GetInstanceId() != instance) throw new Exception("Full snapshot recreated stable visual");
        frame.Cursor = new(-1, -1); frame.Sequence = ++_sequence; Net.Inject(frame, 2);
        await ToSignal(GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout);
        if (node.Scale.X >= late) throw new Exception("Hover exit did not animate locally");
        Log.Info($"[DemoLab] PASS 1.3.1: {name} hover advances without screen packets, returns locally, full refresh preserves node identity.");
    }
}
