using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using Sts2Demo;
using Sts2Demo.Networking;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace Sts2DemoLab;

// Native source UI + production wire encoder/decoder, in the isolated lab only.
public partial class LabOracle
{
    private async Task AuditHeaderUtilities()
    {
        var bar = NRun.Instance!.GlobalUi.TopBar;
        foreach (var (name, button) in new[] { ("map", bar.Map as NButton), ("deck", bar.Deck as NButton), ("settings", bar.Pause as NButton) })
        {
            InjectActivity();
            HarmonyLib.AccessTools.Method(button!.GetType(), "OnRelease").Invoke(button, null);
            await ToSignal(GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout);
            bool correct = RewardDemoController.IsViewing && Sts2Demo.Ui.DemoTopBar.UtilityOpen;
            if (name == "deck")
            {
                var deck = NCapstoneContainer.Instance!.CurrentCapstoneScreen;
                correct &= deck is NDeckViewScreen && ReferenceEquals(
                    HarmonyLib.AccessTools.Field(typeof(NDeckViewScreen), "_player").GetValue(deck), Players[1]);
            }
            await Capture("header-local-" + name);
            if (correct) Log.Info("[DemoLab] PASS: native header " + name + " preserves observation and correct ownership.");
            else Log.Error("[DemoLab] FAIL: native header " + name + " routing or observer state.");
            NCapstoneContainer.Instance!.Close(); NRun.Instance.GlobalUi.MapScreen.Close();
            await ToSignal(GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout);
        }
    }
    private async Task AuditActivity(string name)
    {
        RewardDemoController.Current!.Exit();
        await ToSignal(GetTree().CreateTimer(.8), SceneTreeTimer.SignalName.Timeout);
        var published = await CapturePublishedFrame();
        await Capture(name + "-source");
        InjectActivity();
        if (!RewardDemoController.Current.Enter(2)) throw new Exception("Audit entry refused: " + name);
        InjectNativeMetadata(published.Data, published.Origin, published.Size);
        await Capture(name);
        Log.Info("[DemoLab] VISUAL PAIR: " + name);
        RewardDemoController.Current.Exit();
    }

    private async Task AuditBlocked(string name)
    {
        RewardDemoController.Current!.Exit();
        await ToSignal(GetTree().CreateTimer(.8), SceneTreeTimer.SignalName.Timeout);
        InjectActivity();
        bool entered = RewardDemoController.Current.Enter(2);
        await Capture(name);
        if (entered) Log.Error("[DemoLab] FAIL: forbidden merchant observation entered: " + name);
        else Log.Info("[DemoLab] PASS: merchant observation denied: " + name);
        RewardDemoController.Current.Exit();
    }

