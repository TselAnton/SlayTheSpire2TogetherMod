using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using Sts2Demo;
using Sts2Demo.Core;
using Sts2Demo.Networking;

namespace Sts2DemoLab;

public partial class LabOracle
{
    private async Task StressProjection()
    {
        await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
        await FinishForRewards();
        DemoSettings.AllActivities = true;
        for (int cycle = 0; cycle < 10; cycle++)
        {
            // Debug entry does not advance the map, so unlike a real map transition
            // it never resets this 4-bit room counter. Keep synthetic locations
            // representable by the game's own wire format during repeated cycles.
            AccessTools.Property(typeof(RunState), nameof(RunState.NextRoomId))
                .SetValue(RunManager.Instance.DebugOnlyGetState(), 0);
            foreach (var model in new MegaCrit.Sts2.Core.Models.EventModel[] {
                MegaCrit.Sts2.Core.Models.ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.FakeMerchant>(),
                MegaCrit.Sts2.Core.Models.ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.CrystalSphere>() })
            {
                await RunManager.Instance.EnterRoomDebug(MegaCrit.Sts2.Core.Rooms.RoomType.Event, model: model, showTransition: false);
                var frame = await CapturePublishedFrame();
                InjectActivity();
                if (!RewardDemoController.Current!.Enter(2)) throw new Exception("Stress observation entry refused");
                InjectNativeMetadata(frame.Data, frame.Origin, frame.Size);
                await ToSignal(GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout);
                RewardDemoController.Current.Exit();
                GC.Collect();
                await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
            }
            Log.Info($"[DemoLab] STRESS PASS cycle {cycle + 1}/10: two scene replacements, projection and forced GC.");
        }
        Log.Info("[DemoLab] STRESS FINISHED.");
    }
    private async Task AuditShopUpdates()
    {
        var controller = RewardDemoController.Current!;
        var inventory = NRun.Instance!.MerchantRoom!.Inventory.Inventory!;
        var entry = inventory.CharacterCardEntries.First();
        await CapturePublishedFrame();
        Net.PublishedScreens.Clear();
        var cost = AccessTools.Field(typeof(MegaCrit.Sts2.Core.Entities.Merchant.MerchantEntry), "_cost");
        cost.SetValue(entry, (int)cost.GetValue(entry)! + 7);
        entry.OnMerchantInventoryUpdated();
        AccessTools.Method(typeof(RewardDemoController), "CaptureNativeScreen").Invoke(controller, null);
        if (Net.PublishedScreens.Count == 0) throw new Exception("Shop price change was not published");
        var delta = DemoVisualCodec.Decode(Net.PublishedScreens.OrderBy(m => m.Index).SelectMany(m => m.Data!).ToArray());
        if (delta.Full || delta.Items.Count == 0) throw new Exception("Shop change did not use an incremental update");
        int count = Net.PublishedScreens.Count;
        AccessTools.Method(typeof(RewardDemoController), "CaptureNativeScreen").Invoke(controller, null);
        if (Net.PublishedScreens.Count != count) throw new Exception("Unchanged shop state was retransmitted");
        Log.Info("[DemoLab] PASS: changed shop price sends delta; unchanged immediate capture sends zero packets.");
        await AuditActivity("merchant-price-updated");
        Players[0].Gold = 1000;
        var purchased = entry.CreationResult!.Card;
        if (!await entry.OnTryPurchaseWrapper(inventory) || entry.IsStocked || !Players[0].Deck.Cards.Contains(purchased))
            throw new Exception("Native shop purchase fixture failed");
        await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
        await AuditActivity("merchant-purchased");
        Log.Info("[DemoLab] PASS: native purchased shop card leaves stock and appears in buyer deck before DEMO projection.");
    }

