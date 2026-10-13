using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using Sts2Demo.Core;
using Sts2Demo.Networking;
using Sts2Demo.Ui;

namespace Sts2Demo;

/// <summary>Run-scoped observer for reward screens. Never claims a reward or inserts a card into a deck.</summary>
public partial class RewardDemoController : Control
{
    private sealed record RemoteFrame(DemoRewardMessage Message, ulong Seen);
    public static RewardDemoController? Current { get; private set; }
    public static bool IsViewing => Current?._target != null;
    public static ulong? ObservedPlayerId => Current?._target;
    private static ulong? _continueTarget;
    private static NRewardsScreen? _localRewardsScreen;
    private static RewardsSet? _localRewardsSet;
    private static NCardRewardSelectionScreen? _localChoiceScreen;
    private static IReadOnlyList<CardCreationResult> _localOptions = [];
    private static IReadOnlyList<CardRewardAlternative> _localAlternatives = [];
    private static int _chosen = -1;
    private static ulong _choiceHoldUntil;
    private static DemoRewardMessage? _summaryClick;
    private static ulong _summaryHoldUntil;
    private readonly Dictionary<ulong, RemoteFrame> _frames = [];
    private readonly Dictionary<ulong, (DemoPresenceMessage Message, ulong Seen)> _presence = [];
    private readonly Dictionary<ulong, TextureButton> _eyes = [];
    private readonly Dictionary<TextureRect, Color> _avatarTints = [];
    private static readonly Color ActiveTint = new(.65f, 1f, .75f);
    private int? _playerListZ;
    private readonly List<NCard> _cards = [];
    private readonly List<Label> _labels = [];
    private INetGameService _net = null!;
    private Control _view = null!;
    private Control _contents = null!;
    private TextureButton _exit = null!;
    private NRemoteMouseCursor? _cursor;
    private Texture2D? _eye;
    private ulong? _target;
    private string _contentKey = "";
    private ulong _lastSend;
    private ulong _lastPresence;
    private uint _sequence;
    private bool _failed;
    private readonly HashSet<int> _highlighted = [];

    public static void ContinueAfterCombat(ulong? target) => _continueTarget = target;
    public static void TrackRewards(NRewardsScreen screen, RewardsSet set) { _localRewardsScreen = screen; _localRewardsSet = set; }
    public static void TrackChoices(NCardRewardSelectionScreen screen, IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives)
    {
        _localChoiceScreen = screen; _localOptions = options; _localAlternatives = alternatives;
        _chosen = -1; _choiceHoldUntil = 0;
        _summaryClick = null;
        Current?.SendSnapshot();
    }
    public static void TrackChosen(CardModel card)
    {
        _chosen = _localOptions.ToList().FindIndex(c => c.Card == card);
        _choiceHoldUntil = Time.GetTicksMsec() + 1600;
        Current?.SendSnapshot();
    }
    public static void TrackAlternative(int index)
    {
        _chosen = _localOptions.Count + index;
        _choiceHoldUntil = Time.GetTicksMsec() + 1600;
        Current?.SendSnapshot();
    }
    public static void TrackRewardClick(NRewardButton button)
    {
        if (Current == null || _localRewardsScreen == null) return;
        var buttons = DemoUi.Descendants<NRewardButton>(_localRewardsScreen)
            .Where(b => b.IsVisibleInTree() && b.Reward != null).Take(DemoRewardMessage.MaxOptions).ToList();
        int selected = buttons.IndexOf(button);
        if (selected < 0) return;
        _summaryClick = new DemoRewardMessage
        {
            Kind = DemoRewardKind.Rewards, Cards = [], Labels = buttons.Select(b => b.Reward!.Description.GetFormattedText()).ToList(),
            Chosen = selected, Hovered = selected,
        };
        _summaryHoldUntil = Time.GetTicksMsec() + 1000;
        Current.SendSnapshot();
    }

