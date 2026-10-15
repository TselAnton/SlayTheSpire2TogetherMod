using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Potions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.sts2.Core.Nodes.TopBar;

namespace Sts2Demo.Ui;

/// <summary>Native display controls for the observed player; native local utility buttons stay in place.</summary>
public partial class DemoTopBar : Control
{
    private readonly Dictionary<Control, Color> _hidden = [];
    private readonly List<(Control Source, Control Copy)> _copies = [];
    private readonly Dictionary<Control, MouseFilterEnum> _mouseFilters = [];
    private ulong? _target;
    private int _oldZ;
    private Color _oldBarTint;
    private object? _entryScreen;
    private string _potionsKey = "";
    private Control? _potions;
    private Control? _potionSlots;
    private Control? _relics;
    private string _relicsKey = "";
    public static DemoTopBar? Current { get; private set; }
    public static void BeginObservation()
    { if (Current != null && (Current._target == null || !UtilityOpen && !RewardDemoController.OpenedObserverMap)) Current._entryScreen = ActiveScreenContext.Instance.GetCurrentScreen(); }
    public static object? PublicationScreen => (UtilityOpen || RewardDemoController.OpenedObserverMap)
        && Current?._entryScreen is Node entry && GodotObject.IsInstanceValid(entry) && entry.IsInsideTree()
        ? entry : ActiveScreenContext.Instance.GetCurrentScreen();
    public static Player? ObservedPlayer => (DemoController.ObservedPlayerId ?? RewardDemoController.ObservedPlayerId) is ulong id
        ? RunManager.Instance.DebugOnlyGetState()?.GetPlayer(id) : null;
    public static bool UtilityOpen
    {
        get
        {
            if (ObservedPlayer == null || Current == null) return false;
            var screen = ActiveScreenContext.Instance.GetCurrentScreen();
            if (RewardDemoController.OpenedObserverMap && screen?.GetType().Name == "NMapScreen") return false;
            return screen != Current._entryScreen && screen?.GetType().Name is
                "NCardPileScreen" or "NSimpleCardsViewScreen" or "NMapScreen" or "NDeckViewScreen" or "NInspectCardScreen" or "NPauseMenu" or "NSettingsScreen" or "NCapstoneSubmenuStack" or "NSettingsPanel";
        }
    }
    public override void _Ready()
    {
        Current = this; MouseFilter = MouseFilterEnum.Ignore; ZIndex = 1201;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
    }
    private T Copy<T>(T source) where T : Control
    {
        var copy = (T)source.Duplicate((int)Node.DuplicateFlags.Scripts);
        foreach (var child in DemoUi.Descendants<Node>(copy))
        {
            bool unique = child.UniqueNameInOwner;
            child.Owner = copy; child.UniqueNameInOwner = unique || child.Name == "HpLabel";
        }
        AddChild(copy); copy.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        copy.Size = source.Size; copy.Modulate = Colors.White;
        DemoUi.ReadOnly(copy); _copies.Add((source, copy)); HideOriginal(source);
        return copy;
    }
    private void HideOriginal(Control source)
    {
        foreach (var holder in DemoUi.Descendants<NRelicInventoryHolder>(source)) MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet.Remove(holder);
        _hidden.TryAdd(source, source.Modulate); source.Modulate = new Color(source.Modulate, 0);
        foreach (var control in DemoUi.Descendants<Control>(source).Prepend(source))
        { _mouseFilters.TryAdd(control, control.MouseFilter); control.MouseFilter = MouseFilterEnum.Ignore; }
    }
    private void Restore()
    {
        foreach (var pair in _hidden) if (GodotObject.IsInstanceValid(pair.Key)) pair.Key.Modulate = pair.Value;
        foreach (var pair in _mouseFilters) if (GodotObject.IsInstanceValid(pair.Key)) pair.Key.MouseFilter = pair.Value;
        _mouseFilters.Clear();
        _hidden.Clear(); _copies.Clear(); _potions = null; _potionsKey = ""; _relics = null; _relicsKey = "";
        foreach (var child in GetChildren()) { RemoveChild(child); child.QueueFree(); }
        if (_target != null && NRun.Instance != null)
        {
            var bar = NRun.Instance.GlobalUi.TopBar; bar.ZIndex = _oldZ; bar.Modulate = _oldBarTint;
            AccessTools.Method(bar.Deck.GetType(), "OnPileContentsChanged").Invoke(bar.Deck, null);
        }
        _target = null;
    }
    public override void _Process(double delta)
    {
        var player = ObservedPlayer;
        if (_target != player?.NetId)
        {
            Restore();
            if (player == null || NRun.Instance == null) return;
            _target = player.NetId;
            var bar = NRun.Instance.GlobalUi.TopBar; _oldZ = bar.ZIndex; _oldBarTint = bar.Modulate; bar.ZIndex = 1200;
            Copy(bar.Hp).Initialize(player);
            // Gold's popup is a sibling in the full top-bar scene. Copy only its
            // display nodes, without lifecycle methods that expect that sibling.
            var gold = (Control)bar.Gold.Duplicate(0);
            AddChild(gold); gold.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
            gold.Modulate = Colors.White; DemoUi.ReadOnly(gold);
            _copies.Add((bar.Gold, gold)); HideOriginal(bar.Gold);
            // Portrait has no subscriptions: start with the native frame, replace only its icon.
            var portrait = new Control { Size = bar.Portrait.Size, MouseFilter = MouseFilterEnum.Ignore };
            AddChild(portrait); portrait.AddChild(player.Character.Icon); _copies.Add((bar.Portrait, portrait));
            HideOriginal(bar.Portrait);
            var badge = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                Texture = DemoUi.LoadEye(), Size = new(28, 28), MouseFilter = MouseFilterEnum.Ignore,
                Position = portrait.Size - new Vector2(28, 28), Modulate = new(.65f, 1, .75f) };
            portrait.AddChild(badge);
            _potions = (Control)bar.PotionContainer.Duplicate(0);
            AddChild(_potions); _potions.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
            _potionSlots = _potions.GetNode<Control>("MarginContainer/PotionHolders");
            DemoUi.ReadOnly(_potions);
            _copies.Add((bar.PotionContainer, _potions)); HideOriginal(bar.PotionContainer);
            var sourceRelics = NRun.Instance.GlobalUi.RelicInventory;
            _relics = (Control)sourceRelics.Duplicate(0);
            foreach (var child in _relics.GetChildren()) { _relics.RemoveChild(child); child.Free(); }
            AddChild(_relics); _relics.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
            _relics.Modulate = Colors.White;
            _copies.Add((sourceRelics, _relics)); HideOriginal(sourceRelics);
        }
        if (player == null || NRun.Instance == null) return;
        Modulate = new Color(1, 1, 1, RewardDemoController.ChromeOpacity);
        NRun.Instance.GlobalUi.TopBar.Modulate = _oldBarTint * Modulate;
        string relicsKey = string.Join('|', player.Relics.Select(r => $"{r.Id}:{r.Status}:{r.DisplayAmount}:{r.ShowCounter}"));
        if (_relics != null && _relicsKey != relicsKey)
        {
            _relicsKey = relicsKey;
            foreach (var child in _relics.GetChildren()) { _relics.RemoveChild(child); child.QueueFree(); }
            foreach (var relic in player.Relics)
            {
                var holder = NRelicInventoryHolder.Create((RelicModel)relic.ClonePreservingMutability());
                if (holder == null) continue;
                _relics.AddChild(holder);
            }
        }
        foreach (var pair in _copies)
        {
            HideOriginal(pair.Source); // Native inventories may add children while we observe.
            pair.Copy.Position = GetGlobalTransformWithCanvas().AffineInverse() * pair.Source.GetGlobalTransformWithCanvas().Origin;
            pair.Copy.Size = pair.Source.Size;
            if (pair.Source == NRun.Instance.GlobalUi.TopBar.Gold)
                foreach (var label in DemoUi.Descendants<Label>(pair.Copy).Where(l => l.Name == "GoldLabel")) label.Text = player.Gold.ToString();
        }
        var deckCount = NRun.Instance.GlobalUi.TopBar.Deck.GetNode<MegaLabel>("DeckCardCount");
        string countText = player.Deck.Cards.Count.ToString();
        if (deckCount.Text != countText) deckCount.SetTextAutoSize(countText);
        string key = player.MaxPotionCount + ":" + string.Join('|', player.PotionSlots.Select(p => p?.Id.ToString() ?? ""));
        if (_potions != null && _potionSlots != null && key != _potionsKey)
        {
            _potionsKey = key;
            foreach (var child in _potionSlots.GetChildren()) { _potionSlots.RemoveChild(child); child.QueueFree(); }
            for (int i = 0; i < player.MaxPotionCount; i++)
            {
                var holder = NPotionHolder.Create(isUsable: false);
                if (holder == null) continue;
                _potionSlots.AddChild(holder);
                if (i < player.PotionSlots.Count && player.PotionSlots[i] is { } model)
                {
                    var potion = NPotion.Create(model);
                    if (potion != null) { potion.Position = new(-30, -30); holder.AddPotion(potion); }
                }
                DemoUi.ReadOnly(holder);
            }
        }
    }
    public override void _ExitTree() { Restore(); if (Current == this) Current = null; }
}