    private async Task AuditCombatActivity()
    {
        try
        {
            var screen = MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NSimpleCardSelectScreen.Create(
                Players[0].PlayerCombatState!.Hand.Cards.Take(3).ToList(),
                new MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs(MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs.DiscardSelectionPrompt, 1) { Cancelable = true });
            MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack.Instance!.Push(screen);
            var published = await CapturePublishedFrame();
            await Capture("combat-selection-source");
            MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack.Instance.Remove(screen);
            await ToSignal(GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout);
            DemoController.Current!.EnterDemo(2);
            var frame = new DemoRewardMessage { ProtocolVersion = DemoRewardMessage.Protocol, Sequence = ++_sequence,
                Location = RunManager.Instance.DebugOnlyGetState()!.RunLocation, Kind = DemoRewardKind.Activity,
                Screen = "NSimpleCardSelectScreen", Cards = [], Labels = [], Chosen = -1, Hovered = -1 };
            Net.Inject(frame, 2);
            await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
            if (RewardDemoController.ObservedPlayerId != 2 || DemoController.IsViewing)
                throw new Exception("Combat did not hand off to remote native selection");
            InjectNativeMetadata(published.Data);
            await Capture("combat-selection");
            // Screen descriptor + full metadata in one tick must survive a renderer replacement.
            frame.Sequence = ++_sequence; frame.Screen = "NChooseACardSelectionScreen";
            Net.Inject(frame, 2);
            var state = DemoVisualCodec.Decode(published.Data) with { Screen = frame.Screen };
            InjectNativeMetadata(DemoVisualCodec.Encode(state));
            var projection = Descendants<Sts2Demo.Ui.DemoVisualProjection>(RewardDemoController.Current!).Single();
            if (projection.VisualCount != state.Items.Count) throw new Exception("Same-tick descriptor/full state was lost");
            frame.Sequence = ++_sequence; frame.Screen = "NCombatRoom"; frame.Kind = DemoRewardKind.None;
            Net.Inject(frame, 2);
            await ToSignal(GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout);
            if (DemoController.ObservedPlayerId != 2 || RewardDemoController.IsViewing)
                throw new Exception("Remote selection did not return to combat observation");
            await Capture("combat-selection-return");
            frame.Sequence = ++_sequence; frame.Screen = "UnknownFutureCombatWindow";
            Net.Inject(frame, 2);
            await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
            if (DemoController.IsViewing || RewardDemoController.Current!.CanObserve(2)) throw new Exception("Unknown combat window remained observable");
            frame.Sequence = ++_sequence; frame.Screen = "NCombatRoom"; Net.Inject(frame, 2);
            DemoController.Current!.ExitDemo();
            Log.Info("[DemoLab] PASS: combat -> remote selection -> combat, same-tick screen replacement and full metadata.");
            var pile = Players[0].PlayerCombatState!.DrawPile;
            NCardPileScreen.ShowScreen(pile, []);
            await AuditActivity("combat-pile");
            MegaCrit.Sts2.Core.Nodes.Screens.Capstones.NCapstoneContainer.Instance!.Close();
            var pileSelection = MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NCombatPileCardSelectScreen.Create(pile,
                new MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs(MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs.DiscardSelectionPrompt, 1) { Cancelable = true }, _ => true);
            MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack.Instance!.Push(pileSelection);
            await AuditActivity("combat-pile-selection");
            MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack.Instance.Remove(pileSelection);
        }
        catch (Exception e) { Log.Error("[DemoLab] COMBAT ACTIVITY FAILED: " + e); }
        finally
        {
            RewardDemoController.Current?.Exit(); DemoController.Current?.ExitDemo();
            Net.Inject(new DemoRewardMessage { ProtocolVersion = DemoRewardMessage.Protocol, Sequence = ++_sequence,
                Location = RunManager.Instance.DebugOnlyGetState()!.RunLocation, Kind = DemoRewardKind.None,
                Screen = "NCombatRoom", Cards = [], Labels = [] }, 2);
        }
    }