    public override void _Ready()
    {
        Current = this;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ZIndex = 200;
        _net = RunManager.Instance.NetService;
        _net.RegisterMessageHandler<DemoRewardMessage>(Receive);
        _net.RegisterMessageHandler<DemoPresenceMessage>(ReceivePresence);
        if (RunManager.Instance.RunLobby is { } lobby)
        {
            lobby.RemotePlayerDisconnected += ResetPeer;
            lobby.PlayerRejoined += ResetPeer;
        }
        _view = new Control { MouseFilter = MouseFilterEnum.Stop, Visible = false, Name = "ReadOnlyRewards" };
        AddChild(_view);
        _view.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var back = new ColorRect { Color = new Color(0.025f, 0.035f, 0.04f, .96f), MouseFilter = MouseFilterEnum.Ignore };
        _view.AddChild(back); back.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _contents = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _view.AddChild(_contents); _contents.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _eye = DemoController.EyeTexture ?? DemoUi.LoadEye();
        _exit = DemoUi.Eye(_eye, 66);
        _exit.ZIndex = 500;
        _exit.Pressed += Exit;
        _view.AddChild(_exit);
    }

    public override void _Process(double delta)
    {
        if (_failed || !RunManager.Instance.IsInProgress || NRun.Instance == null) return;
        try
        {
            _eye ??= DemoController.EyeTexture ?? DemoUi.LoadEye();
            ulong now = Time.GetTicksMsec();
            if (CombatManager.Instance.IsInProgress) _continueTarget = null;
            if (now - _lastSend >= 100) { SendSnapshot(); RefreshEyes(now); _lastSend = now; }
            if (now - _lastPresence > 2000 && !CombatManager.Instance.IsInProgress) { SendPresence(); _lastPresence = now; }
            if (_continueTarget is ulong pending && !CombatManager.Instance.IsInProgress
                && _frames.TryGetValue(pending, out var waiting) && waiting.Message.Kind != DemoRewardKind.None)
            { _continueTarget = null; Enter(pending); }
            if (_target is not ulong target) return;
            if (CombatManager.Instance.IsInProgress || !Connected(target)) { ExitBecause("combat resumed or peer disconnected"); return; }
            if (!_frames.TryGetValue(target, out var frame) || now - frame.Seen > 5000) { ExitBecause("reward snapshot timed out"); return; }
            if (frame.Message.Kind == DemoRewardKind.None) { ExitBecause("peer closed reward screen"); return; }
            Render(frame.Message, (float)delta);
            if (_cursor != null)
            {
                _cursor.SetNextPosition(RunManager.Instance.InputSynchronizer.GetControlSpaceFocusPosition(target, this));
                _cursor.UpdateImage(RunManager.Instance.InputSynchronizer.GetMouseDown(target), DrawingMode.None);
            }
        }
        catch (Exception e) { Log.Error($"[Friend View] Rewards observer disabled safely: {e}"); Exit(); _failed = true; }
    }

    private bool Connected(ulong id) => _net.NetId == id || RunManager.Instance.RunLobby?.ConnectedPlayerIds.Contains(id) == true;
    private void ResetPeer(ulong id)
    {
        _frames.Remove(id); _presence.Remove(id);
        if (_target == id) Exit();
    }

    public void FocusAvatarEye(ulong id)
    {
        if (_eyes.TryGetValue(id, out var eye) && eye.Visible && !eye.Disabled) eye.GrabFocus();
    }

    private void Toggle(ulong id)
    {
        if (CombatManager.Instance.IsInProgress)
        {
            if (DemoController.ObservedPlayerId == id) DemoController.Current?.ExitDemo();
            else DemoController.Current?.EnterDemo(id);
        }
        else if (_target == id) Exit();
        else Enter(id);
        RefreshEyes(Time.GetTicksMsec());
    }

