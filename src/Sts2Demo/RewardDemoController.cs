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
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.ControllerInput;
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
    private Tween? _viewTween;
    private readonly HashSet<int> _highlighted = [];
    private Control? _upgradeRow;
    private NTickbox? _upgradeTickbox;
    private bool _viewUpgraded;
    private List<MegaCrit.Sts2.Core.Saves.Runs.SerializableCard> _displayOptions = [];
    private TextureRect? _nativeImage;
    private ImageTexture? _nativeTexture;
    private DemoScreenAssembly _screenAssembly = new();
    private ulong _lastCapture;
    private ulong _lastNativeFrame;
    private bool _capturePending;
    private uint _screenSequence;
    private Vector2 _nativeCursor;
    private bool _nativeMouseDown;
    private ulong _enteredAt;
    private Vector2 _imageOrigin;
    private Vector2 _imageSize = Vector2.One;
    private readonly HashSet<StringName> _ownedPresses = [];
    private Control? _returnFocus;
    private TextureRect _exitHint = null!;

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
        _net.RegisterMessageHandler<DemoScreenMessage>(ReceiveScreen);
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
        _exitHint = DemoUi.ControllerHint(_exit);
    }

    public override void _Process(double delta)
    {
        if (_failed || !RunManager.Instance.IsInProgress || NRun.Instance == null) return;
        try
        {
            _eye ??= DemoController.EyeTexture ?? DemoUi.LoadEye();
            ulong now = Time.GetTicksMsec();
            if (!CombatManager.Instance.IsInProgress && !LocalScreenAllowed())
            {
                _continueTarget = null;
                if (_target != null) ExitBecause("local screen is disabled for demo");
            }
            if (CombatManager.Instance.IsInProgress) _continueTarget = null;
            if (now - _lastSend >= 100) { SendSnapshot(); RefreshEyes(now); _lastSend = now; }
            RefreshControllerHints();
            if (now - _lastPresence > 2000 && !CombatManager.Instance.IsInProgress) { SendPresence(); _lastPresence = now; }
            if (now - _lastCapture >= 250 && !_capturePending && ShouldCapture(now))
            { _lastCapture = now; _ = CaptureNativeScreen(); }
            if (_continueTarget is ulong pending && !CombatManager.Instance.IsInProgress
                && _frames.TryGetValue(pending, out var waiting) && waiting.Message.Kind != DemoRewardKind.None)
            { _continueTarget = null; Enter(pending); }
            if (_target is not ulong target) return;
            if (CombatManager.Instance.IsInProgress || !Connected(target)) { ExitBecause("combat resumed or peer disconnected"); return; }
            if (!_frames.TryGetValue(target, out var frame) || now - frame.Seen > 5000) { ExitBecause("reward snapshot timed out"); return; }
            if (frame.Message.Location != RunManager.Instance.DebugOnlyGetState()?.RunLocation) { ExitBecause("room changed"); return; }
            if (frame.Message.Kind == DemoRewardKind.None) { ExitBecause("peer closed reward screen"); return; }
            Render(frame.Message, (float)delta);
            bool imageView = frame.Message.Kind is DemoRewardKind.Activity or DemoRewardKind.Rewards;
            if (imageView && now - (_lastNativeFrame == 0 ? _enteredAt : _lastNativeFrame) > 5000)
            { ExitBecause("native UI stream timed out"); return; }
            if (_cursor != null)
            {
                _cursor.Visible = !imageView || _lastNativeFrame != 0;
                _cursor.SetNextPosition(imageView ? _nativeCursor * Size : RunManager.Instance.InputSynchronizer.GetControlSpaceFocusPosition(target, this));
                _cursor.UpdateImage(imageView ? _nativeMouseDown : RunManager.Instance.InputSynchronizer.GetMouseDown(target), DrawingMode.None);
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
        if (NControllerManager.Instance?.IsUsingController != true) return;
        var creature = CombatManager.Instance.IsInProgress
            ? NCombatRoom.Instance?.CreatureNodes.FirstOrDefault(c => c.Entity.Player?.NetId == id) : null;
        if (creature?.Hitbox.IsVisibleInTree() == true) creature.Hitbox.GrabFocus();
        else if (_eyes.TryGetValue(id, out var eye) && eye.Visible && !eye.Disabled) eye.GrabFocus();
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
        var seen = new HashSet<ulong>();
        foreach (var state in DemoUi.Descendants<NMultiplayerPlayerState>(NRun.Instance!.GlobalUi.MultiplayerPlayerContainer))
        {
            var id = state.Player.NetId;
            if (id == _net.NetId) continue;
            seen.Add(id);
            if (!_eyes.TryGetValue(id, out var eye))
            {
                eye = DemoUi.Eye(_eye, 66);
                eye.Name = $"AvatarDemoEye_{id}";
                eye.ZIndex = 500;
                AddChild(eye); _eyes[id] = eye;
                eye.Pressed += () => Toggle(id);
                DemoUi.ControllerHint(eye);
            }
            var avatar = state.GetNode<TextureRect>("%CharacterIcon");
            var inverse = GetGlobalTransformWithCanvas().AffineInverse();
            var row = inverse * state.GlobalPosition;
            var avatarCenter = inverse * (avatar.GetGlobalTransformWithCanvas() * (avatar.Size / 2));
            // Anchor to the fixed row layout, not its animated hover hitbox.
            var health = state.GetNode<MegaCrit.Sts2.Core.Nodes.Combat.NHealthBar>("%HealthBar").HpBarContainer;
            float statusRight = (inverse * (health.GetGlobalTransformWithCanvas() * new Vector2(health.Size.X, 0))).X;
            eye.Position = new(statusRight + 14, avatarCenter.Y - eye.Size.Y / 2);
            var creature = combat ? NCombatRoom.Instance?.GetCreatureNode(state.Player.Creature) : null;
            if (creature != null)
            {
                var top = creature.GetTopOfHitbox();
                // Do not overlap the native miniature card/intent above the creature.
                top.Y = Math.Min(top.Y, creature.IntentContainer.GlobalPosition.Y);
                eye.Position = inverse * top - new Vector2(eye.Size.X / 2, eye.Size.Y + 18);
                eye.Position = new(eye.Position.X, Math.Max(100, eye.Position.Y));
            }
            bool available = _frames.TryGetValue(id, out var frame) && frame.Message.Kind != DemoRewardKind.None && now - frame.Seen < 3000
                && frame.Message.Location == RunManager.Instance.DebugOnlyGetState()?.RunLocation;
            bool selected = (combat ? DemoController.ObservedPlayerId : _target) == id;
            eye.Visible = combat ? creature?.IsVisibleInTree() == true
                : state.IsVisibleInTree() && !cardChoices && LocalScreenAllowed() && (available || selected);
            eye.Disabled = !Connected(id) || (!combat && !available && !selected);
            eye.SelfModulate = selected ? ActiveTint : Colors.White; // Do not tint the native glyph above it.
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
        foreach (var (id, eye) in _eyes) if (!seen.Contains(id)) eye.Hide();
    }

    private ulong? FocusedFriend()
    {
        var focus = GetViewport().GuiGetFocusOwner();
        if (focus == null || NRun.Instance == null) return null;
        if (_exit.IsVisibleInTree() && focus == _exit) return _target;
        bool combat = CombatManager.Instance.IsInProgress;
        if (combat && (NCombatRoom.Instance is not { } room || !ActiveScreenContext.Instance.IsCurrent(room)
            || NTargetManager.Instance.IsInSelection || room.Ui.Hand.InCardPlay
            || room.Ui.Hand.IsInCardSelection)) return null;
        if (!combat && !LocalScreenAllowed()) return null;
        foreach (var (id, eye) in _eyes)
        {
            if (!eye.IsVisibleInTree() || eye.Disabled) continue;
            if (focus == eye) return id;
            if (combat && NCombatRoom.Instance?.CreatureNodes.Any(c => c.Entity.Player?.NetId == id && c.Hitbox == focus) == true) return id;
            if (!combat && DemoUi.Descendants<NMultiplayerPlayerState>(NRun.Instance.GlobalUi.MultiplayerPlayerContainer)
                .Any(s => s.Player.NetId == id && s.Hitbox == focus)) return id;
        }
        return null;
    }

    private void RefreshControllerHints()
    {
        var focused = FocusedFriend();
        var target = DemoController.ObservedPlayerId ?? _target;
        foreach (var (id, eye) in _eyes)
            DemoUi.UpdateHint(eye.GetNode<TextureRect>("DemoControllerHint"),
                target == id ? MegaInput.cancel : MegaInput.select,
                eye.IsVisibleInTree() && !eye.Disabled && (target == id || focused == id));
        DemoUi.UpdateHint(_exitHint, MegaInput.cancel, _target != null && _exit.IsVisibleInTree());
        // The cloned tickbox uses the native accept binding (Y), not select (A).
        // Native NButton also refreshes it on InputRebound / controller changes.
        if (_upgradeTickbox?.GetNodeOrNull<TextureRect>("%ControllerIcon") is { } upgradeHint)
        {
            upgradeHint.Texture = NInputManager.Instance?.GetHotkeyIcon(MegaInput.accept);
            upgradeHint.Visible = _target != null && NControllerManager.Instance?.IsUsingController == true && upgradeHint.Texture != null;
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
            || !LocalScreenAllowed() || !_frames.TryGetValue(id, out var frame) || frame.Message.Kind == DemoRewardKind.None
            || frame.Message.Location != RunManager.Instance.DebugOnlyGetState()?.RunLocation) return false;
        _continueTarget = null;
        if (_target == id) return true;
        _viewTween?.Kill();
        if (_target == null) _returnFocus = GetViewport().GuiGetFocusOwner();
        _target = id;
        _lastNativeFrame = 0;
        _enteredAt = Time.GetTicksMsec(); _screenAssembly = new();
        _contentKey = "";
        _view.Visible = true;
        _cursor?.QueueFree();
        _cursor = NRemoteMouseCursor.Create(id); _view.AddChild(_cursor);
        DemoUi.ReadOnly(_cursor); _cursor.ZIndex = 300;
        _view.Modulate = Colors.Transparent;
        _viewTween = CreateTween(); _viewTween.TweenProperty(_view, "modulate:a", 1f, .2);
        Render(frame.Message, 1);
        RefreshEyes(Time.GetTicksMsec());
        if (NControllerManager.Instance?.IsUsingController == true)
        { if (_exit.Visible) _exit.GrabFocus(); else FocusAvatarEye(id); }
        SendPresence();
        NGame.Instance?.RemoteCursorContainer.ForceUpdateAllCursors();
        Log.Info($"[Friend View] Enter rewards: {id}; options={frame.Message.Cards?.Count ?? 0}.");
        return true;
    }

    public void Exit()
    {
        _continueTarget = null;
        if (_target == null) return;
        _target = null; _viewTween?.Kill();
        _cursor?.QueueFree(); _cursor = null;
        if (IsInsideTree() && !_failed)
        {
            _viewTween = CreateTween();
            _viewTween.TweenProperty(_view, "modulate:a", 0f, .18).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
            _viewTween.TweenCallback(Callable.From(() => { _view.Hide(); Clear(); }));
        }
        else { _view.Hide(); Clear(); }
        SetPlayerListForeground(false);
        RefreshEyes(Time.GetTicksMsec());
        if (_returnFocus != null && GodotObject.IsInstanceValid(_returnFocus)
            && _returnFocus.IsVisibleInTree() && _returnFocus.FocusMode == FocusModeEnum.All
            && NControllerManager.Instance?.IsUsingController == true) _returnFocus.GrabFocus();
        _returnFocus = null;
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
        if (_failed || NRun.Instance == null) return;
        // A release still belongs to its old press even if settings changed while held.
        foreach (var owned in _ownedPresses.ToArray())
            if (owned != "demo_escape" && input.IsActionReleased(owned))
            { _ownedPresses.Remove(owned); GetViewport().SetInputAsHandled(); return; }
        if (input is InputEventKey { Keycode: Key.Escape } escape)
        {
            if (_target != null || DemoController.IsViewing || _ownedPresses.Contains("demo_escape"))
            {
                if (escape.Pressed && !escape.Echo)
                { _ownedPresses.Add("demo_escape"); CloseObservedView(); }
                else if (!escape.Pressed) _ownedPresses.Remove("demo_escape");
                GetViewport().SetInputAsHandled();
            }
            return;
        }
        if (input is InputEventAction logical && MegaInput.AllInputs.Contains(logical.Action.ToString()))
        {
            if (RouteAction(logical.Action, logical.Pressed, logical.Action)) GetViewport().SetInputAsHandled();
            return;
        }
        // Steam Input emits controller_* actions; native Godot controllers can emit
        // joypad events directly. Read the SAME current mapping as NInputManager.
        // Do not swallow an unmapped raw event: the engine still needs to translate it.
        if (input.IsEcho()
            || !(input is InputEventJoypadButton || input is InputEventAction physical
                && Controller.AllControllerInputs.Contains(physical.Action))) return;
        var mapping = (Dictionary<StringName, StringName>)AccessTools.Field(typeof(NInputManager), "_controllerInputMap")
            .GetValue(NInputManager.Instance)!;
        // Choose ONE owner even if an unusual mapping aliases multiple actions.
        var priority = new[] { MegaInput.cancel, MegaInput.select, MegaInput.accept, MegaInput.left,
            MegaInput.right, MegaInput.up, MegaInput.down }.Concat(MegaInput.AllInputs.Select(s => new StringName(s))).Distinct();
        foreach (var action in priority)
        {
            if (!mapping.TryGetValue(action, out var binding)) continue;
            bool pressed = input.IsActionPressed(binding), released = input.IsActionReleased(binding);
            if (NControllerManager.Instance?.IsUsingController != true && !(released && _ownedPresses.Contains(binding))) continue;
            if ((pressed || released) && RouteAction(action, pressed, binding))
            { GetViewport().SetInputAsHandled(); return; }
        }
    }

    private bool RouteAction(StringName action, bool pressed, StringName pressKey)
    {
        // Own the matching release after exiting: it must not close a local screen,
        // claim a reward or activate a newly restored underlying control.
        if (!pressed && _ownedPresses.Remove(pressKey)) return true;
        if (pressed && _ownedPresses.Contains(pressKey)) return true; // held button is not a second action
        var friend = FocusedFriend();
        var command = DemoInputPolicy.Resolve(action, DemoController.IsViewing, _target != null,
            friend != null, _upgradeTickbox?.IsVisibleInTree() == true);
        if (command == DemoInputAction.None) return false;
        if (!pressed) return true;
        _ownedPresses.Add(pressKey);
        switch (command)
        {
            case DemoInputAction.Exit: CloseObservedView(); break;
            case DemoInputAction.ToggleFriend: Toggle(friend!.Value); break;
            case DemoInputAction.Upgrades:
                AccessTools.Method(typeof(NTickbox), "OnRelease").Invoke(_upgradeTickbox, null); break;
            case DemoInputAction.Navigate: NavigateDemo(action); break;
        }
        RefreshControllerHints();
        return true;
    }

    private void CloseObservedView()
    {
        if (_target != null) Exit(); else DemoController.Current?.ExitDemo();
    }

    private void NavigateDemo(StringName direction)
    {
        var controls = new List<Control>();
        if (DemoController.IsViewing && NCombatRoom.Instance is { } room)
        {
            controls.AddRange(room.CreatureNodes.Where(c => c.Entity.Player is { } p && p.NetId != _net.NetId
                && _eyes.TryGetValue(p.NetId, out var eye) && eye.IsVisibleInTree() && !eye.Disabled)
                .OrderBy(c => c.GlobalPosition.X).Select(c => c.Hitbox));
            if (room.Ui.EndTurnButton.IsEnabled && room.Ui.EndTurnButton.IsVisibleInTree()) controls.Add(room.Ui.EndTurnButton);
        }
        else
        {
            if (_upgradeTickbox?.IsVisibleInTree() == true) controls.Add(_upgradeTickbox);
            if (_exit.IsVisibleInTree()) controls.Add(_exit);
            controls.AddRange(_eyes.Values.Where(e => e.IsVisibleInTree() && !e.Disabled).OrderBy(e => e.GlobalPosition.Y));
        }
        if (controls.Count == 0) return;
        int index = controls.IndexOf(GetViewport().GuiGetFocusOwner());
        int step = direction == MegaInput.left || direction == MegaInput.up ? -1 : 1;
        controls[(index + step + controls.Count) % controls.Count].GrabFocus();
    }

    private void Render(DemoRewardMessage frame, float delta)
    {
        string key = $"{frame.Kind}:{string.Join('|', frame.Cards!.Select(c => $"{c.Id}:{c.CurrentUpgradeLevel}:{c.Enchantment}"))}:{string.Join('|', frame.Labels!)}";
        if (_contentKey != key)
        {
            Clear(); _contentKey = key;
            if (frame.Kind is DemoRewardKind.Activity or DemoRewardKind.Rewards) _enteredAt = Time.GetTicksMsec();
            var target = RunManager.Instance.DebugOnlyGetState()!.GetPlayer(_target!.Value)!;
            _displayOptions = frame.Cards!.ToList();
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
            if (frame.Kind == DemoRewardKind.CardChoice) BuildUpgradeCheckbox();
            // Alternatives such as Skip belong to the friend's controls, not the observer's UI.
            if (frame.Kind is DemoRewardKind.Rewards or DemoRewardKind.Activity)
            {
                _nativeImage = new TextureRect { Name = "NativeFriendScreen", MouseFilter = MouseFilterEnum.Ignore,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale };
                _contents.AddChild(_nativeImage); _nativeImage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
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
        _exit.SelfModulate = ActiveTint;
        float cardsRight = width / 2 + Math.Max(0, _cards.Count - 1) * spacing / 2 + 160;
        _exit.Position = new(Math.Clamp(cardsRight + 30, 0, width - _exit.Size.X - 24), height / 2 - _exit.Size.Y / 2);
        if (_upgradeRow != null) _upgradeRow.Position = new(width / 2 - _upgradeRow.Size.X / 2, height * .79f);
        if (_nativeImage != null) { _nativeImage.Position = _imageOrigin * Size; _nativeImage.Size = _imageSize * Size; }
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
        var activeScreen = ActiveScreenContext.Instance.GetCurrentScreen();
        bool choicesOpen = !IsViewing && activeScreen is NCardRewardSelectionScreen
            && GodotObject.IsInstanceValid(_localChoiceScreen) && _localChoiceScreen!.IsInsideTree();
        if (!IsViewing && (choicesOpen || activeScreen is NRewardsScreen && _choiceHoldUntil > Time.GetTicksMsec()))
        {
            frame.Kind = DemoRewardKind.CardChoice;
            frame.Cards = _localOptions.Take(DemoRewardMessage.MaxOptions).Select(c => c.Card.ToSerializable()).ToList();
            frame.Labels = _localAlternatives.Select(a => a.Title.GetFormattedText()).Take(DemoRewardMessage.MaxOptions).ToList();
            frame.Chosen = _chosen;
            if (choicesOpen)
            {
                var holders = DemoUi.Descendants<NGridCardHolder>(_localChoiceScreen!).ToList();
                // Mouse wins over the default keyboard/controller focus on card 0.
                frame.Hovered = holders.FindIndex(h => h.Hitbox.GetGlobalRect().HasPoint(GetGlobalMousePosition()));
                if (frame.Hovered < 0 && NControllerManager.Instance?.IsUsingController == true)
                    frame.Hovered = holders.FindIndex(h => h.HasFocus() || h.Hitbox.HasFocus());
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
        // Block rest sites, shared relic selection, personal menus and unknown custom
        // screens even when an old reward screen is still retained underneath them.
        if (!LocalScreenAllowed() || IsViewing) frame.Kind = DemoRewardKind.None;
        else if (frame.Kind != DemoRewardKind.CardChoice)
        {
            var screen = ActiveScreenContext.Instance.GetCurrentScreen();
            frame.Kind = screen is NRewardsScreen ? DemoRewardKind.Rewards : DemoRewardKind.Activity;
            frame.Cards = []; frame.Labels = [];
        }
        _net.SendMessage(frame);
    }

    private void Receive(DemoRewardMessage frame, ulong sender)
    {
        if (sender == _net.NetId || !Connected(sender) || frame.ProtocolVersion != 1
            || frame.Location != RunManager.Instance.DebugOnlyGetState()?.RunLocation
            || frame.Kind > DemoRewardKind.Activity || frame.Cards == null || frame.Labels == null) return;
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
        _upgradeRow = null; _upgradeTickbox = null; _viewUpgraded = false; _displayOptions = [];
        _nativeImage = null; _nativeTexture = null; _lastNativeFrame = 0;
    }

    private void BuildUpgradeCheckbox()
    {
        // Duplicate only the native inspect tickbox subtree, never its screen,
        // signals, owner or gameplay handlers. Its own shader animates visuals only.
        var template = PreloadManager.Cache.GetScene("res://scenes/screens/inspect_card_screen.tscn").Instantiate<Control>();
        var source = DemoUi.Descendants<NTickbox>(template).First(t => t.Name == "Upgrade");
        _upgradeTickbox = (NTickbox)source.Duplicate((int)Node.DuplicateFlags.Scripts);
        _upgradeTickbox.Name = "DemoViewUpgrades";
        foreach (var child in DemoUi.Descendants<Node>(_upgradeTickbox)) child.Owner = _upgradeTickbox;
        template.Free();
        _upgradeRow = new Control { Name = "DemoUpgradeRow", Size = new(340, 60), MouseFilter = MouseFilterEnum.Ignore };
        _contents.AddChild(_upgradeRow);
        _upgradeRow.AddChild(_upgradeTickbox);
        _upgradeTickbox.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        _upgradeTickbox.Position = Vector2.Zero;
        _upgradeTickbox.IsTicked = false;
        _upgradeTickbox.Enable();
        _upgradeTickbox.FocusMode = FocusModeEnum.All;
        _upgradeTickbox.Connect(NTickbox.SignalName.Toggled, Callable.From<NTickbox>(_ =>
        {
            _viewUpgraded = _upgradeTickbox.IsTicked;
            RefreshUpgradePreviews();
        }));
        foreach (var label in DemoUi.Descendants<Label>(_upgradeTickbox))
            label.Text = new LocString("card_selection", "VIEW_UPGRADES").GetFormattedText();
    }

    private void RefreshUpgradePreviews()
    {
        var owner = RunManager.Instance.DebugOnlyGetState()!.GetPlayer(_target!.Value)!;
        for (int i = 0; i < _cards.Count; i++)
        {
            var display = CardModel.FromSerializable(_displayOptions[i]);
            display.Owner = owner;
            if (_viewUpgraded && !display.IsUpgraded && display.IsUpgradable)
            {
                display.UpgradePreviewType = CardUpgradePreviewType.Deck;
                display.UpgradeInternal();
            }
            _cards[i].Model = display;
            if (_viewUpgraded) _cards[i].ShowUpgradePreview();
            else _cards[i].UpdateVisuals(PileType.None, CardPreviewMode.Normal);
        }
    }
    public override void _ExitTree()
    {
        Exit(); _net.UnregisterMessageHandler<DemoRewardMessage>(Receive);
        _net.UnregisterMessageHandler<DemoPresenceMessage>(ReceivePresence);
        _net.UnregisterMessageHandler<DemoScreenMessage>(ReceiveScreen);
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

    private static bool LocalScreenAllowed()
    {
        var screen = ActiveScreenContext.Instance.GetCurrentScreen();
        return screen?.GetType().Assembly == typeof(NRun).Assembly
            && DemoScreenPolicy.Allowed(screen.GetType().Name, NRun.Instance?.RestSiteRoom != null, CombatManager.Instance.IsInProgress,
                NRun.Instance?.EventRoom?.Layout is MegaCrit.Sts2.Core.Nodes.Events.NAncientEventLayout);
    }

    private bool ShouldCapture(ulong now) => !IsViewing && !DemoController.IsViewing && LocalScreenAllowed()
        && ActiveScreenContext.Instance.GetCurrentScreen() is not NCardRewardSelectionScreen
        && _presence.Any(p => Connected(p.Key) && p.Value.Message.Watching
            && p.Value.Message.TargetId == _net.NetId && now - p.Value.Seen < 6500);

    private async Task CaptureNativeScreen()
    {
        _capturePending = true;
        try
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (!IsInsideTree() || !ShouldCapture(Time.GetTicksMsec())) return;
            var run = RunManager.Instance.DebugOnlyGetState();
            if (run == null || !_net.IsConnected) return;
            // Only the game's viewport, never the desktop, another app or a file.
            using var image = GetViewport().GetTexture().GetImage();
            if (image.IsEmpty()) return;
            var reference = GetViewport().GetVisibleRect().Size;
            var bounds = new Rect2(Vector2.Zero, reference);
            // Rewards: copy the native window only. Proceed/Skip is a sibling outside
            // this subtree and must not become a second observer control or image.
            if (ActiveScreenContext.Instance.GetCurrentScreen() is NRewardsScreen rewards)
                bounds = rewards.GetNode<Control>("Rewards").GetGlobalRect().Intersection(bounds);
            else
            {
                // Player rows are ordered local-player-first. Keep the observer's
                // native list and real eye controls, rather than duplicate the peer's
                // differently ordered list (including non-clickable baked-in eyes).
                Rect2? playerPanel = null;
                foreach (var row in DemoUi.Descendants<NMultiplayerPlayerState>(NRun.Instance!.GlobalUi.MultiplayerPlayerContainer))
                    if (row.IsVisibleInTree()) playerPanel = playerPanel?.Merge(row.Hitbox.GetGlobalRect()) ?? row.Hitbox.GetGlobalRect();
                foreach (var eye in _eyes.Values)
                    if (eye.IsVisibleInTree()) playerPanel = playerPanel?.Merge(eye.GetGlobalRect()) ?? eye.GetGlobalRect();
                if (playerPanel is Rect2 panel)
                {
                    panel = panel.Grow(12).Intersection(bounds);
                    var factor = new Vector2(image.GetWidth(), image.GetHeight()) / reference;
                    image.FillRect(new Rect2I((Vector2I)(panel.Position * factor), (Vector2I)(panel.Size * factor)), new Color(.025f, .035f, .04f, 1));
                }
            }
            var origin = bounds.Position / reference; var size = bounds.Size / reference;
            if (!DemoScreenMessage.ValidImageRect(origin, size)) return;
            var pixels = new Rect2I((Vector2I)(origin * new Vector2(image.GetWidth(), image.GetHeight())),
                (Vector2I)(size * new Vector2(image.GetWidth(), image.GetHeight())));
            if (pixels.Size.X <= 0 || pixels.Size.Y <= 0) return;
            using var capture = image.GetRegion(pixels);
            capture.Convert(Image.Format.Rgb8);
            float fit = Math.Min(1, Math.Min(1280f / capture.GetWidth(), 720f / capture.GetHeight()));
            if (fit < 1) capture.Resize(Math.Max(1, (int)(capture.GetWidth() * fit)), Math.Max(1, (int)(capture.GetHeight() * fit)), Image.Interpolation.Lanczos);
            var data = capture.SaveJpgToBuffer(.88f);
            if (data.Length > DemoScreenMessage.MaxFrameBytes) data = capture.SaveJpgToBuffer(.65f);
            if (data.Length == 0 || data.Length > DemoScreenMessage.MaxFrameBytes) return;
            byte count = (byte)((data.Length + DemoScreenMessage.ChunkBytes - 1) / DemoScreenMessage.ChunkBytes);
            uint sequence = ++_screenSequence;
            Vector2 cursor = GetViewport().GetMousePosition() / reference;
            for (byte i = 0; i < count; i++)
            {
                int offset = i * DemoScreenMessage.ChunkBytes;
                _net.SendMessage(new DemoScreenMessage { ProtocolVersion = DemoScreenMessage.Protocol, Sequence = sequence, Location = run.RunLocation,
                    Index = i, Count = count, Cursor = cursor, MouseDown = Input.IsMouseButtonPressed(MouseButton.Left),
                    ImageOrigin = origin, ImageSize = size,
                    Data = data.AsSpan(offset, Math.Min(DemoScreenMessage.ChunkBytes, data.Length - offset)).ToArray() });
            }
        }
        catch (Exception e) { Log.Warn($"[Friend View] Native UI frame skipped: {e.Message}"); }
        finally { _lastCapture = Time.GetTicksMsec(); _capturePending = false; }
    }

    private void ReceiveScreen(DemoScreenMessage message, ulong sender)
    {
        if (_target != sender || !Connected(sender) || message.ProtocolVersion != DemoScreenMessage.Protocol
            || message.Location != RunManager.Instance.DebugOnlyGetState()?.RunLocation
            || !_frames.TryGetValue(sender, out var frame) || frame.Message.Kind is not (DemoRewardKind.Activity or DemoRewardKind.Rewards)) return;
        var data = _screenAssembly.Accept(message, Time.GetTicksMsec());
        if (data == null || !DemoScreenAssembly.SafeJpeg(data) || _nativeImage == null) return;
        using var image = new Image();
        if (image.LoadJpgFromBuffer(data) != Error.Ok) return;
        if (_nativeTexture != null && _nativeTexture.GetWidth() == image.GetWidth() && _nativeTexture.GetHeight() == image.GetHeight()) _nativeTexture.Update(image);
        else { _nativeTexture = ImageTexture.CreateFromImage(image); _nativeImage.Texture = _nativeTexture; }
        _nativeImage.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
        _imageOrigin = message.ImageOrigin; _imageSize = message.ImageSize;
        _nativeImage.Position = _imageOrigin * Size; _nativeImage.Size = _imageSize * Size;
        _nativeCursor = message.Cursor; _nativeMouseDown = message.MouseDown; _lastNativeFrame = Time.GetTicksMsec();
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
