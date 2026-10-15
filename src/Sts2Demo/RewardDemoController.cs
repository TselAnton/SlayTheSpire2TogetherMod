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
    private sealed record RemoteFrame(DemoRewardMessage Message, ulong Seen, string ContentKey);
    public static RewardDemoController? Current { get; private set; }
    public static bool IsViewing => Current?._target != null;
    public static ulong? ObservedPlayerId => Current?._target;
    public static float ChromeOpacity => IsViewing && !DemoTopBar.UtilityOpen ? Current?._nativeImage?.ChromeOpacity ?? 1 : 1;
    private static ulong? _continueTarget;
    private static ulong _continueUntil;
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
    private Color? _playerListTint;
    private readonly List<NCard> _cards = [];
    private readonly List<Label> _labels = [];
    private INetGameService _net = null!;
    private Control _view = null!;
    private Control _contents = null!;
    private TextureButton _exit = null!;
    private NRemoteMouseCursor? _cursor;
    private DemoTargeting _targeting = null!;
    private Texture2D? _eye;
    private ulong? _target;
    private ulong? _loadingTarget;
    private ulong _loadingUntil;
    private string _contentKey = "";
    private ulong _lastSend;
    private ulong _lastPresence;
    private uint _sequence;
    private bool _failed;
    private ulong _publishedRoot;
    private ulong _unavailableRoot;
    private bool _openedMap;
    private bool _avatarExitPressed;
    private bool _utilityWasOpen;
    private int? _transitionZ;
    public static bool OpenedObserverMap => Current?._openedMap == true;
    private Tween? _viewTween;
    private readonly HashSet<int> _highlighted = [];
    private Control? _upgradeRow;
    private NTickbox? _upgradeTickbox;
    private bool _viewUpgraded;
    private List<MegaCrit.Sts2.Core.Saves.Runs.SerializableCard> _displayOptions = [];
    private DemoVisualProjection? _nativeImage;
    private DemoRewardsWindow? _rewardsWindow;
    private Dictionary<string, DemoVisual> _publishedVisuals = [];
    private string _publishedScreen = "";
    private Vector2 _publishedCursor;
    private uint _publishedBase;
    private ulong _lastFullMetadata;
    private bool _forceFullMetadata;
    private ulong _lastAvailability;
    private string _availabilityKey = "";
    private string _rewardDetailsKey = "";
    private ulong _lastRewardDetails;
    private DemoRewardMessage? _lastNonMapFrame;
    private DemoScreenAssembly _screenAssembly = new();
    private ulong _lastCapture;
    private ulong _lastNativeFrame;
    private uint _screenSequence;
    private Vector2 _nativeCursor;
    private bool _nativeMouseDown;
    private ulong _enteredAt;
    private readonly HashSet<StringName> _ownedPresses = [];
    private Control? _returnFocus;
    private TextureRect _exitHint = null!;

    public static void ContinueAfterCombat(ulong? target) { _continueTarget = target; _continueUntil = Time.GetTicksMsec() + 10000; }
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
        _view = new Control { MouseFilter = MouseFilterEnum.Ignore, Visible = false, Name = "ReadOnlyRewards" };
        AddChild(_view);
        _view.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var back = new ColorRect { Color = new Color(0.025f, 0.035f, 0.04f, 1f), MouseFilter = MouseFilterEnum.Stop };
        _view.AddChild(back); back.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        back.OffsetTop = 80; // Keep the original top-bar mouse/controller controls reachable.
        _contents = new Control { MouseFilter = MouseFilterEnum.Ignore };
        _view.AddChild(_contents); _contents.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _eye = DemoController.EyeTexture ?? DemoUi.LoadEye();
        _exit = DemoUi.Eye(_eye, 48);
        _exit.ZIndex = 500;
        _exit.Pressed += Exit;
        _view.AddChild(_exit);
        _exitHint = DemoUi.ControllerHint(_exit);
        _targeting = new DemoTargeting { Name = "FriendPotionTarget", Visible = false };
        _view.AddChild(_targeting);
    }

    public override void _Process(double delta)
    {
        if (_failed || !RunManager.Instance.IsInProgress || NRun.Instance == null) return;
        try
        {
            _eye ??= DemoController.EyeTexture ?? DemoUi.LoadEye();
            ulong now = Time.GetTicksMsec();
            if (NRun.Instance.TreasureRoom != null)
            { Exit(); DemoController.Current?.ExitDemo(); _continueTarget = null; }
            UpdateTransitionOpacity();
            if (_loadingTarget is ulong loading)
            {
                if (CanObserve(loading)) { _loadingTarget = null; Enter(loading); }
                else if (now > _loadingUntil || !Connected(loading) || !LocalScreenAllowed()) _loadingTarget = null;
            }
            if (!ObservationContextAllowed() && !DemoTopBar.UtilityOpen)
            {
                _continueTarget = null;
                if (_target != null) ExitBecause("local screen is disabled for demo");
            }
            if (now - _lastSend >= 100) { SendSnapshot(); RefreshEyes(now); _lastSend = now; }
            RefreshControllerHints();
            if (now - _lastPresence > 2000) { SendPresence(); _lastPresence = now; }
            if (now - _lastCapture >= 500 && ShouldCapture(now))
            { _lastCapture = now; CaptureNativeScreen(); }
            if (_continueTarget is ulong pending)
            {
                if (now > _continueUntil || !Connected(pending)) _continueTarget = null;
                else if (CanObserveActivity(pending)) { _continueTarget = null; Enter(pending); }
                else if (CombatManager.Instance.IsInProgress && CanObserve(pending))
                { _continueTarget = null; DemoController.Current?.EnterDemo(pending); }
            }
            if (_target is not ulong target) return;
            _view.Visible = !DemoTopBar.UtilityOpen;
            // Keep the latest packets, but leave local inspection/settings in charge.
            if (DemoTopBar.UtilityOpen) { _utilityWasOpen = true; return; }
            if (_utilityWasOpen) { _utilityWasOpen = false; SendPresence(requestSnapshot: true); }
            if (!Connected(target)) { ExitBecause("peer disconnected"); return; }
            if (!_frames.TryGetValue(target, out var frame) || now - frame.Seen > 5000) { ExitBecause("reward snapshot timed out"); return; }
            if (frame.Message.Location != RunManager.Instance.DebugOnlyGetState()?.RunLocation)
            {
                ExitBecause("room changed");
                if (DemoSettings.AllActivities && !DemoSettings.AutoExitRoom) ContinueAfterCombat(target);
                return;
            }
            if (DemoSettings.AutoExitRoom && frame.Message.RoomComplete)
            { ExitBecause("watched player can proceed"); return; }
            if (frame.Message.Kind == DemoRewardKind.None)
            {
                bool resumeCombat = DemoSettings.AllActivities && frame.Message.Screen == "NCombatRoom" && CombatManager.Instance.IsInProgress;
                if (resumeCombat && DemoController.Current is { } combat)
                    combat.TransferObservation(() => { ExitBecause("peer closed activity screen"); combat.EnterDemo(target); });
                else ExitBecause("peer closed activity screen");
                return;
            }
            Render(frame.Message, (float)delta, frame.ContentKey);
            _targeting.Render(frame.Message.Targeting, this);
            if (frame.Message.Kind == DemoRewardKind.Map) _view.Hide();
            bool imageView = frame.Message.Kind is DemoRewardKind.Activity or DemoRewardKind.Waiting;
            if (imageView && _lastNativeFrame == 0 && now >= _enteredAt && now - _enteredAt > 5000)
            { ExitBecause("native UI stream timed out"); return; }
            if (_cursor != null)
            {
                _cursor.Visible = !DemoTopBar.UtilityOpen && (!imageView || _lastNativeFrame != 0);
                _cursor.SetNextPosition(frame.Message.Cursor * Size);
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
        if (DemoSettings.SeparateButton && _eyes.TryGetValue(id, out var eye) && eye.Visible && !eye.Disabled) eye.GrabFocus();
        else
        {
            var row = DemoUi.Descendants<NMultiplayerPlayerState>(NRun.Instance!.GlobalUi.MultiplayerPlayerContainer).FirstOrDefault(r => r.Player.NetId == id);
            if (row?.Hitbox.IsVisibleInTree() == true) row.Hitbox.GrabFocus();
        }
    }

    public void Toggle(ulong id)
    {
        if (_target == id) Exit();
        else if (DemoController.ObservedPlayerId == id) DemoController.Current?.ExitDemo();
        else if (CanObserveActivity(id)) Enter(id);
        else if (CombatManager.Instance.IsInProgress)
        {
            if (DemoController.ObservedPlayerId == id) DemoController.Current?.ExitDemo();
            else DemoController.Current?.EnterDemo(id);
        }
        else if (!Enter(id) && Connected(id) && LocalScreenAllowed())
        {
            _loadingTarget = _loadingTarget == id ? null : id;
            _loadingUntil = Time.GetTicksMsec() + 5000;
            SendPresence(requestSnapshot: true);
        }
        RefreshEyes(Time.GetTicksMsec());
    }

    private void RefreshEyes(ulong now)
    {
        if (_eye == null) return;
        var playerList = NRun.Instance!.GlobalUi.MultiplayerPlayerContainer;
        if (ChromeOpacity < 1)
        {
            _playerListTint ??= playerList.Modulate;
            playerList.Modulate = _playerListTint.Value * new Color(1, 1, 1, ChromeOpacity);
        }
        else if (_playerListTint is { } originalTint)
        { playerList.Modulate = originalTint; _playerListTint = null; }
        bool combat = CombatManager.Instance.IsInProgress;
        bool showColumn = DemoSettings.SeparateButton && !DemoTopBar.UtilityOpen;
        foreach (var (avatar, original) in _avatarTints)
            if (GodotObject.IsInstanceValid(avatar) && avatar.SelfModulate == ActiveTint) avatar.SelfModulate = original;
        _avatarTints.Clear();
        var seen = new HashSet<ulong>();
        var rows = DemoUi.Descendants<NMultiplayerPlayerState>(NRun.Instance!.GlobalUi.MultiplayerPlayerContainer).ToList();
        var inverse = GetGlobalTransformWithCanvas().AffineInverse();
        float column = rows.Where(s => s.IsVisibleInTree()).Select(s => {
            var hp = s.GetNode<NHealthBar>("%HealthBar").HpBarContainer;
            return (inverse * (hp.GetGlobalTransformWithCanvas() * new Vector2(hp.Size.X, 0))).X;
        }).DefaultIfEmpty(200).Max() + 18;
        foreach (var state in rows)
        {
            var id = state.Player.NetId;
            if (id == _net.NetId) continue;
            seen.Add(id);
            if (!_eyes.TryGetValue(id, out var eye))
            {
                eye = DemoUi.Eye(_eye, 40);
                eye.Name = $"AvatarDemoEye_{id}";
                eye.ZIndex = 500;
                AddChild(eye); _eyes[id] = eye;
                eye.Pressed += () => Toggle(id);
                DemoUi.ControllerHint(eye);
            }
            var avatar = state.GetNode<TextureRect>("%CharacterIcon");
            var avatarCenter = inverse * (avatar.GetGlobalTransformWithCanvas() * (avatar.Size / 2));
            // Anchor to the fixed row layout, not its animated hover hitbox.
            eye.Position = new(column, avatarCenter.Y - eye.Size.Y / 2);
            _frames.TryGetValue(id, out var frame);
            bool selected = (DemoController.ObservedPlayerId ?? _target) == id;
            eye.Visible = showColumn && state.IsVisibleInTree();
            bool explicitlyUnavailable = RemoteScreenUnavailable(id);
            eye.Disabled = !selected && (!Connected(id) || !ObservationContextAllowed() || explicitlyUnavailable);
            bool loadingState = _loadingTarget == id || _target == id && frame?.Message.Kind == DemoRewardKind.Activity && _lastNativeFrame == 0;
            eye.SelfModulate = loadingState ? new Color(.8f, .65f, 1f) : selected ? ActiveTint : Colors.White;
            // These controls sit above the native overlay so they remain usable.
            // Match its dimming instead of leaving a bright floating eye on black.
            eye.Modulate = new Color(1, 1, 1, Math.Min(ChromeOpacity, NRun.Instance.GlobalUi.Overlays.ScreenCount > 0 ? .55f : 1));
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

    public bool CanObserve(ulong id)
    {
        if (NRun.Instance?.TreasureRoom != null || _failed || id == _net.NetId || !Connected(id) || DemoTopBar.UtilityOpen || RemoteScreenUnavailable(id)) return false;
        if (CanObserveActivity(id)) return true;
        if (CombatManager.Instance.IsInProgress)
            return NCombatRoom.Instance is { } room && ActiveScreenContext.Instance.IsCurrent(room)
                && !NTargetManager.Instance.IsInSelection && !room.Ui.Hand.InCardPlay && !room.Ui.Hand.IsInCardSelection;
        return LocalScreenAllowed() && _frames.TryGetValue(id, out var frame) && frame.Message.Kind != DemoRewardKind.None
            && Time.GetTicksMsec() - frame.Seen < 3000 && frame.Message.Location == RunManager.Instance.DebugOnlyGetState()?.RunLocation;
    }

    public bool RemoteScreenUnavailable(ulong id) => _frames.TryGetValue(id, out var frame)
        && Time.GetTicksMsec() - frame.Seen < 3000 && frame.Message.Location == RunManager.Instance.DebugOnlyGetState()?.RunLocation
        && (frame.Message.Kind == DemoRewardKind.None && frame.Message.Screen != "NCombatRoom"
            || DemoSettings.AutoExitRoom && frame.Message.RoomComplete);

    public bool CanObserveActivity(ulong id) => !_failed && id != _net.NetId && Connected(id)
        && ObservationContextAllowed() && _frames.TryGetValue(id, out var frame) && frame.Message.Kind != DemoRewardKind.None
        && (!DemoSettings.AutoExitRoom || !frame.Message.RoomComplete)
        && Time.GetTicksMsec() - frame.Seen < 3000 && frame.Message.Location == RunManager.Instance.DebugOnlyGetState()?.RunLocation;

    private static bool ObservationContextAllowed() => NRun.Instance?.TreasureRoom == null && (LocalScreenAllowed() || DemoSettings.AllActivities
        && CombatManager.Instance.IsInProgress && NCombatRoom.Instance is { } room && ActiveScreenContext.Instance.IsCurrent(room)
        && !room.Ui.Hand.InCardPlay && !room.Ui.Hand.IsInCardSelection && !NTargetManager.Instance.IsInSelection);

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
            if (!CanObserve(id)) continue;
            if (focus == eye) return id;
            if (!DemoSettings.SeparateButton && DemoUi.Descendants<NMultiplayerPlayerState>(NRun.Instance.GlobalUi.MultiplayerPlayerContainer)
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
        if (id == _net.NetId || !Connected(id) || _failed
            || !ObservationContextAllowed() || !_frames.TryGetValue(id, out var frame) || frame.Message.Kind == DemoRewardKind.None
            || DemoSettings.AutoExitRoom && frame.Message.RoomComplete
            || frame.Message.Location != RunManager.Instance.DebugOnlyGetState()?.RunLocation) return false;
        DemoController.Current?.ExitDemo();
        DemoTopBar.BeginObservation();
        _continueTarget = null;
        _loadingTarget = null;
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
        _view.Modulate = new Color(1, 1, 1, 0);
        _viewTween = CreateTween(); _viewTween.TweenProperty(_view, "modulate:a", 1f, .2);
        Render(frame.Message, 1, frame.ContentKey);
        RefreshEyes(Time.GetTicksMsec());
        if (NControllerManager.Instance?.IsUsingController == true)
        { if (_exit.Visible) _exit.GrabFocus(); else FocusAvatarEye(id); }
        SendPresence(requestSnapshot: true);
        NGame.Instance?.RemoteCursorContainer.ForceUpdateAllCursors();
        Log.Info($"[Friend View] Enter rewards: {id}; options={frame.Message.Cards?.Count ?? 0}.");
        return true;
    }

    public void Exit()
    {
        _continueTarget = null;
        _loadingTarget = null;
        if (_target == null) return;
        DemoController.Current?.StopAutomaticObservation();
        _target = null; _viewTween?.Kill();
        if (_openedMap) { NMapScreen.Instance?.Close(); _openedMap = false; }
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
        if (DemoTopBar.UtilityOpen) return;
        // Modal card selectors disable the native row hitbox. Own avatar exit here
        // before GUI dispatch, matching the independent eye's input ownership.
        if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left } mouse)
        {
            if (!mouse.Pressed && _avatarExitPressed)
            { _avatarExitPressed = false; GetViewport().SetInputAsHandled(); return; }
            var selected = DemoController.ObservedPlayerId ?? _target;
            if (mouse.Pressed && selected.HasValue)
            {
                var row = DemoUi.Descendants<NMultiplayerPlayerState>(NRun.Instance.GlobalUi.MultiplayerPlayerContainer)
                    .FirstOrDefault(r => r.Player.NetId == selected.Value);
                var icon = row?.GetNode<TextureRect>("%CharacterIcon");
                if (icon?.IsVisibleInTree() == true && icon.GetGlobalRect().HasPoint(mouse.Position))
                { _avatarExitPressed = true; CloseObservedView(); GetViewport().SetInputAsHandled(); return; }
            }
        }
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
            if (DemoSettings.SeparateButton)
                controls.AddRange(_eyes.Values.Where(e => e.IsVisibleInTree() && !e.Disabled).OrderBy(e => e.GlobalPosition.Y));
            else
                controls.AddRange(DemoUi.Descendants<NMultiplayerPlayerState>(NRun.Instance!.GlobalUi.MultiplayerPlayerContainer)
                    .Where(s => s.IsVisibleInTree() && CanObserve(s.Player.NetId)).OrderBy(s => s.GlobalPosition.Y).Select(s => s.Hitbox));
            if (room.Ui.EndTurnButton.IsEnabled && room.Ui.EndTurnButton.IsVisibleInTree()) controls.Add(room.Ui.EndTurnButton);
        }
        else
        {
            if (_upgradeTickbox?.IsVisibleInTree() == true) controls.Add(_upgradeTickbox);
            if (_exit.IsVisibleInTree()) controls.Add(_exit);
            if (DemoSettings.SeparateButton)
                controls.AddRange(_eyes.Values.Where(e => e.IsVisibleInTree() && !e.Disabled).OrderBy(e => e.GlobalPosition.Y));
            else
                controls.AddRange(DemoUi.Descendants<NMultiplayerPlayerState>(NRun.Instance!.GlobalUi.MultiplayerPlayerContainer)
                    .Where(s => s.IsVisibleInTree() && CanObserve(s.Player.NetId)).OrderBy(s => s.GlobalPosition.Y).Select(s => s.Hitbox));
        }
        if (controls.Count == 0) return;
        int index = controls.IndexOf(GetViewport().GuiGetFocusOwner());
        int step = direction == MegaInput.left || direction == MegaInput.up ? -1 : 1;
        controls[(index + step + controls.Count) % controls.Count].GrabFocus();
    }

    private void Render(DemoRewardMessage frame, float delta, string key)
    {
        if (_contentKey != key)
        {
            Clear(); _contentKey = key;
            if (frame.Kind is DemoRewardKind.Activity or DemoRewardKind.Waiting) _enteredAt = Time.GetTicksMsec();
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
                card.UpdateVisuals(PileType.None, CardPreviewMode.Normal); card.CardHighlight.AnimHide(); _cards.Add(card);
            }
            if (frame.Kind == DemoRewardKind.CardChoice)
            {
                BuildUpgradeCheckbox();
                var template = PreloadManager.Cache.GetScene("res://scenes/screens/card_selection/card_reward_selection_screen.tscn").Instantiate<Control>();
                var banner = template.GetNode<NCommonBanner>("UI/Banner");
                banner.GetParent().RemoveChild(banner);
                foreach (var child in DemoUi.Descendants<Node>(banner))
                { child.Owner = banner; if (child.Name == "Label") child.UniqueNameInOwner = true; }
                template.Free();
                _contents.AddChild(banner);
                banner.label.SetTextAutoSize(new LocString("gameplay_ui", "CHOOSE_CARD_HEADER").GetRawText());
                banner.AnimateIn(); DemoUi.ReadOnly(banner);
            }
            // Alternatives such as Skip belong to the friend's controls, not the observer's UI.
            if (frame.Kind == DemoRewardKind.Rewards)
            {
                _rewardsWindow = new DemoRewardsWindow { Name = "NativeRewardWindow", Size = Size };
                _contents.AddChild(_rewardsWindow);
                var entries = frame.Entries?.Count > 0 ? frame.Entries
                    : frame.Labels!.Select(text => new DemoRewardEntry("Reward", text, [])).ToList();
                _rewardsWindow.Build(frame.Header ?? new LocString("gameplay_ui", "COMBAT_REWARD_HEADER_LOOT").GetFormattedText(), entries!);
            }
            if (frame.Kind is DemoRewardKind.Activity or DemoRewardKind.Waiting)
            {
                _nativeImage = new DemoVisualProjection { Name = "NativeFriendScreen", MouseFilter = MouseFilterEnum.Ignore };
                _contents.AddChild(_nativeImage);
            }
        }
        if (frame.Kind == DemoRewardKind.Map && !_openedMap)
        {
            var map = NRun.Instance!.GlobalUi.MapScreen;
            if (!map.IsOpen)
            {
                map.Open(isOpenedFromTopBar: true); _openedMap = true;
                // Open recalculates map-point visuals. Reapply authoritative votes;
                // never cast/cancel a vote or regenerate the map for observation.
                AccessTools.Method(typeof(NMapScreen), "InitMapVotes").Invoke(map, null);
                map.RefreshAllMapPointVotes();
            }
        }
        else if (frame.Kind != DemoRewardKind.Map && _openedMap)
        { NRun.Instance!.GlobalUi.MapScreen.Close(); _openedMap = false; }
        if (_nativeImage != null) _nativeImage.RemoteCursor = frame.Cursor;
        float width = Math.Max(1, Size.X), height = Math.Max(1, Size.Y);
        float spacing = Math.Min(350, (width - 200) / Math.Max(1, _cards.Count));
        for (int i = 0; i < _cards.Count; i++)
        {
            bool sourceLayout = frame.Centers?.Count == _cards.Count;
            int hovered = frame.Controller ? frame.Hovered : sourceLayout
                ? DemoRewardLayout.Hover(frame.Cursor, Size, frame.Centers!) : DemoRewardLayout.Hover(frame.Cursor, width, height, _cards.Count);
            bool focused = frame.Chosen >= 0 ? frame.Chosen == i : hovered == i;
            var position = sourceLayout ? frame.Centers![i] * Size
                : new Vector2(width / 2 + (i - (_cards.Count - 1) / 2f) * spacing, height / 2);
            position.Y -= focused ? 28 : 0;
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
        _exit.Visible = false; // Entry/exit controls stay in the player table in both UI modes.
        _exit.SelfModulate = ActiveTint;
        // NCard's Control.Size is zero: its art is centred on a 300-unit card face.
        float cardsRight = _cards.Count > 0 ? _cards.Max(c => c.Position.X + 150 * c.Scale.X)
            : width / 2;
        float cardsCenter = frame.Centers?.Count > 0 ? frame.Centers.Average(p => p.Y) * height : height / 2;
        _exit.Position = new(Math.Clamp(cardsRight + 48, 0, width - _exit.Size.X - 24), cardsCenter - _exit.Size.Y / 2);
        if (_upgradeRow != null) _upgradeRow.Position = new(width / 2 - _upgradeRow.Size.X / 2, height * .79f);
        _rewardsWindow?.Highlight(frame.Hovered, frame.Chosen);
        SetPlayerListForeground(true);
    }

    private void SendSnapshot()
    {
        if (!_net.IsConnected || !RunManager.Instance.IsInProgress || RunManager.Instance.DebugOnlyGetState()!.Players.Count < 2) return;
        var frame = new DemoRewardMessage
        {
            ProtocolVersion = DemoRewardMessage.Protocol, Sequence = ++_sequence, Location = RunManager.Instance.DebugOnlyGetState()!.RunLocation,
            Cursor = (GetViewport().GetMousePosition() / GetViewport().GetVisibleRect().Size).Clamp(-1, 2),
            Controller = NControllerManager.Instance?.IsUsingController == true, Entries = [],
            Screen = DemoTopBar.PublicationScreen?.GetType().Name ?? "",
            RoomComplete = LocalRoomComplete(),
            Kind = DemoRewardKind.None, Hovered = -1, Chosen = -1, Cards = [], Labels = [],
        };
        var activeScreen = DemoTopBar.PublicationScreen;
        bool watched = HasWatchers(Time.GetTicksMsec());
        // Reject unsupported contexts before inspecting/serializing any card options,
        // reward icons or event-specific data, not only before displaying them.
        if (!LocalScreenAllowed())
        {
            _lastNonMapFrame = null;
            if (_availabilityKey != "disabled" || Time.GetTicksMsec() - _lastAvailability >= 1000)
            {
                _availabilityKey = "disabled"; _lastAvailability = Time.GetTicksMsec();
                _net.SendMessage(frame);
            }
            return;
        }
        if (_availabilityKey == "disabled") _availabilityKey = "";
        // A friend's map is a local utility overlay, not a new observed activity.
        // Keep the last complete description (never a cursor-only delta) alive.
        if (activeScreen is NMapScreen or NMapRoom && DemoSettings.AllActivities)
        {
            frame.Kind = DemoRewardKind.Map;
            if (Time.GetTicksMsec() - _lastAvailability >= 1000 || _availabilityKey != "map")
            { _net.SendMessage(frame); _availabilityKey = "map"; _lastAvailability = Time.GetTicksMsec(); }
            return;
        }
        if (activeScreen is NMapScreen)
        {
            if (LocalScreenAllowed() && _lastNonMapFrame is { } previous && previous.Location == frame.Location)
            {
                previous.Sequence = frame.Sequence; previous.Delta = false;
                if (Time.GetTicksMsec() - _lastRewardDetails >= 1000 || _forceFullMetadata)
                { _net.SendMessage(previous); _lastRewardDetails = Time.GetTicksMsec(); }
            }
            else _net.SendMessage(frame);
            return;
        }
        // Unwatched clients advertise only availability. Do not walk card holders,
        // reward icons or construct serialized card models ten times per second.
        if (!watched)
        {
            frame.Kind = LocalScreenAllowed()
                ? activeScreen is NCardRewardSelectionScreen ? DemoRewardKind.CardChoice
                    : activeScreen is NRewardsScreen { IsComplete: true } ? DemoRewardKind.Waiting
                    : activeScreen is NRewardsScreen ? DemoRewardKind.Rewards : DemoRewardKind.Activity
                : DemoRewardKind.None;
            string availability = $"{frame.Kind}:{activeScreen?.GetType().Name}:{frame.Location}";
            if (availability == _availabilityKey && Time.GetTicksMsec() - _lastAvailability < 1000) return;
            _availabilityKey = availability; _lastAvailability = Time.GetTicksMsec();
            _net.SendMessage(frame); return;
        }
        if (frame.Controller && GetViewport().GuiGetFocusOwner() is { } focusedControl)
            frame.Cursor = ((focusedControl.GetGlobalTransformWithCanvas() * (focusedControl.Size / 2)) / GetViewport().GetVisibleRect().Size).Clamp(-1, 2);
        bool choicesOpen = activeScreen is NCardRewardSelectionScreen
            && GodotObject.IsInstanceValid(_localChoiceScreen) && _localChoiceScreen!.IsInsideTree();
        if ((choicesOpen || activeScreen is NRewardsScreen { IsComplete: false } && _choiceHoldUntil > Time.GetTicksMsec()))
        {
            frame.Kind = DemoRewardKind.CardChoice;
            frame.Cards = _localOptions.Take(DemoRewardMessage.MaxOptions).Select(c => c.Card.ToSerializable()).ToList();
            frame.Labels = _localAlternatives.Select(a => a.Title.GetFormattedText()).Take(DemoRewardMessage.MaxOptions).ToList();
            frame.Chosen = _chosen;
            if (choicesOpen)
            {
                var holders = DemoUi.Descendants<NGridCardHolder>(_localChoiceScreen!).ToList();
                frame.Centers = _localOptions.Select(option =>
                {
                    var holder = holders.FirstOrDefault(h => h.CardModel == option.Card);
                    return holder?.CardNode != null ? holder.GetGlobalTransformWithCanvas().Origin / GetViewport().GetVisibleRect().Size
                        : new Vector2(.5f, .5f);
                }).Take(DemoRewardMessage.MaxOptions).ToList();
                // Mouse wins over the default keyboard/controller focus on card 0.
                var focused = holders.FirstOrDefault(h => h.HasFocus() || h.Hitbox.HasFocus())?.CardModel;
                frame.Hovered = frame.Controller && focused != null ? _localOptions.ToList().FindIndex(c => c.Card == focused) : -1;
            }
        }
        else if (activeScreen is NRewardsScreen && GodotObject.IsInstanceValid(_localRewardsScreen) && _localRewardsScreen!.IsInsideTree() && !_localRewardsScreen.IsComplete && _localRewardsSet != null)
        {
            frame.Kind = DemoRewardKind.Rewards;
            var buttons = DemoUi.Descendants<NRewardButton>(_localRewardsScreen).Where(b => b.IsVisibleInTree() && b.Reward != null).Take(DemoRewardMessage.MaxOptions).ToList();
            frame.Labels = buttons.Select(b => b.Reward!.Description.GetFormattedText()).ToList();
            frame.Header = DemoUi.Descendants<Label>(_localRewardsScreen).FirstOrDefault(l => l.Name == "HeaderLabel")?.Text;
            frame.Entries = buttons.Select(b =>
            {
                var icon = b.GetNode<Control>("%Icon");
                return new DemoRewardEntry(b.Reward!.GetType().Name,
                    b.Reward.Description.GetFormattedText()[..Math.Min(b.Reward.Description.GetFormattedText().Length, 512)],
                    DemoVisualProjection.Capture(icon, icon.GetGlobalTransformWithCanvas().AffineInverse()).Take(8).ToList());
            }).ToList();
            for (int i = 0; i < buttons.Count; i++)
                if (buttons[i].HasFocus() || buttons[i].GetGlobalRect().HasPoint(GetGlobalMousePosition())) { frame.Hovered = i; break; }
        }
        if (activeScreen is not NRewardsScreen { IsComplete: true } && frame.Kind != DemoRewardKind.CardChoice && _summaryHoldUntil > Time.GetTicksMsec() && _summaryClick is { } summary)
        {
            frame.Kind = summary.Kind; frame.Labels = summary.Labels; frame.Cards = [];
            frame.Hovered = summary.Hovered; frame.Chosen = summary.Chosen;
        }
        // Block rest sites, shared relic selection, personal menus and unknown custom
        // screens even when an old reward screen is still retained underneath them.
        if (!LocalScreenAllowed()) frame.Kind = DemoRewardKind.None;
        else if (frame.Kind == DemoRewardKind.None)
        {
            var screen = DemoTopBar.PublicationScreen;
            frame.Kind = screen is NRewardsScreen { IsComplete: true } ? DemoRewardKind.Waiting
                : screen is NRewardsScreen ? DemoRewardKind.Rewards : DemoRewardKind.Activity;
            frame.Cards = []; frame.Labels = [];
        }
        _lastNonMapFrame = frame;
        if (frame.Kind != DemoRewardKind.None) frame.Targeting = IsViewing || DemoController.IsViewing ? default : DemoTargeting.Capture(this);
        {
            string details = frame.ContentKey() + string.Join('|', frame.Centers?.Select(p => $"{MathF.Round(p.X * 1000)}:{MathF.Round(p.Y * 1000)}") ?? []);
            if (!_forceFullMetadata && details == _rewardDetailsKey)
            { frame.Delta = true; frame.Cards = []; frame.Labels = []; frame.Entries = []; frame.Header = ""; frame.Centers = []; }
            else { _rewardDetailsKey = details; _lastRewardDetails = Time.GetTicksMsec(); }
            if (frame.Kind is not DemoRewardKind.Activity and not DemoRewardKind.Waiting) _forceFullMetadata = false;
        }
        _net.SendMessage(frame);
    }

    private void Receive(DemoRewardMessage frame, ulong sender)
    {
        if (sender == _net.NetId || !Connected(sender) || frame.ProtocolVersion != DemoRewardMessage.Protocol
            || frame.Location != RunManager.Instance.DebugOnlyGetState()?.RunLocation
            || frame.Kind > DemoRewardKind.Waiting || frame.Cards == null || frame.Labels == null) return;
        if (_frames.TryGetValue(sender, out var previous) && !DemoSession.Newer(frame.Sequence, previous.Message.Sequence)) return;
        if (frame.Kind is DemoRewardKind.Activity or DemoRewardKind.Map && (!DemoSettings.AllActivities || !DemoScreenPolicy.KnownActivity(frame.Screen)))
        { frame.Kind = DemoRewardKind.None; frame.Delta = false; }
        if (frame.Kind == DemoRewardKind.Waiting && frame.Screen != "NRewardsScreen") return;
        if (frame.Delta)
        {
            if (previous == null || previous.Message.Kind != frame.Kind || previous.Message.Screen != frame.Screen) return;
            frame.Cards = previous.Message.Cards; frame.Labels = previous.Message.Labels;
            frame.Header = previous.Message.Header; frame.Entries = previous.Message.Entries; frame.Centers = previous.Message.Centers;
        }
        var received = new RemoteFrame(frame, Time.GetTicksMsec(), frame.Delta ? previous!.ContentKey : frame.ContentKey());
        _frames[sender] = received;
        // Reliable descriptor and full UI fragments can arrive in the same game
        // tick. Prepare the matching renderer now, before its first full state.
        if (_target == sender && !DemoTopBar.UtilityOpen && frame.Kind != DemoRewardKind.None && _contentKey != received.ContentKey)
        {
            try { Render(frame, 0, received.ContentKey); }
            catch (Exception e) { Log.Warn($"[Friend View] Invalid peer display state: {e.Message}"); Exit(); }
        }
    }
    private void ReceivePresence(DemoPresenceMessage frame, ulong sender)
    {
        if (sender == _net.NetId || !Connected(sender) || frame.ProtocolVersion != 1
            || frame.Location != RunManager.Instance.DebugOnlyGetState()?.RunLocation) return;
        if (_presence.TryGetValue(sender, out var previous) && !DemoSession.Newer(frame.Sequence, previous.Message.Sequence)) return;
        bool newWatcher = !_presence.TryGetValue(sender, out var old) || !old.Message.Watching || old.Message.TargetId != _net.NetId || frame.RequestSnapshot;
        _presence[sender] = (frame, Time.GetTicksMsec());
        if (newWatcher && frame.Watching && frame.TargetId == _net.NetId) { _forceFullMetadata = true; SendSnapshot(); }
    }
    private void SendPresence(bool requestSnapshot = false)
    {
        var run = RunManager.Instance.DebugOnlyGetState();
        if (run == null || !_net.IsConnected || DemoController.IsViewing || CombatManager.Instance.IsInProgress && _target == null && _loadingTarget == null) return;
        _net.SendMessage(new DemoPresenceMessage
        {
            ProtocolVersion = 1, Sequence = DemoPresenceSequence.Next(), Location = run.RunLocation,
            Watching = _target.HasValue || _loadingTarget.HasValue, RequestSnapshot = requestSnapshot, TargetId = _target ?? _loadingTarget ?? 0,
        });
    }
    private void Clear()
    {
        foreach (var child in _contents.GetChildren()) { _contents.RemoveChild(child); child.QueueFree(); }
        _cards.Clear(); _labels.Clear(); _contentKey = "";
        _highlighted.Clear();
        _upgradeRow = null; _upgradeTickbox = null; _viewUpgraded = false; _displayOptions = [];
        _nativeImage = null; _rewardsWindow = null; _lastNativeFrame = 0;
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
        Exit();
        if (_transitionZ is int originalZ && NGame.Instance?.Transition is { } transition) transition.ZIndex = originalZ;
        _net.UnregisterMessageHandler<DemoRewardMessage>(Receive);
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
        if (NRun.Instance?.TreasureRoom != null) return false;
        var screen = DemoTopBar.PublicationScreen;
        if (screen is Node node && Current?._unavailableRoot == node.GetInstanceId()) return false;
        return screen?.GetType().Assembly == typeof(NRun).Assembly && DemoScreenPolicy.KnownActivity(screen.GetType().Name);
    }

    private static bool LocalRoomComplete()
    {
        if (CombatManager.Instance.IsInProgress || NRun.Instance == null) return false;
        if (DemoTopBar.PublicationScreen is NRewardsScreen rewards) return rewards.IsComplete;
        return DemoTopBar.PublicationScreen is IRoomWithProceedButton room
            && room.ProceedButton.IsVisibleInTree() && room.ProceedButton.IsEnabled;
    }

    private void UpdateTransitionOpacity()
    {
        // Global transition must cover observer chrome too. Keep it above our
        // highest top-bar layer, rather than guessing shader fade progress.
        if (NGame.Instance?.Transition is { } transition && transition.ZIndex < 1300)
        { _transitionZ ??= transition.ZIndex; transition.ZIndex = 1300; }
    }

    private bool HasWatchers(ulong now) => _presence.Any(p => Connected(p.Key) && p.Value.Message.Watching
        && p.Value.Message.TargetId == _net.NetId && now - p.Value.Seen < 6500);

    private bool ShouldCapture(ulong now) => (DemoSettings.AllActivities || DemoTopBar.PublicationScreen is NRewardsScreen { IsComplete: true })
        && LocalScreenAllowed() && HasWatchers(now)
        && DemoTopBar.PublicationScreen is not NRewardsScreen { IsComplete: false } and not NCardRewardSelectionScreen and not NMapScreen and not NMapRoom;

    private void CaptureNativeScreen()
    {
        if (!ShouldCapture(Time.GetTicksMsec()) || DemoTopBar.PublicationScreen is not Node root) return;
        try
        {
            var reference = GetViewport().GetVisibleRect().Size;
            bool mapOverlay = root is NMapScreen;
            if (mapOverlay && (_lastNonMapFrame?.Kind != DemoRewardKind.Activity || _publishedVisuals.Count == 0)) return;
            var current = mapOverlay ? _publishedVisuals : (root is NRewardsScreen { IsComplete: true }
                ? DemoVisualProjection.Capture(NRun.Instance!.GetNode<Node>("RoomContainer"), Transform2D.Identity)
                : DemoVisualProjection.CaptureActivity(root)).ToDictionary(v => v.Id);
            string screen = mapOverlay ? _publishedScreen : root.GetType().Name;
            ulong now = Time.GetTicksMsec();
            bool full = _forceFullMetadata || screen != _publishedScreen || root.GetInstanceId() != _publishedRoot;
            var update = new DemoVisualUpdate { Screen = screen, Full = full, Base = _publishedBase,
                Reference = [reference.X, reference.Y],
                Items = full ? current.Values.ToList() : current.Where(p => !_publishedVisuals.TryGetValue(p.Key, out var old)
                    || !DemoVisualProjection.Same(p.Value, old)).Select(p => p.Value).ToList(),
                Removed = full ? [] : _publishedVisuals.Keys.Except(current.Keys).ToList() };
            if (!full && update.Items.Count == 0 && update.Removed.Count == 0) return;
            byte[] data = DemoVisualCodec.Encode(update);
            if (data.Length > DemoScreenMessage.MaxFrameBytes) throw new InvalidOperationException("UI metadata exceeds safe budget.");
            uint sequence = ++_screenSequence;
            int count = (data.Length + DemoScreenMessage.ChunkBytes - 1) / DemoScreenMessage.ChunkBytes;
            for (int i = 0; i < count; i++)
                _net.SendMessage(new DemoScreenMessage { ProtocolVersion = DemoScreenMessage.Protocol, Sequence = sequence,
                    Location = RunManager.Instance.DebugOnlyGetState()!.RunLocation, Index = (byte)i, Count = (byte)count,
                    Cursor = mapOverlay ? _publishedCursor : (GetViewport().GetMousePosition() / reference).Clamp(-1, 2),
                    MouseDown = !mapOverlay && Input.IsMouseButtonPressed(MouseButton.Left),
                    ImageOrigin = Vector2.Zero, ImageSize = Vector2.One,
                    Data = data.Skip(i * DemoScreenMessage.ChunkBytes).Take(DemoScreenMessage.ChunkBytes).ToArray() });
            _publishedVisuals = current; _publishedScreen = screen; _publishedBase = sequence; _publishedRoot = root.GetInstanceId();
            if (!mapOverlay) _publishedCursor = (GetViewport().GetMousePosition() / reference).Clamp(-1, 2);
            if (full) _lastFullMetadata = now;
            _forceFullMetadata = false;
        }
        catch (Exception e)
        {
            _unavailableRoot = root.GetInstanceId();
            SendSnapshot();
            Log.Warn($"[Friend View] Metadata projection unavailable; screen disabled: {e}");
        }
    }

    private void ReceiveScreen(DemoScreenMessage message, ulong sender)
    {
        if (_target != sender || message.ProtocolVersion != DemoScreenMessage.Protocol
            || message.Location != RunManager.Instance.DebugOnlyGetState()?.RunLocation || _nativeImage == null) return;
        var data = _screenAssembly.Accept(message, Time.GetTicksMsec());
        if (data == null) return;
        try
        {
            var update = DemoVisualCodec.Decode(data);
            if (!DemoScreenPolicy.KnownActivity(update.Screen) || !_frames.TryGetValue(sender, out var frame)
                || update.Screen != frame.Message.Screen) return;
            if (!_nativeImage.Apply(update, message.Sequence)) { SendPresence(requestSnapshot: true); return; }
            _nativeCursor = message.Cursor; _nativeMouseDown = message.MouseDown; _lastNativeFrame = Time.GetTicksMsec();
        }
        catch (Exception e) when (e is InvalidDataException or System.Text.Json.JsonException or IOException or InvalidOperationException)
        { Log.Warn("[Friend View] Invalid UI metadata rejected."); }
    }
}