    private void RefreshEyes(ulong now)
    {
        if (_eye == null) return;
        bool combat = CombatManager.Instance.IsInProgress;
        bool cardChoices = _target is ulong target && _frames.TryGetValue(target, out var active)
            && active.Message.Kind == DemoRewardKind.CardChoice;
        foreach (var (avatar, original) in _avatarTints)
            if (GodotObject.IsInstanceValid(avatar) && avatar.SelfModulate == ActiveTint) avatar.SelfModulate = original;
        _avatarTints.Clear();
        foreach (var eye in _eyes.Values) eye.Hide();
        foreach (var state in DemoUi.Descendants<NMultiplayerPlayerState>(NRun.Instance!.GlobalUi.MultiplayerPlayerContainer))
        {
            var id = state.Player.NetId;
            if (id == _net.NetId) continue;
            if (!_eyes.TryGetValue(id, out var eye))
            {
                eye = DemoUi.Eye(_eye, 56);
                eye.Name = $"AvatarDemoEye_{id}";
                eye.ZIndex = 500;
                AddChild(eye); _eyes[id] = eye;
                eye.Pressed += () => Toggle(id);
            }
            var avatar = state.GetNode<TextureRect>("%CharacterIcon");
            var inverse = GetGlobalTransformWithCanvas().AffineInverse();
            var row = inverse * state.GlobalPosition;
            var avatarCenter = inverse * (avatar.GetGlobalTransformWithCanvas() * (avatar.Size / 2));
            // Outside the row's labels/health bar, aligned with the avatar, never the creature in the room.
            eye.Position = new(row.X + state.Hitbox.Size.X + 6, avatarCenter.Y - eye.Size.Y / 2);
            bool available = _frames.TryGetValue(id, out var frame) && frame.Message.Kind != DemoRewardKind.None && now - frame.Seen < 3000;
            bool selected = (combat ? DemoController.ObservedPlayerId : _target) == id;
            eye.Visible = state.IsVisibleInTree() && !cardChoices;
            eye.Disabled = !Connected(id) || (!combat && !available && !selected);
            eye.Modulate = selected ? ActiveTint : Colors.White;
            if (selected)
            {
                _avatarTints[avatar] = avatar.SelfModulate;
                avatar.SelfModulate = ActiveTint;
                _exit.TooltipText = state.GetNode<Control>("%NameplateLabel").Get("text").AsString();
            }
            var watchers = _presence.Where(p => p.Value.Message.Watching && p.Value.Message.TargetId == id
                && now - p.Value.Seen < 6500 && Connected(p.Key)).Select(p => p.Key.ToString());
            eye.TooltipText = string.Join(", ", watchers.Prepend(state.Player.Character.Id.ToString()));
        }
    }

    private void SetPlayerListForeground(bool visible)
    {
        var container = NRun.Instance?.GlobalUi.MultiplayerPlayerContainer;
        if (container == null) return;
        if (visible)
        {
            _playerListZ ??= container.ZIndex;
            container.ZIndex = ZIndex + 1;
        }
        else if (_playerListZ is int original)
        {
            container.ZIndex = original;
            _playerListZ = null;
        }
    }

    public bool Enter(ulong id)
    {
        if (id == _net.NetId || !Connected(id) || CombatManager.Instance.IsInProgress || _failed
            || !_frames.TryGetValue(id, out var frame) || frame.Message.Kind == DemoRewardKind.None) return false;
        _continueTarget = null;
        if (_target == id) return true;
        _target = id;
        _contentKey = "";
        _view.Visible = true;
        _cursor?.QueueFree();
        _cursor = NRemoteMouseCursor.Create(id); _view.AddChild(_cursor);
        DemoUi.ReadOnly(_cursor); _cursor.ZIndex = 300;
        _view.Modulate = Colors.Transparent;
        CreateTween().TweenProperty(_view, "modulate:a", 1f, .2);
        Render(frame.Message, 1);
        RefreshEyes(Time.GetTicksMsec());
        if (_exit.Visible) _exit.GrabFocus(); else FocusAvatarEye(id);
        SendPresence();
        NGame.Instance?.RemoteCursorContainer.ForceUpdateAllCursors();
        Log.Info($"[Friend View] Enter rewards: {id}; options={frame.Message.Cards?.Count ?? 0}.");
        return true;
    }

