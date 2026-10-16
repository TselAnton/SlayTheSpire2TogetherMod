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
using Sts2Demo.Core;
using Sts2Demo.Networking;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace Sts2DemoLab;

// Native source UI + production wire encoder/decoder, in the isolated lab only.
public partial class LabOracle
{
    private async Task AuditSettings()
    {
        RewardDemoController.Current!.Exit();
        var settings = (MegaCrit.Sts2.Core.Nodes.Screens.Settings.NSettingsScreen)NRun.Instance!.GlobalUi.SubmenuStack.ShowScreen(CapstoneSubmenuType.Settings);
        var manager = settings.GetNode<MegaCrit.Sts2.Core.Nodes.Screens.Settings.NSettingsTabManager>("%SettingsTabManager");
        await Capture("settings-native-general");
        manager.GetNode<MegaCrit.Sts2.Core.Nodes.Screens.Settings.NSettingsTab>("FriendView").ForceTabPressed();
        await Capture("settings");
        var toggles = Descendants<NTickbox>(settings).Where(t => t.Name == "Toggle").ToList();
        if (toggles.Count != 5) throw new Exception("Missing mod settings controls");
        var layout = Descendants<Control>(settings).Where(c => c.GetPath().ToString().Contains("FriendViewSettings") || c.GetPath().ToString().Contains("ShowMpDrawings"))
            .Select(c => new { Path = c.GetPath().ToString(), Type = c.GetClass().ToString(), Rect = c.GetGlobalRect().ToString(), Local = c.Position.ToString(),
                H = c.SizeFlagsHorizontal.ToString(), V = c.SizeFlagsVertical.ToString(), A = new[] { c.AnchorLeft, c.AnchorRight }, O = new[] { c.OffsetLeft, c.OffsetRight } });
        System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!, "../validation-1.4.0/settings-layout.json"), System.Text.Json.JsonSerializer.Serialize(layout));
        foreach (var toggle in toggles)
        {
            var row = toggle.GetParent<Control>();
            var label = Descendants<RichTextLabel>(row).First();
            if (label.GetGlobalRect().Position.X >= toggle.GetGlobalRect().GetCenter().X)
                throw new Exception("Setting caption overlaps the right-hand toggle");
        }
        toggles[0].EmitSignal(Control.SignalName.MouseEntered);
        await Capture("settings-tooltip");
        toggles[0].EmitSignal(Control.SignalName.MouseExited);
        string language = MegaCrit.Sts2.Core.Localization.LocManager.Instance.Language;
        bool[] originalTicks = toggles.Select(t => t.IsTicked).ToArray();
        var originalSettings = (DemoSettings.SeparateButton, DemoSettings.AutoExitTurn,
            DemoSettings.AutoObserve, DemoSettings.AutoExitRoom, DemoSettings.FollowCombat);
        try
        {
            MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage("eng");
            await Capture("settings-english");
            toggles[1].EmitSignal(Control.SignalName.MouseEntered);
            await Capture("settings-english-tooltip");
            toggles[1].EmitSignal(Control.SignalName.MouseExited);
            DemoSettings.SeparateButton = DemoSettings.AutoExitTurn = DemoSettings.AutoObserve =
                DemoSettings.AutoExitRoom = DemoSettings.FollowCombat = true;
            foreach (var toggle in toggles) toggle.IsTicked = true;
            await Capture("workshop-settings-defaults");
            string[] settingNames = ["button", "exit-turn", "auto-watch", "exit-room", "follow-cards"];
            for (int i = 0; i < toggles.Count; i++)
            {
                toggles[i].EmitSignal(Control.SignalName.MouseEntered);
                await Capture("workshop-setting-" + settingNames[i]);
                toggles[i].EmitSignal(Control.SignalName.MouseExited);
            }
        }
        finally
        {
            (DemoSettings.SeparateButton, DemoSettings.AutoExitTurn, DemoSettings.AutoObserve,
                DemoSettings.AutoExitRoom, DemoSettings.FollowCombat) = originalSettings;
            for (int i = 0; i < toggles.Count; i++) toggles[i].IsTicked = originalTicks[i];
            MegaCrit.Sts2.Core.Localization.LocManager.Instance.SetLanguage(language);
        }
        foreach (var toggle in toggles) HarmonyLib.AccessTools.Method(typeof(NTickbox), "OnRelease").Invoke(toggle, null);
        Sts2Demo.Core.DemoSettings.Load();
        if (Sts2Demo.Core.DemoSettings.SeparateButton || !Sts2Demo.Core.DemoSettings.AllActivities)
            throw new Exception("Mod setting persistence mismatch");
        await Capture("settings-player-combat");
        Sts2Demo.Core.DemoSettings.SeparateButton = true; Sts2Demo.Core.DemoSettings.AllActivities = true;
        Sts2Demo.Core.DemoSettings.AutoObserve = false; Sts2Demo.Core.DemoSettings.AutoExitTurn = false; Sts2Demo.Core.DemoSettings.AutoExitRoom = false; Sts2Demo.Core.DemoSettings.FollowCombat = true;
        Sts2Demo.Core.DemoSettings.Save();
        NCapstoneContainer.Instance!.Close();
        Log.Info("[DemoLab] PASS: native settings tab, five options, persistence and restoration.");
    }

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
        var nativeState = DemoVisualCodec.Decode(published.Data);
        if (name == "inspect-upgraded" && !nativeState.Items.Any(v => v.Id.Contains("HoverTip")))
            throw new Exception("Native inspector keyword tooltip missing from published state");
        if (name == "inspect-relic" && !nativeState.Items.Any(v => v.AlphaMasks.Count > 0))
            throw new Exception("Native relic inspector alpha mask missing from published state");
        if (name.StartsWith("predictions", StringComparison.Ordinal))
        {
            bool visibleItem = nativeState.Items.Any(v => v.Id.Contains("/Items/") && v.Kind == 0);
            if (visibleItem != (name != "predictions")) throw new Exception("Prediction item visibility does not match revealed cells");
            if (name == "predictions-partial" && nativeState.Items.Where(v => v.Id.Contains("/Items/") && v.Kind == 0).Any(v => v.Clip.Length != 4))
                throw new Exception("Partially revealed item lost cell clipping");
        }
        if (name == "treasure" && nativeState.Items.Where(v => v.Kind == 6).Any(v => v.SpinePlayback.Length != 3 || v.SpinePlayback[1] != 0))
            throw new Exception("Closed chest animation lost native pause");
        System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!, "../validation-1.4.0/visuals-" + name + ".json"),
            System.Text.Json.JsonSerializer.Serialize(DemoVisualCodec.Decode(published.Data)));
        if (name is "merchant-inventory" or "choose-relic")
        {
            var relicNodes = Descendants<MegaCrit.Sts2.Core.Nodes.Relics.NRelic>(NRun.Instance!).Where(n => n.IsVisibleInTree())
                .SelectMany(n => new Node[] { n }.Concat(Descendants<Node>(n))).OfType<CanvasItem>()
                .Select(n => new { Path = n.GetPath().ToString(), n.ShowBehindParent, n.ZIndex, n.ZAsRelative,
                    Tint = n.Modulate.ToString(), SelfTint = n.SelfModulate.ToString(), n.UseParentMaterial,
                    Shader = (n.Material as ShaderMaterial)?.Shader?.Code });
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!, "../validation-1.4.0/relic-draw-" + name + ".json"),
                System.Text.Json.JsonSerializer.Serialize(relicNodes));
        }
        if (name == "predictions")
        {
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!, "../validation-1.4.0/prediction-visuals.json"),
                System.Text.Json.JsonSerializer.Serialize(DemoVisualCodec.Decode(published.Data).Items.Where(v => v.Id.Contains("DivinationButton"))));
            var nodes = Descendants<Node>(NRun.Instance!).Where(n => n.GetPath().ToString().Contains("DivinationButton"))
                .Select(n => new { Path = n.GetPath().ToString(), Type = n.GetClass().ToString(),
                    Size = (n as Control)?.Size.ToString(), Scale = (n as Control)?.Scale.ToString(),
                    Texture = n is TextureRect t ? t.Texture?.ResourcePath : null,
                    Properties = n.GetPropertyList().Select(p => p["name"].AsString()).Where(p => p.Contains("texture") || p.Contains("stretch") || p.Contains("frame") || p.Contains("region"))
                        .ToDictionary(p => p, p => n.Get(p).ToString()) });
            System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!, "../validation-1.4.0/prediction-nodes.json"), System.Text.Json.JsonSerializer.Serialize(nodes));
        }
        var elapsed = new List<double>();
        for (int sample = 0; sample < 5; sample++)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Sts2Demo.Ui.DemoVisualProjection.CaptureActivity((Node)MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.ActiveScreenContext.Instance.GetCurrentScreen()!);
            elapsed.Add(stopwatch.Elapsed.TotalMilliseconds);
        }
        Log.Info($"[DemoLab] CAPTURE COST {name}: mean={elapsed.Average():F2}ms max={elapsed.Max():F2}ms (5 samples, metadata only).");
        var banners = Descendants<NCommonBanner>(NGame.Instance!).Where(b => b.IsVisibleInTree()).SelectMany(b => new Node[] { b }.Concat(Descendants<Node>(b)))
            .Select(n => new { Path = n.GetPath().ToString(), Type = n.GetClass().ToString(), Z = (n as CanvasItem)?.ZIndex,
                Tint = (n as CanvasItem)?.Modulate.ToString(), Self = (n as CanvasItem)?.SelfModulate.ToString(),
                Font = n is Label l ? l.GetThemeFontSize("font_size") : 0, Material = (n as CanvasItem)?.Material?.ResourcePath,
                Size = (n as Control)?.Size.ToString(), Texture = (n as TextureRect)?.Texture?.ResourcePath,
                TextureType = (n as TextureRect)?.Texture?.GetClass().ToString() });
        System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!, "../validation-1.4.0/banner-" + name + ".json"),
            System.Text.Json.JsonSerializer.Serialize(banners));
        var diagnostics = Descendants<Node>(NRun.Instance!).Where(n => n.GetClass().ToString().StartsWith("Spine"))
            .Select(n => new { Path = n.GetPath().ToString(), Asset = n.GetClass() == "SpineSprite" ? (n.Get("skeleton_data_res").AsGodotObject() as Resource)?.ResourcePath : null,
                Properties = n.GetPropertyList().Select(p => p["name"].AsString()).Where(p => p.Contains("material") || p.Contains("slot"))
                    .ToDictionary(p => p, p => n.Get(p).VariantType == Variant.Type.Object ? (n.Get(p).AsGodotObject() as Resource)?.ResourcePath : n.Get(p).ToString()) }).ToArray();
        System.IO.File.WriteAllText(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(OS.GetExecutablePath())!, "../validation-1.4.0/spine-" + name + ".json"),
            System.Text.Json.JsonSerializer.Serialize(diagnostics));
        await Capture(name + "-source");
        InjectActivity();
        if (!RewardDemoController.Current.Enter(2)) throw new Exception("Audit entry refused: " + name);
        InjectNativeMetadata(published.Data, published.Origin, published.Size);
        var projectionNodes = (Dictionary<string, Control>)HarmonyLib.AccessTools.Field(typeof(Sts2Demo.Ui.DemoVisualProjection), "_leaves")
            .GetValue(Descendants<Sts2Demo.Ui.DemoVisualProjection>(RewardDemoController.Current!).Single())!;
        foreach (var visual in nativeState.Items.Where(v => v.Kind == 7))
        {
            var leaf = projectionNodes[visual.Id];
            if (visual.CanvasMaterial.Length == 6 && (leaf.Material is not CanvasItemMaterial material
                || (int)material.BlendMode != visual.CanvasMaterial[0])) throw new Exception("Particle blend material lost: " + visual.Id);
            if (Descendants<GpuParticles2D>(leaf).Any(p => !p.UseParentMaterial)) throw new Exception("Particle shader not inherited");
        }
        await Capture(name);
        Log.Info("[DemoLab] VISUAL PAIR: " + name);
        if (name is "event-dialogue" or "choose-card" or "select-upgrade") await AuditLocalHover(name, nativeState);
        if (name == "choose-card") await Audit140AvatarExit();
        if (name == "event-dialogue")
        {
            var cross = DemoVisualCodec.Decode((await CapturePublishedFrame()).Data);
            if (!RewardDemoController.IsViewing || cross.Screen != nativeState.Screen || cross.Items.Any(v => v.Id.Contains("TogetherRewardDemo") || v.Id.Contains("FriendViewTopBar")))
                throw new Exception("Cross-activity publication recursed or lost native screen");
            Log.Info("[DemoLab] PASS 1.4.0: cross-activity publication sends underlying native screen while both peers observe.");
        }
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
        await AuditRewardOwnership();
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
        try
        {
            var relics = Players[0].Relics.ToList();
            NGame.Instance!.GetInspectRelicScreen().Open(relics, relics[0]);
            await AuditActivity("inspect-relic");
        }
        catch (Exception e) { Log.Error("[DemoLab] VISUAL AUDIT FAILED inspect-relic: " + e); }
        finally { NGame.Instance!.GetInspectRelicScreen().Close(); }
        await RunManager.Instance.EnterRoomDebug(RoomType.Event,
            model: ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.FakeMerchant>(), showTransition: false);
        await AuditActivity("fake-merchant");
        Descendants<MegaCrit.Sts2.Core.Nodes.Events.Custom.NFakeMerchant>(NRun.Instance!).Single().Inventory.Open();
        await AuditActivity("fake-merchant-inventory");
    }

    private async Task AuditRewardOwnership()
    {
        var controller = RewardDemoController.Current!;
        controller.Exit();
        var local = NCardRewardSelectionScreen.ShowScreen(_localCards.Select(c => new CardCreationResult(c)).ToList(), []);
        await Capture("reward-owner-local");
        foreach (ulong id in new ulong[] { 2, 3, 2 })
        {
            var expected = id == 2 ? _rewardCards : _thirdRewardCards;
            Net.Inject(new DemoRewardMessage { ProtocolVersion = DemoRewardMessage.Protocol, Sequence = ++_sequence,
                Location = RunManager.Instance.DebugOnlyGetState()!.RunLocation, Kind = DemoRewardKind.CardChoice,
                Cards = expected, Labels = [], Entries = [], Hovered = -1, Chosen = -1 }, id);
            if (!controller.Enter(id)) throw new Exception("Ownership test entry refused");
            await ToSignal(GetTree().CreateTimer(.25), SceneTreeTimer.SignalName.Timeout);
            var models = Descendants<MegaCrit.Sts2.Core.Nodes.Cards.NCard>(controller.GetNode("ReadOnlyRewards")).Select(c => c.Model).ToList();
            bool match = models.Count == expected.Count && models.Select(c => c!.Id).SequenceEqual(expected.Select(c => c.Id))
                && models.All(c => c!.Owner.NetId == id) && !models.Select(c => c!.Id).SequenceEqual(_localCards.Select(c => c.Id));
            if (!match) throw new Exception("Reward ownership mismatch: " + id);
            Log.Info("[DemoLab] PASS: reward options and owners match sender " + id + "; differ from observer; no stale cards after switch.");
            await Capture("reward-owner-" + id);
        }
        controller.Exit();
        if (local != null) NOverlayStack.Instance!.Remove(local);
    }

    private async Task AuditRewardCards()
    {
        var screen = NCardRewardSelectionScreen.ShowScreen(_localCards.Select(c => new CardCreationResult(c)).ToList(), []);
        try
        {
            await ToSignal(GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
            Net.PublishedRewards.Clear();
            Net.Inject(new DemoPresenceMessage { ProtocolVersion = 1, Sequence = ++_sequence,
                Location = RunManager.Instance.DebugOnlyGetState()!.RunLocation, Watching = true, RequestSnapshot = true, TargetId = 1 }, 2);
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