[HarmonyPatch(typeof(NRun), nameof(NRun._Ready))]
internal static class AttachRewardDemo
{
    static void Postfix(NRun __instance) => Callable.From(() =>
    { if (GodotObject.IsInstanceValid(__instance) && __instance.IsInsideTree())
        {
            __instance.GlobalUi.AddChild(new RewardDemoController { Name = "TogetherRewardDemo" });
            __instance.GlobalUi.AddChild(new DemoTopBar { Name = "FriendViewTopBar" });
        }
    }).CallDeferred();
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

[HarmonyPatch(typeof(NMultiplayerPlayerState), "OnRelease")]
internal static class ObserveFromPlayerRow
{
    static bool Prefix(NMultiplayerPlayerState __instance)
    {
        // Exit owns the click even on a map/unsupported screen or while the entry
        // preference changes. Never fall through to the native deck inspector.
        if ((RewardDemoController.ObservedPlayerId ?? DemoController.ObservedPlayerId) == __instance.Player.NetId)
        {
            RewardDemoController.Current?.Exit(); DemoController.Current?.ExitDemo();
            return false;
        }
        if (DemoSettings.SeparateButton || RewardDemoController.Current is not { } controller
            || !controller.CanObserve(__instance.Player.NetId)
            || NTargetManager.Instance.LastTargetingFinishedFrame == __instance.GetTree().GetFrame()) return true;
        controller.Toggle(__instance.Player.NetId);
        return false;
    }
}