    private async Task AuditCardScreens()
    {
        var cards = _localCards;
        var previewCards = Players[0].Deck.Cards.Where(c => c.IsUpgradable).Take(3).ToList();
        await AuditRewardCards();
        async Task Overlay(string name, Func<Node?> create)
        {
            Node? screen = null;
            try
            {
                screen = create(); await AuditActivity(name);
                if (screen is NDeckUpgradeSelectScreen or NDeckTransformSelectScreen or NDeckEnchantSelectScreen)
                {
                    // Only open the native confirmation preview, never confirm/apply it.
                    HarmonyLib.AccessTools.Method(screen.GetType(), "OnCardClicked").Invoke(screen, [previewCards[0]]);
                    await AuditActivity(name + "-preview");
                }
            }
            catch (Exception e) { Log.Error("[DemoLab] VISUAL AUDIT FAILED " + name + ": " + e); }
            finally
            {
                RewardDemoController.Current!.Exit();
                if (screen is IOverlayScreen overlay && screen.IsInsideTree()) NOverlayStack.Instance!.Remove(overlay);
            }
        }
        await Overlay("choose-card", () => NChooseACardSelectionScreen.ShowScreen(cards, true));
        await Overlay("choose-bundle", () => NChooseABundleSelectionScreen.ShowScreen(
            new List<IReadOnlyList<CardModel>> { cards.Take(2).ToList(), cards.Skip(1).ToList() }));
        await Overlay("select-deck", () =>
        {
            var screen = NDeckCardSelectScreen.Create(cards, new(CardSelectorPrefs.RemoveSelectionPrompt, 1) { Cancelable = true });
            NOverlayStack.Instance!.Push(screen); return screen;
        });
        await Overlay("select-simple", () =>
        {
            var screen = NSimpleCardSelectScreen.Create(cards, new(CardSelectorPrefs.DiscardSelectionPrompt, 1, 2));
            NOverlayStack.Instance!.Push(screen); return screen;
        });
        await Overlay("select-upgrade", () => NDeckUpgradeSelectScreen.ShowScreen(previewCards,
            new(CardSelectorPrefs.UpgradeSelectionPrompt, 1), RunManager.Instance.DebugOnlyGetState()!));
        await Overlay("select-transform", () => NDeckTransformSelectScreen.ShowScreen(previewCards,
            c => new CardTransformation(c, previewCards[(previewCards.IndexOf(c) + 1) % previewCards.Count]), new(CardSelectorPrefs.TransformSelectionPrompt, 1)));
        await Overlay("select-enchant", () => NDeckEnchantSelectScreen.ShowScreen(previewCards,
            ModelDb.Enchantment<MegaCrit.Sts2.Core.Models.Enchantments.Vigorous>(), 1, new(CardSelectorPrefs.EnchantSelectionPrompt, 1)));
        try
        {
            NDeckViewScreen.ShowScreen(Players[0]);
            await AuditActivity("deck");
        }
        catch (Exception e) { Log.Error("[DemoLab] VISUAL AUDIT FAILED deck: " + e); }
        finally { NCapstoneContainer.Instance!.Close(); }
        try
        {
            NSimpleCardsViewScreen.ShowScreen(cards.Select(c => new CardPileAddResult { success = true, cardAdded = c }).ToList(),
                new MegaCrit.Sts2.Core.Localization.LocString("gameplay_ui", "DECK_PILE_INFO"));
            await AuditActivity("cards-view");
        }
        catch (Exception e) { Log.Error("[DemoLab] VISUAL AUDIT FAILED cards-view: " + e); }
        finally { NCapstoneContainer.Instance!.Close(); }
        try
        {
            NGame.Instance!.GetInspectCardScreen().Open(cards, 0);
            await AuditActivity("inspect");
            NGame.Instance.GetInspectCardScreen().Close();
            await ToSignal(GetTree().CreateTimer(.4), SceneTreeTimer.SignalName.Timeout);
            NGame.Instance.GetInspectCardScreen().Open(cards, 0, true);
            await AuditActivity("inspect-upgraded");
        }
        catch (Exception e) { Log.Error("[DemoLab] VISUAL AUDIT FAILED inspect: " + e); }
        finally { NGame.Instance!.GetInspectCardScreen().Close(); }
        await RunManager.Instance.EnterRoomDebug(RoomType.Event,
            model: ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.FakeMerchant>(), showTransition: false);
        await AuditBlocked("blocked-fake-merchant");
        Descendants<MegaCrit.Sts2.Core.Nodes.Events.Custom.NFakeMerchant>(NRun.Instance!).Single().Inventory.Open();
        await AuditBlocked("blocked-fake-merchant-inventory");
    }

    private async Task AuditRewardCards()
    {
        var screen = NCardRewardSelectionScreen.ShowScreen(_localCards.Select(c => new CardCreationResult(c)).ToList(), []);
        try
        {
            await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
            Net.PublishedRewards.Clear();
            Net.Inject(new DemoPresenceMessage { ProtocolVersion = 1, Sequence = ++_sequence,
                Location = RunManager.Instance.DebugOnlyGetState()!.RunLocation, Watching = true, TargetId = 1 }, 2);
            await ToSignal(GetTree().CreateTimer(1.2), SceneTreeTimer.SignalName.Timeout);
            var frame = Net.PublishedRewards.Last(m => m.Kind == DemoRewardKind.CardChoice && !m.Delta && m.Cards!.Count == 3);
            await Capture("audit-reward-cards-source");
            for (int i = 0; i < 3; i++)
            {
                frame.Sequence = ++_sequence; frame.Cursor = frame.Centers![i]; frame.Controller = false; frame.Hovered = 2; frame.Chosen = -1;
                Net.Inject(frame, 2);
                if (!RewardDemoController.IsViewing) RewardDemoController.Current!.Enter(2);
                await Capture("audit-reward-hover-" + i);
            }
            var tick = Descendants<NTickbox>(RewardDemoController.Current!.GetNode("ReadOnlyRewards")).Single();
            tick.IsTicked = true; tick.EmitSignal(NTickbox.SignalName.Toggled, tick);
            await Capture("audit-reward-upgraded");
        }
        catch (Exception e) { Log.Error("[DemoLab] VISUAL AUDIT FAILED reward-cards: " + e); }
        finally { RewardDemoController.Current!.Exit(); if (screen != null) NOverlayStack.Instance!.Remove(screen); }
    }
}