    public void Exit()
    {
        _continueTarget = null;
        if (_target == null) return;
        _target = null; _view.Hide(); Clear(); _cursor?.QueueFree(); _cursor = null;
        SetPlayerListForeground(false);
        RefreshEyes(Time.GetTicksMsec());
        SendPresence();
        NGame.Instance?.RemoteCursorContainer.ForceUpdateAllCursors();
        Log.Info("[Friend View] Exit rewards; original reward screen untouched.");
    }
    private void ExitBecause(string reason)
    {
        Log.Info($"[Friend View] Closing reward observation: {reason}.");
        Exit();
    }

    public override void _Input(InputEvent input)
    {
        if (_target == null) return;
        if (input.IsActionPressed("ui_cancel") || input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape })
        { Exit(); GetViewport().SetInputAsHandled(); }
        else if (input is InputEventKey or InputEventJoypadButton or InputEventJoypadMotion)
            GetViewport().SetInputAsHandled();
    }

    private void Render(DemoRewardMessage frame, float delta)
    {
        string key = $"{frame.Kind}:{string.Join('|', frame.Cards!.Select(c => $"{c.Id}:{c.CurrentUpgradeLevel}:{c.Enchantment}"))}:{string.Join('|', frame.Labels!)}";
        if (_contentKey != key)
        {
            Clear(); _contentKey = key;
            var target = RunManager.Instance.DebugOnlyGetState()!.GetPlayer(_target!.Value)!;
            foreach (var serialized in frame.Cards!)
            {
                // Detached display model. Never RunState.AddCard, CardScope, reward.Select or RNG.
                var display = CardModel.FromSerializable(serialized);
                display.Owner = target;
                var card = NCard.Create(display);
                if (card == null) continue;
                _contents.AddChild(card); DemoUi.ReadOnly(card);
                card.UpdateVisuals(PileType.None, CardPreviewMode.Normal); _cards.Add(card);
            }
            // Alternatives such as Skip belong to the friend's controls, not the observer's UI.
            foreach (var text in frame.Kind == DemoRewardKind.CardChoice ? [] : frame.Labels!)
            {
                var label = DemoUi.Label(DemoUi.Plain(text), 27);
                _contents.AddChild(label); _labels.Add(label);
            }
        }
        float width = Math.Max(1, Size.X), height = Math.Max(1, Size.Y);
        float spacing = Math.Min(350, (width - 200) / Math.Max(1, _cards.Count));
        for (int i = 0; i < _cards.Count; i++)
        {
            bool focused = frame.Chosen >= 0 ? frame.Chosen == i : frame.Hovered == i;
            var position = new Vector2(width / 2 + (i - (_cards.Count - 1) / 2f) * spacing, height / 2 - (focused ? 28 : 0));
            _cards[i].Position = _cards[i].Position.Lerp(position, 1 - MathF.Exp(-16 * delta));
            _cards[i].Scale = _cards[i].Scale.Lerp(Vector2.One * (focused ? 1.04f : .9f), 1 - MathF.Exp(-16 * delta));
            _cards[i].ZIndex = focused ? 20 : i;
            _cards[i].Modulate = frame.Chosen >= 0 && frame.Chosen != i ? new Color(1, 1, 1, .4f) : Colors.White;
            if (focused && _highlighted.Add(i)) _cards[i].CardHighlight.AnimShow();
            else if (!focused && _highlighted.Remove(i)) _cards[i].CardHighlight.AnimHide();
        }
        for (int i = 0; i < _labels.Count; i++)
        {
            int option = _cards.Count + i;
            _labels[i].Position = new(width / 2 - 220, _cards.Count == 0 ? height * .30f + i * 60 : height * .80f + i * 42);
            _labels[i].Modulate = frame.Chosen == option || frame.Hovered == option ? DemoUi.Accent : Colors.White;
        }
        _exit.Visible = frame.Kind == DemoRewardKind.CardChoice;
        _exit.Modulate = ActiveTint;
        float cardsRight = width / 2 + Math.Max(0, _cards.Count - 1) * spacing / 2 + 160;
        _exit.Position = new(Math.Clamp(cardsRight + 30, 0, width - _exit.Size.X - 24), height / 2 - _exit.Size.Y / 2);
        SetPlayerListForeground(true);
    }

    private void SendSnapshot()
    {
        if (!_net.IsConnected || !RunManager.Instance.IsInProgress || RunManager.Instance.DebugOnlyGetState()!.Players.Count < 2) return;
        var frame = new DemoRewardMessage
        {
            ProtocolVersion = 1, Sequence = ++_sequence, Location = RunManager.Instance.DebugOnlyGetState()!.RunLocation,
            Kind = DemoRewardKind.None, Hovered = -1, Chosen = -1, Cards = [], Labels = [],
        };
        bool choicesOpen = !IsViewing && GodotObject.IsInstanceValid(_localChoiceScreen) && _localChoiceScreen!.IsInsideTree();
        if (!IsViewing && (choicesOpen || _choiceHoldUntil > Time.GetTicksMsec()))
        {
            frame.Kind = DemoRewardKind.CardChoice;
            frame.Cards = _localOptions.Take(DemoRewardMessage.MaxOptions).Select(c => c.Card.ToSerializable()).ToList();
            frame.Labels = _localAlternatives.Select(a => a.Title.GetFormattedText()).Take(DemoRewardMessage.MaxOptions).ToList();
            frame.Chosen = _chosen;
            if (choicesOpen)
            {
                var holders = DemoUi.Descendants<NGridCardHolder>(_localChoiceScreen!).ToList();
                for (int i = 0; i < holders.Count; i++)
                    if (holders[i].HasFocus() || holders[i].Hitbox.GetGlobalRect().HasPoint(GetGlobalMousePosition())) { frame.Hovered = i; break; }
            }
        }
        else if (!IsViewing && GodotObject.IsInstanceValid(_localRewardsScreen) && _localRewardsScreen!.IsInsideTree() && !_localRewardsScreen.IsComplete && _localRewardsSet != null)
        {
            frame.Kind = DemoRewardKind.Rewards;
            var buttons = DemoUi.Descendants<NRewardButton>(_localRewardsScreen).Where(b => b.IsVisibleInTree() && b.Reward != null).Take(DemoRewardMessage.MaxOptions).ToList();
            frame.Labels = buttons.Select(b => b.Reward!.Description.GetFormattedText()).ToList();
            for (int i = 0; i < buttons.Count; i++)
                if (buttons[i].HasFocus() || buttons[i].GetGlobalRect().HasPoint(GetGlobalMousePosition())) { frame.Hovered = i; break; }
        }
        if (!IsViewing && frame.Kind != DemoRewardKind.CardChoice && _summaryHoldUntil > Time.GetTicksMsec() && _summaryClick is { } summary)
        {
            frame.Kind = summary.Kind; frame.Labels = summary.Labels; frame.Cards = [];
            frame.Hovered = summary.Hovered; frame.Chosen = summary.Chosen;
        }
        _net.SendMessage(frame);
    }

    private void Receive(DemoRewardMessage frame, ulong sender)
    {
        if (sender == _net.NetId || !Connected(sender) || frame.ProtocolVersion != 1
            || frame.Location != RunManager.Instance.DebugOnlyGetState()?.RunLocation
            || frame.Kind > DemoRewardKind.CardChoice || frame.Cards == null || frame.Labels == null) return;
        if (_frames.TryGetValue(sender, out var previous) && !DemoSession.Newer(frame.Sequence, previous.Message.Sequence)) return;
        _frames[sender] = new(frame, Time.GetTicksMsec());
    }
    private void ReceivePresence(DemoPresenceMessage frame, ulong sender)
    {
        if (sender == _net.NetId || !Connected(sender) || frame.ProtocolVersion != 1
            || frame.Location != RunManager.Instance.DebugOnlyGetState()?.RunLocation) return;
        if (_presence.TryGetValue(sender, out var previous) && !DemoSession.Newer(frame.Sequence, previous.Message.Sequence)) return;
        _presence[sender] = (frame, Time.GetTicksMsec());
    }
    private void SendPresence()
    {
        var run = RunManager.Instance.DebugOnlyGetState();
        if (run == null || CombatManager.Instance.IsInProgress || !_net.IsConnected || DemoController.IsViewing) return;
        _net.SendMessage(new DemoPresenceMessage
        {
            ProtocolVersion = 1, Sequence = DemoPresenceSequence.Next(), Location = run.RunLocation,
            Watching = _target.HasValue, TargetId = _target ?? 0,
        });
    }
    private void Clear()
    {
        foreach (var child in _contents.GetChildren()) { _contents.RemoveChild(child); child.QueueFree(); }
        _cards.Clear(); _labels.Clear(); _contentKey = "";
        _highlighted.Clear();
    }
    public override void _ExitTree()
    {
        Exit(); _net.UnregisterMessageHandler<DemoRewardMessage>(Receive);
        _net.UnregisterMessageHandler<DemoPresenceMessage>(ReceivePresence);
        if (RunManager.Instance.RunLobby is { } lobby)
        {
            lobby.RemotePlayerDisconnected -= ResetPeer;
            lobby.PlayerRejoined -= ResetPeer;
        }
        if (Current == this) Current = null;
        _localChoiceScreen = null; _localRewardsScreen = null; _localRewardsSet = null;
        _localOptions = []; _localAlternatives = []; _continueTarget = null;
        _summaryClick = null; _summaryHoldUntil = 0;
    }
}