[HarmonyPatch(typeof(NDeckViewScreen), nameof(NDeckViewScreen.ShowScreen))]
internal static class ObserveDeckFromNativeButton
{
    static void Prefix(ref Player player)
    {
        if (DemoTopBar.ObservedPlayer is { } observed) player = observed;
    }
}

// Native potion shortcuts must never use the local player's potion behind a friend's display.
[HarmonyPatch(typeof(NPotionHolder), "OnRelease")]
internal static class BlockObservedPotionUse
{
    static bool Prefix() => DemoTopBar.ObservedPlayer == null;
}
[HarmonyPatch(typeof(NPotionContainer), "OnPotionShortcutPressed")]
internal static class BlockObservedPotionShortcut
{
    static bool Prefix() => DemoTopBar.ObservedPlayer == null;
}

[HarmonyPatch(typeof(NPotionPopup), "OnDiscardButtonPressed")]
internal static class BlockObservedPotionDiscard
{
    static bool Prefix() => DemoTopBar.ObservedPlayer == null;
}

[HarmonyPatch(typeof(NPotionHolder), nameof(NPotionHolder.UsePotion))]
internal static class BlockObservedPotionUseFromPopup
{
    static bool Prefix(ref Task __result)
    {
        if (DemoTopBar.ObservedPlayer == null) return true;
        __result = Task.CompletedTask;
        return false;
    }
}
