using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using Sts2Demo;
using Sts2Demo.Core;
using Sts2Demo.Networking;
using Sts2Demo.Ui;

namespace Sts2DemoLab;

public partial class LabOracle
{
    private async Task AuditPotionsAndRestore()
    {
        try
        {
            var controller = DemoController.Current!;
            var room = NCombatRoom.Instance!;
            var manager = NTargetManager.Instance;
            var location = RunManager.Instance.DebugOnlyGetState()!.RunLocation;
            var enemy = room.CreatureNodes.First(n => n.Entity.IsEnemy);
            var ally = room.CreatureNodes.First(n => n.Entity.Player?.NetId == 3);
            var origin = NRun.Instance!.GlobalUi.TopBar.PotionContainer.GlobalPosition + new Vector2(70, 50);
            async Task Target(string name, TargetType type, Node node)
            {
                controller.ExitDemo();
                await ToSignal(GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout);
                manager.StartTargeting(type, origin, TargetMode.Controller, () => false, null);
                manager.OnNodeHovered(node);
                await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
                var state = DemoTargeting.Capture(controller);
                if (!state.Visible || state.TargetKind == 0) throw new Exception("Native target was not captured: " + name);
                var resolved = DemoTargeting.Resolve(state, controller);
                if (resolved.DistanceTo(state.To * controller.Size) > 1) throw new Exception("Arrow endpoint differs from native: " + name);
                await Capture("potion-" + name + "-source");
                manager.CancelTargeting();
                controller.EnterDemo(2);
                var poses = Players[1].PlayerCombatState!.Hand.Cards.Select((c, i) => new DemoCardPose {
                    CardId = MegaCrit.Sts2.Core.Entities.Multiplayer.NetCombatCard.FromModel(c).CombatCardIndex,
                    Position = new(.35f + i * .07f, .85f), Scale = new(.65f, .65f) }).ToList();
                // Keep the actual production wire frame fresh while the screenshot waits.
                var message = new DemoHandMessage { ProtocolVersion = DemoHandMessage.Protocol, Location = location,
                    Round = CombatManager.Instance.DebugOnlyGetState()!.RoundNumber, ReferenceSize = controller.Size, Cards = poses,
                    ArrowVisible = true, ArrowFrom = state.From, ArrowTo = state.To, ArrowTintRG = state.TintRG, ArrowTintBA = state.TintBA, Targeting = state };
                for (int i = 0; i < 8; i++)
                {
                    message.Sequence = ++_sequence; Net.Inject(message, 2);
                    await ToSignal(GetTree().CreateTimer(.1), SceneTreeTimer.SignalName.Timeout);
                }
                string path = Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath())!, "../validation-1.3.0/lab-potion-" + name + ".png");
                GetViewport().GetTexture().GetImage().SavePng(path);
                var arrow = Descendants<Node2D>(controller).Single(n => n.Name == "DemoTargetingArrow");
                if (!arrow.Visible) throw new Exception("Remote potion arrow missing: " + name);
                Log.Info("[DemoLab] PASS: potion " + name + " native endpoint, identity and remote arrow.");
                controller.ExitDemo();
                await ToSignal(GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout);
            }
            await Target("enemy", TargetType.AnyEnemy, enemy);
            await Target("ally", TargetType.AnyPlayer, ally);
            var row = Descendants<NMultiplayerPlayerState>(NRun.Instance.GlobalUi.MultiplayerPlayerContainer).Single(r => r.Player.NetId == 3);
            await Target("player-row", TargetType.AnyPlayer, row);
            controller.EnterDemo(2);
            await Capture("potion-before-use");
            var observed = Players[1].PotionSlots.Single(p => p != null)!;
            // Native use action, not a direct inventory edit: verifies ordinary synced gameplay updates the mirror.
            observed.EnqueueManualUse(enemy.Entity);
            await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
            if (Players[1].PotionSlots.Contains(observed)) throw new Exception("Native potion use did not remove observed slot");
            if (Descendants<NPotion>(DemoTopBar.Current!).Any(p => p.Model.Id == observed.Id)) throw new Exception("Consumed potion remains in DEMO header");
            await Capture("potion-after-use");
            Log.Info("[DemoLab] PASS: native observed potion use removes the corresponding DEMO slot.");
            controller.ExitDemo();
            // Repeated rapid switches must not leave a tween that later hides the local hand.
            for (int i = 0; i < 8; i++) { controller.EnterDemo((ulong)(2 + i % 2)); controller.ExitDemo(); }
            await ToSignal(GetTree().CreateTimer(.5), SceneTreeTimer.SignalName.Timeout);
            if (!room.Ui.Hand.Visible || room.Ui.Hand.Modulate.A < .99f || manager.IsInSelection)
                throw new Exception("Exit left the local hand hidden or targeting locked");
            if (Descendants<Node2D>(controller).Any(n => n.Name == "DemoTargetingArrow" && n.IsVisibleInTree()))
                throw new Exception("Exit left a remote target arrow visible");
            DemoSettings.SeparateButton = false;
            var friend = Descendants<NMultiplayerPlayerState>(NRun.Instance.GlobalUi.MultiplayerPlayerContainer).Single(r => r.Player.NetId == 2);
            AccessTools.Method(typeof(NMultiplayerPlayerState), "OnRelease").Invoke(friend, [friend.Hitbox]);
            if (DemoController.ObservedPlayerId != 2) throw new Exception("Default player-row click did not enter DEMO");
            await Capture("entry-player-row");
            controller.ExitDemo(); DemoSettings.SeparateButton = true;
            await Capture("exit-native-hand");
            if (!(bool)AccessTools.Method(typeof(NPlayerHand), "CanPlayCards").Invoke(room.Ui.Hand, null)!)
                throw new Exception("Native hand still rejects local card actions after exit");
            Log.Info("[DemoLab] PASS: default row entry and repeated exits restore local hand opacity, geometry and targeting.");
            // Restore a different lab potion for the later header/gallery tests.
            Players[1].AddPotionInternal(ModelDb.Potion<WeakPotion>().ToMutable(), 1);
        }
        catch (Exception e) { Log.Error("[DemoLab] POTION/RESTORE FAILED: " + e); }
        finally { NTargetManager.Instance.CancelTargeting(); DemoController.Current?.ExitDemo(); DemoSettings.SeparateButton = true; }
    }
}