[HarmonyPatch(typeof(NRun), nameof(NRun._Ready))]
internal static class AttachRewardDemo
{
    static void Postfix(NRun __instance) => Callable.From(() =>
    { if (GodotObject.IsInstanceValid(__instance) && __instance.IsInsideTree()) __instance.GlobalUi.AddChild(new RewardDemoController { Name = "TogetherRewardDemo" }); }).CallDeferred();
}
[HarmonyPatch(typeof(NRewardsScreen), nameof(NRewardsScreen.ShowScreen))]
internal static class TrackRewardScreen
{
    static void Postfix(NRewardsScreen __result, RewardsSet set) => RewardDemoController.TrackRewards(__result, set);
}
[HarmonyPatch(typeof(NCardRewardSelectionScreen), nameof(NCardRewardSelectionScreen.RefreshOptions))]
internal static class TrackRewardOptions
{
    static void Postfix(NCardRewardSelectionScreen __instance, IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> extraOptions)
        => RewardDemoController.TrackChoices(__instance, options, extraOptions);
}
[HarmonyPatch(typeof(NCardRewardSelectionScreen), "SelectCard")]
internal static class TrackRewardChosen
{
    static bool Prefix(NCardHolder cardHolder)
    { if (RewardDemoController.IsViewing) return false; if (cardHolder.CardModel != null) RewardDemoController.TrackChosen(cardHolder.CardModel); return true; }
}
[HarmonyPatch(typeof(NCardRewardSelectionScreen), "OnAlternateRewardSelected")]
internal static class TrackRewardSkipped
{
    static bool Prefix(int index)
    { if (RewardDemoController.IsViewing) return false; RewardDemoController.TrackAlternative(index); return true; }
}
[HarmonyPatch(typeof(NRewardsScreen), "OnProceedButtonPressed")]
internal static class BlockLocalRewardProceed
{
    static bool Prefix() => !RewardDemoController.IsViewing;
}

[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Rewards.NRewardButton), "GetReward")]
internal static class BlockLocalRewardClaim
{
    static bool Prefix(NRewardButton __instance, ref Task __result)
    {
        if (!RewardDemoController.IsViewing) { RewardDemoController.TrackRewardClick(__instance); return true; }
        __result = Task.CompletedTask;
        return false;
    }
}
