using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Logging;
using Sts2Demo;
using Sts2Demo.Core;
using Sts2Demo.Ui;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Runs;
using Sts2Demo.Networking;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Orbs;

namespace Sts2DemoLab;

public partial class LabOracle
{
    private async Task Audit140Combat()
    {
        var demo = DemoController.Current!;
        demo.ExitDemo();
        if (!demo.EnterDemo(3)) throw new Exception("1.4 Regent entry failed");
        await ToSignal(GetTree().CreateTimer(.35), SceneTreeTimer.SignalName.Timeout);
        foreach (var node in NCombatRoom.Instance!.CreatureNodes.Where(n => n.Entity.Player != null || n.Entity.PetOwner != null))
        {
            var display = node.GetNode<NCreatureStateDisplay>("%HealthBar");
            var owner = node.Entity.Player ?? node.Entity.PetOwner;
            if (display.Visible != (owner!.NetId == 3 && node.Entity.IsAlive)) throw new Exception("1.4 creature HUD ownership mismatch");
        }
        var stars = Descendants<NStarCounter>(demo).Single();
        if (!stars.Visible || !ReferenceEquals(AccessTools.Field(typeof(NStarCounter), "_player").GetValue(stars), Players[2]))
            throw new Exception("1.4 native Regent star counter missing");
        await Capture("140-regent-demo");
        var pile = Descendants<NCombatCardPile>(demo.Ui).First();
        AccessTools.Method(typeof(NCombatCardPile), "OnRelease").Invoke(pile, null);
        await ToSignal(GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout);
        if (!DemoController.IsViewing || !DemoTopBar.UtilityOpen) throw new Exception("1.4 pile inspection lost observer");
        var remoteScreen = new DemoRewardMessage { ProtocolVersion = DemoRewardMessage.Protocol, Sequence = ++_sequence,
            Location = RunManager.Instance.DebugOnlyGetState()!.RunLocation, Kind = DemoRewardKind.None,
            Screen = "NSettingsScreen", Cards = [], Labels = [] };
        Net.Inject(remoteScreen, 3);
        await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
        if (!DemoController.IsViewing || !DemoTopBar.UtilityOpen) throw new Exception("Remote screen change displaced local inspection");
        remoteScreen.Screen = "NCombatRoom"; remoteScreen.Sequence = ++_sequence; Net.Inject(remoteScreen, 3);
        await Capture("140-pile-independent");
        GetViewport().PushInput(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = true }, true);
        GetViewport().PushInput(new InputEventKey { Keycode = Key.Escape, PhysicalKeycode = Key.Escape, Pressed = false }, true);
        await ToSignal(GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout);
        if (!DemoController.IsViewing || DemoTopBar.UtilityOpen) throw new Exception("1.4 Escape did not close only pile inspection");
        await Audit140LocalControls();
        demo.ExitDemo();
        await Capture("140-combat-restored");
        Log.Info("[DemoLab] PASS 1.4.0: watched native creature HUD, Regent stars, independent pile inspection and return.");
        if (!demo.EnterDemo(4)) throw new Exception("Fourth player cannot be observed");
        await OrbCmd.Channel<FrostOrb>(new ThrowingPlayerChoiceContext(), Players[3]);
        await OrbCmd.AddSlots(Players[3], 1);
        await ToSignal(GetTree().CreateTimer(.5), SceneTreeTimer.SignalName.Timeout);
        var orbs = NCombatRoom.Instance!.GetCreatureNode(Players[3].Creature)!.OrbManager!;
        var orbViews = (List<NOrb>)AccessTools.Field(typeof(NOrbManager), "_orbs").GetValue(orbs)!;
        if (!orbs.IsLocal || !orbs.Visible || orbViews.Count != Players[3].PlayerCombatState!.OrbQueue.Capacity
            || orbViews.Count(n => n.Model != null) != Players[3].PlayerCombatState!.OrbQueue.Orbs.Count)
            throw new Exception($"Watched Defect orb state mismatch: local={orbs.IsLocal}, visible={orbs.Visible}, slots={orbViews.Count}/{Players[3].PlayerCombatState!.OrbQueue.Capacity}, orbs={orbViews.Count(n => n.Model != null)}/{Players[3].PlayerCombatState!.OrbQueue.Orbs.Count}");
        await Capture("140-defect-orbs");
        await OrbCmd.EvokeNext(new ThrowingPlayerChoiceContext(), Players[3]);
        OrbCmd.RemoveSlots(Players[3], 1);
        await ToSignal(GetTree().CreateTimer(.5), SceneTreeTimer.SignalName.Timeout);
        if (orbViews.Count != Players[3].PlayerCombatState!.OrbQueue.Capacity) throw new Exception("Orb removal not reflected");
        demo.ExitDemo();
        await OstyCmd.Summon(new ThrowingPlayerChoiceContext(), Players[2], 12, null);
        demo.EnterDemo(3);
        await ToSignal(GetTree().CreateTimer(.5), SceneTreeTimer.SignalName.Timeout);
        var osty = NCombatRoom.Instance!.GetCreatureNode(Players[2].Osty)!;
        if (!osty.GetNode<NCreatureStateDisplay>("%HealthBar").Visible) throw new Exception("Non-Necrobinder Osty missing");
        await Capture("140-regent-osty");
        // Both ends announce observation. Publication must still describe local hand.
        Net.Inject(new DemoPresenceMessage { ProtocolVersion = 1, Sequence = ++_sequence,
            Location = RunManager.Instance.DebugOnlyGetState()!.RunLocation, Watching = true, TargetId = 1 }, 3);
        await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
        var sent = Net.PublishedHands.Last();
        var ownIds = Players[0].PlayerCombatState!.Hand.Cards.Select(c => NetCombatCard.FromModel(c).CombatCardIndex).ToHashSet();
        if (sent.Suspended || sent.Cards == null || sent.Cards.Count != ownIds.Count || sent.Cards.Any(c => !ownIds.Contains(c.CardId)))
            throw new Exception("Cross observation published observer hand or suspended own hand");
        demo.ExitDemo();
        Log.Info("[DemoLab] PASS 1.4.0: fourth-player Defect channel/slots/evoke, non-Necrobinder Osty, cross-observation native hand publication.");
        await Audit140Automation();
    }

    private async Task Audit140LocalControls()
    {
        var demo = DemoController.Current!;
        DemoSettings.FollowCombat = false;
        await ToSignal(GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout);
        var card = Descendants<NCard>(demo).First(n => n.Model?.Owner == Players[2]);
        var point = card.GlobalPosition - new Vector2(0, 20);
        var originalHand = Players[2].PlayerCombatState!.Hand.Cards.ToArray();
        GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
        GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = true }, true);
        GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = false }, true);
        await ToSignal(GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout);
        if (AccessTools.Field(typeof(DemoController), "_localSelected").GetValue(demo) == null
            || !originalHand.SequenceEqual(Players[2].PlayerCombatState!.Hand.Cards)) throw new Exception("Local independent card selection failed or changed hand");
        await Capture("140-independent-hand");
        DemoSettings.FollowCombat = true;
        var holder = Descendants<NRelicInventoryHolder>(DemoTopBar.Current!).First();
        if (holder.Relic.Model.Owner != Players[2]) throw new Exception("Relic tooltip owner mismatch");
        AccessTools.Method(typeof(NRelicInventoryHolder), "OnFocus").Invoke(holder, null);
        await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
        var tips = (Dictionary<Control, NHoverTipSet>)AccessTools.Field(typeof(NHoverTipSet), "_activeHoverTips").GetValue(null)!;
        if (!tips.ContainsKey(holder) || Descendants<NRelicInventoryHolder>(NRun.Instance!.GlobalUi.RelicInventory).Any(h => h.MouseFilter != Control.MouseFilterEnum.Ignore))
            throw new Exception("Watched tooltip missing or native relic input still active");
        await Capture("140-relic-tooltip");
        AccessTools.Method(typeof(NRelicInventoryHolder), "OnUnfocus").Invoke(holder, null);
        await NGame.Instance!.Transition.FadeOut(.25f);
        await Capture("140-transition-black");
        if (NGame.Instance.Transition.ZIndex <= DemoTopBar.Current!.ZIndex) throw new Exception("Transition does not cover observer chrome");
        await NGame.Instance.Transition.FadeIn(.25f);
        Log.Info("[DemoLab] PASS 1.4.0: Escape closes only inspection; independent mouse card selection preserves hand; watched native relic tooltip; transition covers chrome.");
    }

    private async Task Audit140AvatarExit()
    {
        var row = Descendants<NMultiplayerPlayerState>(NRun.Instance!.GlobalUi.MultiplayerPlayerContainer).Single(r => r.Player.NetId == 2);
        var point = row.GetNode<TextureRect>("%CharacterIcon").GetGlobalRect().GetCenter();
        GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
        GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = true }, true);
        GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = false }, true);
        await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
        if (RewardDemoController.IsViewing || DemoController.IsViewing) throw new Exception("Avatar click could not exit card-selection observation");
        await Capture("140-avatar-selection-exit");
        Log.Info("[DemoLab] PASS 1.4.0: actual avatar mouse press/release exits DEMO while native card selector owns modal input.");
    }

    private async Task Audit140Automation()
    {
        var manager = CombatManager.Instance;
        var ready = (HashSet<Player>)AccessTools.Field(typeof(CombatManager), "_playersReadyToEndTurn").GetValue(manager)!;
        var saved = ready.ToArray();
        DemoSettings.AutoObserve = true; DemoSettings.AutoExitTurn = true;
        try
        {
            ready.Clear();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ready.Add(Players[0]);
            await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
            if (DemoController.ObservedPlayerId != 2) throw new Exception("Auto start failed");
            var selection = new DemoRewardMessage { ProtocolVersion = DemoRewardMessage.Protocol, Sequence = ++_sequence,
                Location = RunManager.Instance.DebugOnlyGetState()!.RunLocation, Kind = DemoRewardKind.Activity,
                Screen = "NSimpleCardSelectScreen", Cards = [], Labels = [] };
            Net.Inject(selection, 2);
            await ToSignal(GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout);
            if (RewardDemoController.ObservedPlayerId != 2 || DemoController.IsViewing) throw new Exception("Automatic selection handoff failed");
            selection.Kind = DemoRewardKind.None; selection.Screen = "NCombatRoom"; selection.Sequence = ++_sequence; Net.Inject(selection, 2);
            await ToSignal(GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout);
            if (DemoController.ObservedPlayerId != 2) throw new Exception("Automatic handoff return failed");
            selection.Kind = DemoRewardKind.Activity; selection.Screen = "NSimpleCardSelectScreen"; selection.Sequence = ++_sequence; Net.Inject(selection, 2);
            await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
            ready.Add(Players[1]);
            await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
            if (DemoController.ObservedPlayerId != 3) throw new Exception("Auto second friend failed");
            ready.Add(Players[2]);
            await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
            if (DemoController.ObservedPlayerId != 4) throw new Exception("Auto fourth player failed");
            await Capture("140-auto-fourth-player");
            ready.Add(Players[3]);
            await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
            if (DemoController.IsViewing) throw new Exception("All-ready did not exit");
            Log.Info("[DemoLab] PASS 1.4.0: four-player automatic 2 -> 3 -> 4 -> exit, including activity handoff and return (native readiness fixture).");
        }
        finally
        {
            ready.Clear(); foreach (var p in saved) ready.Add(p);
            DemoSettings.AutoObserve = false; DemoSettings.AutoExitTurn = false;
            Net.Inject(new DemoRewardMessage { ProtocolVersion = DemoRewardMessage.Protocol, Sequence = ++_sequence,
                Location = RunManager.Instance.DebugOnlyGetState()!.RunLocation, Kind = DemoRewardKind.None,
                Screen = "NCombatRoom", Cards = [], Labels = [] }, 2);
            DemoController.Current!.ExitDemo();
            RewardDemoController.Current!.Exit();
        }
    }
}