    private async Task AuditAvailabilityAndMap()
    {
        var controller = RewardDemoController.Current!;
        controller.Exit();
        var location = RunManager.Instance.DebugOnlyGetState()!.RunLocation;
        var screen = ActiveScreenContext.Instance.GetCurrentScreen()!.GetType().Name;
        void Frame(ulong id, DemoRewardKind kind, string name) => Net.Inject(new DemoRewardMessage {
            ProtocolVersion = DemoRewardMessage.Protocol, Sequence = ++_sequence, Location = location,
            Kind = kind, Screen = name, Cards = [], Labels = [], Entries = [], Chosen = -1, Hovered = -1 }, id);
        try
        {
            DemoSettings.SeparateButton = true;
            var frames = (System.Collections.IDictionary)AccessTools.Field(typeof(RewardDemoController), "_frames").GetValue(controller)!;
            frames.Remove(3UL);
            Frame(2, DemoRewardKind.Activity, screen);
            await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
            var eyes = Descendants<TextureButton>(controller).Where(e => e.Name.ToString().StartsWith("AvatarDemoEye_")).ToList();
            if (eyes.Count != Players.Length - 1 || eyes.Any(e => !e.Visible) || Math.Abs(eyes[0].Position.X - eyes[1].Position.X) > .1f)
                throw new Exception("Buttons did not form one visible aligned column while peer 3 was late");
            controller.Toggle(3);
            await Capture("entry-buttons-loading");
            if ((ulong?)AccessTools.Field(typeof(RewardDemoController), "_loadingTarget").GetValue(controller) != 3)
                throw new Exception("Missing snapshot did not keep pending button state");
            Frame(3, DemoRewardKind.Activity, screen);
            await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
            if (RewardDemoController.ObservedPlayerId != 3) throw new Exception("Late snapshot did not finish pending entry");
            controller.Exit();
            Frame(3, DemoRewardKind.None, "UnknownFutureRoom");
            await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
            if (eyes.Any(e => !e.Visible) || !eyes.Single(e => e.Name == "AvatarDemoEye_3").Disabled)
                throw new Exception("Unavailable peer removed a button instead of disabling it");
            await Capture("entry-buttons-unavailable-peer");
            Log.Info("[DemoLab] PASS: grouped aligned eye buttons, delayed snapshot loading, unavailable peer stays visible/disabled.");
            Frame(2, DemoRewardKind.Activity, screen); controller.Enter(2);
            await AuditHeaderUtilities();
            controller.Exit();
            NMapScreen.Instance!.Open(isOpenedFromTopBar: true);
            await Capture("map-source");
            NMapScreen.Instance.Close();
            var mapRun = RunManager.Instance.DebugOnlyGetState()!;
            var synchronizer = RunManager.Instance.MapSelectionSynchronizer;
            var vote = new MegaCrit.Sts2.Core.Multiplayer.Game.MapVote { coord = new MegaCrit.Sts2.Core.Map.MapCoord(0, 1), mapGenerationCount = synchronizer.MapGenerationCount };
            synchronizer.PlayerVotedForMapCoord(Players[0], mapRun.MapLocation, vote);
            Frame(2, DemoRewardKind.Map, "NMapScreen"); controller.Enter(2);
            if (synchronizer.GetVote(Players[0])?.coord != vote.coord || NMapScreen.Instance.PlayerVoteDictionary[Players[0]] != vote.coord)
                throw new Exception("DEMO map reset local vote");
            await Capture("map");
            if (!NMapScreen.Instance.IsOpen || RewardDemoController.ObservedPlayerId != 2)
                throw new Exception("Remote map did not open native local map");
            controller.Exit();
            if (NMapScreen.Instance.IsOpen) throw new Exception("Mod-opened map did not close on exit");
            Frame(2, DemoRewardKind.Activity, screen); controller.Enter(2);
            Frame(2, DemoRewardKind.Activity, "UnknownFutureRoom");
            await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
            if (RewardDemoController.IsViewing || controller.CanObserve(2)) throw new Exception("Unknown screen remained observable");
            Log.Info("[DemoLab] PASS: local native map and unknown remote screen closes observation.");
            DemoSettings.AllActivities = false; // Legacy key cannot restrict 1.4.0.
            Frame(2, DemoRewardKind.Activity, screen);
            if (!controller.Enter(2)) throw new Exception("Removed battle-only setting still restricts activities");
            Log.Info("[DemoLab] PASS: removed availability preference cannot restrict supported activities.");
        }
        catch (Exception e) { Log.Error("[DemoLab] AVAILABILITY/MAP FAILED: " + e); }
        finally { controller.Exit(); DemoSettings.AllActivities = true; DemoSettings.SeparateButton = true; NMapScreen.Instance?.Close(); }
    }
}
