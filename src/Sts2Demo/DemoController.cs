using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;
using Sts2Demo.Core;
using Sts2Demo.Networking;
using Sts2Demo.Ui;
using MegaCrit.sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.Core.Assets;

namespace Sts2Demo;

/// <summary>Combat-scoped, cosmetic observer. All replicated models are read only.</summary>
public partial class DemoController : Control
{
    private sealed record Mirror(CardModel Source, NCard Node);
    private sealed record Presence(ulong Target, bool Watching, uint Sequence, ulong LastSeen);
    private sealed record HandFrame(DemoHandMessage Message, ulong LastSeen);
    public static DemoController? Current { get; private set; }
    public static bool IsViewing => Current?._session.Active == true;
    public static ulong? ObservedPlayerId => Current?._session.Target;
    private static Texture2D? _rememberedEye;
    public static Texture2D? EyeTexture => Current?._eye ?? _rememberedEye;
    public NCombatUi Ui { get; set; } = null!;
    private readonly DemoSession _session = new();
    private readonly Dictionary<ulong, Presence> _presence = [];
    private readonly Dictionary<ulong, HandFrame> _frames = [];
    private readonly Dictionary<uint, Mirror> _cards = [];
    private readonly HashSet<uint> _highlighted = [];
    private readonly Dictionary<CanvasItem, bool> _hidden = [];
    private readonly Dictionary<CanvasItem, Color> _ghosted = [];
    private INetGameService? _net;
    private Control _demo = null!;
    private Control _hand = null!;
    private HFlowContainer _relics = null!;
    private HFlowContainer _powers = null!;
    private NTopBarHp? _health;
    private NRemoteMouseCursor? _cursor;
    private NEnergyCounter? _energy;
    private Label _stars = null!;
    private Texture2D? _eye;
    private Tween? _transition;
    private uint _handSequence;
    private ulong _lastSend;
    private ulong _lastPresence;
    private ulong _lastRefresh;
    private string _inventoryKey = "";
    private bool _failed;
    private Tween? _returnTween;
    private Color? _returnHandTint;
    private Node2D _arrow = null!;
    private readonly List<Sprite2D> _arrowParts = [];

    public override void _Ready()
    {
        Current = this;
        MouseFilter = MouseFilterEnum.Ignore;
        SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        ZIndex = 100;
        ProcessPriority = 100;
        _net = RunManager.Instance.NetService;
        _net.RegisterMessageHandler<DemoPresenceMessage>(ReceivePresence);
        _net.RegisterMessageHandler<DemoHandMessage>(ReceiveHand);
        if (RunManager.Instance.RunLobby is { } lobby)
        {
            lobby.RemotePlayerDisconnected += ResetPeer;
            lobby.PlayerRejoined += ResetPeer;
        }
        // Resolve the existing peek-eye texture from the game's own instantiated UI.
        _eye = DemoUi.Descendants<TextureRect>(Ui.Hand.PeekButton)
            .FirstOrDefault(t => t.Texture != null && !t.Name.ToString().Contains("Flash", StringComparison.OrdinalIgnoreCase))?.Texture;
        _rememberedEye = _eye;
        BuildView();
        Log.Info("[Friend View] Combat UI attached.");
    }

    private void BuildView()
    {
        _demo = new Control { Name = "DemoView", MouseFilter = MouseFilterEnum.Ignore, Visible = false };
        AddChild(_demo);
        _demo.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _hand = new Control { Name = "ReadOnlyHand", MouseFilter = MouseFilterEnum.Ignore };
        _demo.AddChild(_hand);
        _hand.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        _arrow = new Node2D { Name = "DemoTargetingArrow", ZIndex = 150, Visible = false };
        _demo.AddChild(_arrow);
        for (int i = 0; i < 20; i++)
        {
            var part = new Sprite2D { Texture = PreloadManager.Cache.GetTexture2D(i == 19
                ? "res://images/ui/combat/targeting_arrow_head.png" : "res://images/ui/combat/targeting_arrow_segment.png") };
            _arrow.AddChild(part); _arrowParts.Add(part);
        }
        _stars = DemoUi.Label("", 24);
        _demo.AddChild(_stars);
        _relics = new HFlowContainer { Name = "DemoRelics", MouseFilter = MouseFilterEnum.Ignore };
        _demo.AddChild(_relics);
        _powers = new HFlowContainer { Name = "DemoPowers", MouseFilter = MouseFilterEnum.Ignore };
        _demo.AddChild(_powers);
    }

    public override void _Process(double delta)
    {
        if (_failed || _net == null || !RunManager.Instance.IsInProgress) return;
        try
        {
            ulong now = Time.GetTicksMsec();
            if (now - _lastSend >= 66 && CombatManager.Instance.IsInProgress)
            {
                SendHand();
                _lastSend = now;
            }
            if (now - _lastPresence >= 2000)
            {
                SendPresence();
                _lastPresence = now;
            }
            if (!_session.Active) return;
            var target = Target;
            if (!CombatManager.Instance.IsInProgress)
            {
                var nextTarget = _session.Target;
                ExitDemo();
                RewardDemoController.ContinueAfterCombat(nextTarget);
                return;
            }
            if (!_net.IsConnected || target?.PlayerCombatState == null
                || !IsConnected(target.NetId) || Ui.Hand.IsInCardSelection || Ui.Hand.InCardPlay
                || NRun.Instance?.GlobalUi.Overlays.ScreenCount > 0
                || NRun.Instance?.GlobalUi.CapstoneContainer.CurrentCapstoneScreen != null)
            {
                ExitDemo();
                return;
            }
            if (NModalContainer.Instance?.OpenModal != null) { ExitDemo(); return; }
            Layout(target);
            // Game animations may attempt to reveal these again during a turn change.
            foreach (var node in _hidden.Keys) if (GodotObject.IsInstanceValid(node)) node.Visible = false;
            foreach (var node in _ghosted.Keys) if (GodotObject.IsInstanceValid(node)) node.Modulate = Colors.Transparent;
            if (now - _lastRefresh > 150)
            {
                RefreshCards(target, now);
                RefreshInventory(target);
                _lastRefresh = now;
            }
            UpdateCards(target, (float)delta, now);
            UpdateCursor(target);
            UpdateArrow(target, now);
        }
        catch (Exception error)
        {
            Log.Error($"[Friend View] Observer disabled safely: {error}");
            ExitDemo();
            _failed = true;
        }
    }

    private Player? Target => _session.Target is ulong id ? RunManager.Instance.DebugOnlyGetState()?.GetPlayer(id) : null;
    private bool IsConnected(ulong id) => _net?.NetId == id || RunManager.Instance.RunLobby?.ConnectedPlayerIds.Contains(id) == true;
    private void ResetPeer(ulong id)
    {
        _frames.Remove(id); _presence.Remove(id);
        if (_session.Target == id) ExitDemo();
    }


    public bool EnterDemo(ulong id)
    {
        try { return TryEnterDemo(id); }
        catch (Exception error)
        {
            Log.Error($"[Friend View] Could not enter observer safely: {error}");
            ExitDemo(); _failed = true;
            return false;
        }
    }

    private bool TryEnterDemo(ulong id)
    {
        var player = RunManager.Instance.DebugOnlyGetState()?.GetPlayer(id);
        if (player?.PlayerCombatState == null || _net == null || _failed) return false;
        if (!_session.TryEnter(_net.NetId, id, IsConnected(id), CombatManager.Instance.IsInProgress,
            Ui.Hand.InCardPlay || Ui.Hand.IsInCardSelection || NModalContainer.Instance?.OpenModal != null
            || NRun.Instance?.GlobalUi.Overlays.ScreenCount > 0)) return false;
        _transition?.Kill();
        RestoreReturnAnimation();
        ClearCards();
        _arrow.Hide();
        _cursor?.QueueFree();
        _cursor = NRemoteMouseCursor.Create(id);
        _demo.AddChild(_cursor);
        DemoUi.ReadOnly(_cursor);
        _cursor.ZIndex = 200;
        _energy?.QueueFree();
        _energy = NEnergyCounter.Create(player);
        if (_energy != null) { _demo.AddChild(_energy); DemoUi.ReadOnly(_energy); }
        HideLocal(Ui.Hand);
        Ui.Hand.DisableControllerNavigation();
        HideLocal(Ui.EnergyCounterContainer);
        foreach (var previous in _ghosted.Keys.OfType<NRemoteTargetingIndicator>().ToArray())
        {
            if (GodotObject.IsInstanceValid(previous)) previous.Modulate = _ghosted[previous];
            _ghosted.Remove(previous);
        }
        foreach (var indicator in DemoUi.Descendants<NRemoteTargetingIndicator>(NGame.Instance!))
        {
            var owner = (Player?)HarmonyLib.AccessTools.Field(typeof(NRemoteTargetingIndicator), "_player").GetValue(indicator);
            if (owner?.NetId != id) continue;
            if (!_ghosted.ContainsKey(indicator)) _ghosted[indicator] = indicator.Modulate;
            indicator.Modulate = Colors.Transparent;
        }
        if (NRun.Instance != null)
        {
            var hp = NRun.Instance.GlobalUi.TopBar.Hp;
            _health?.QueueFree();
            _health = (NTopBarHp)hp.Duplicate((int)Node.DuplicateFlags.Scripts);
            // This control was originally owned by the whole top-bar scene.
            // Rebind its unique children before _Ready resolves %HpLabel.
            foreach (var child in DemoUi.Descendants<Node>(_health))
            {
                child.Owner = _health;
                if (child.Name == "HpLabel") child.UniqueNameInOwner = true;
            }
            _demo.AddChild(_health);
            _health.SetAnchorsAndOffsetsPreset(LayoutPreset.TopLeft);
            _health.Size = hp.Size;
            _health.Initialize(player);
            _health.Modulate = _ghosted.TryGetValue(hp, out var originalTint) ? originalTint : hp.Modulate;
            DemoUi.ReadOnly(_health);
            if (!_ghosted.ContainsKey(hp)) _ghosted[hp] = hp.Modulate;
            hp.Modulate = Colors.Transparent; // Preserve its slot in the native HBox layout.
            HideLocal(NRun.Instance.GlobalUi.RelicInventory);
        }
        _inventoryKey = "";
        _demo.Visible = true;
        _demo.Modulate = Colors.Transparent;
        Layout(player);
        RefreshCards(player, Time.GetTicksMsec());
        RefreshInventory(player);
        _transition = CreateTween();
        _transition.TweenProperty(_demo, "modulate:a", 1f, 0.22).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.Out);
        RewardDemoController.Current?.FocusAvatarEye(id);
        NGame.Instance?.RemoteCursorContainer.ForceUpdateAllCursors();
        SendPresence();
        Log.Info($"[Friend View] Enter demo: {id}. Read-only mirrors: {_cards.Count}.");
        return true;
    }

    private void HideLocal(CanvasItem node)
    {
        if (!_hidden.ContainsKey(node)) _hidden[node] = node.Visible;
        node.Visible = false;
    }

    public void ExitDemo()
    {
        if (!_session.Active && _hidden.Count == 0 && _ghosted.Count == 0) return;
        _session.Exit();
        _transition?.Kill();
        RestoreReturnAnimation();
        foreach (var (node, visible) in _hidden)
            if (GodotObject.IsInstanceValid(node)) node.Visible = visible;
        _hidden.Clear();
        foreach (var (node, color) in _ghosted) if (GodotObject.IsInstanceValid(node)) node.Modulate = color;
        _ghosted.Clear();
        bool animate = IsInsideTree() && CombatManager.Instance.IsInProgress && !_failed;
        if (animate)
        {
            _transition = CreateTween();
            _transition.TweenProperty(_demo, "modulate:a", 0f, .22).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
            _transition.TweenCallback(Callable.From(ClearExitedView));
        }
        else ClearExitedView();
        if (GodotObject.IsInstanceValid(Ui.Hand) && Ui.Hand.IsInsideTree())
        {
            Ui.Hand.AnimIn();
            Ui.Hand.EnableControllerNavigation();
            if (animate && Ui.Hand.Visible)
            {
                _returnHandTint = Ui.Hand.Modulate;
                Ui.Hand.Modulate = new Color(_returnHandTint.Value, 0);
                _returnTween = CreateTween();
                _returnTween.TweenProperty(Ui.Hand, "modulate", _returnHandTint.Value, .22)
                    .SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
                _returnTween.TweenCallback(Callable.From(RestoreReturnAnimation));
            }
        }
        SendPresence();
        Log.Info("[Friend View] Exit demo; local UI restored.");
        NGame.Instance?.RemoteCursorContainer.ForceUpdateAllCursors();
    }

    private void RestoreReturnAnimation()
    {
        _returnTween?.Kill(); _returnTween = null;
        if (_returnHandTint is Color tint && GodotObject.IsInstanceValid(Ui.Hand)) Ui.Hand.Modulate = tint;
        _returnHandTint = null;
    }

    private void ClearExitedView()
    {
        if (GodotObject.IsInstanceValid(_demo)) _demo.Hide();
        ClearCards(); _arrow.Hide();
        _cursor?.QueueFree(); _cursor = null;
        _energy?.QueueFree(); _energy = null;
        _health?.QueueFree(); _health = null;
    }

    public override void _Input(InputEvent input)
    {
        if (!_session.Active) return;
        if (input is InputEventKey { Pressed: true, Echo: false, Keycode: Key.Escape }
            || input.IsActionPressed("ui_cancel"))
        {
            ExitDemo();
            GetViewport().SetInputAsHandled();
        }
    }

    private void Layout(Player player)
    {
        var size = Ui.Size;
        var state = player.PlayerCombatState!;
        if (_health != null && NRun.Instance != null)
            _health.Position = GetGlobalTransformWithCanvas().AffineInverse() * NRun.Instance.GlobalUi.TopBar.Hp.GlobalPosition;
        _powers.Position = new(100, size.Y - 310);
        _powers.Size = new(Math.Max(250, size.X - 700), 70);
        _relics.Position = new(24, 88);
        _relics.Size = new(Math.Max(250, size.X - 260), 100);
        if (_energy != null) _energy.Position = Ui.EnergyCounterContainer.Position;
        _stars.Position = new(100, size.Y - 145);
        _stars.Text = state.Stars > 0 || player.Character.ShouldAlwaysShowStarCounter ? $"★ {state.Stars}" : "";
    }

    private void RefreshCards(Player player, ulong now)
    {
        var hand = player.PlayerCombatState!.Hand.Cards;
        var ids = new HashSet<uint>();
        foreach (var source in hand)
        {
            uint id = NetCombatCard.FromModel(source).CombatCardIndex;
            ids.Add(id);
            // Clone only display fields: NCard's description preview must never update
            // the live card's DynamicVars. Do not use CreateClone (it registers game cards).
            var display = (CardModel)source.ClonePreservingMutability();
            if (_cards.TryGetValue(id, out var mirror))
            {
                mirror.Node.Model = display;
                mirror.Node.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
                continue;
            }
            var card = NCard.Create(display);
            if (card == null) continue;
            _hand.AddChild(card);
            DemoUi.ReadOnly(card);
            card.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
            card.Modulate = Colors.White;
            card.Position = new(Ui.Size.X / 2, Ui.Size.Y + 200);
            card.Scale = new(0.6f, 0.6f);
            _cards[id] = new(source, card);
        }
        foreach (var id in _cards.Keys.Where(id => !ids.Contains(id)).ToArray())
        {
            var removed = _cards[id].Node;
            _cards.Remove(id);
            var tween = removed.CreateTween().SetParallel();
            tween.TweenProperty(removed, "position:y", Ui.Size.Y + 200, 0.18);
            tween.TweenProperty(removed, "modulate:a", 0f, 0.18);
            tween.Chain().TweenCallback(Callable.From(removed.QueueFree));
        }
    }

    private void UpdateCards(Player player, float delta, ulong now)
    {
        bool fresh = _frames.TryGetValue(player.NetId, out var frame) && now - frame.LastSeen < 1500
            && frame.Message.Round == CombatManager.Instance.DebugOnlyGetState()?.RoundNumber && !frame.Message.Suspended;
        var poses = fresh ? frame!.Message.Cards : null;
        var hover = RunManager.Instance.InputSynchronizer.GetHoveredModelData(player.NetId).hoveredCombatCard;
        int index = 0, count = _cards.Count;
        foreach (var (id, mirror) in _cards)
        {
            var pose = poses?.FirstOrDefault(p => p.CardId == id);
            bool exact = pose.HasValue && pose.Value.IsValid;
            float offset = index++ - (count - 1) / 2f;
            bool focused = hover?.CombatCardIndex == id;
            // Card origins are their centres in the game's hand helper, so the
            // baseline is symmetric about the viewport centre, not the left edge.
            var position = exact ? pose!.Value.Position * Ui.Size
                : new Vector2(Ui.Size.X / 2 + offset * Math.Min(140, 980f / Math.Max(count, 1)), Ui.Size.Y - 125 + offset * offset * 3 - (focused ? 95 : 0));
            var scale = exact ? pose!.Value.Scale : new Vector2(focused ? 0.85f : 0.65f, focused ? 0.85f : 0.65f);
            var angle = exact ? pose!.Value.Rotation : Mathf.DegToRad(offset * 4);
            float blend = 1 - MathF.Exp(-18 * delta);
            mirror.Node.Position = mirror.Node.Position.Lerp(position, blend);
            mirror.Node.Scale = mirror.Node.Scale.Lerp(scale, blend);
            mirror.Node.Rotation = Mathf.LerpAngle(mirror.Node.Rotation, angle, blend);
            mirror.Node.ZIndex = focused ? 50 : exact ? Math.Clamp(pose!.Value.ZIndex, 0, 40) : index;
            bool selected = exact && pose!.Value.Selected;
            if (selected && _highlighted.Add(id)) mirror.Node.CardHighlight.AnimShow();
            else if (!selected && _highlighted.Remove(id)) mirror.Node.CardHighlight.AnimHide();
        }
    }

    private void RefreshInventory(Player player)
    {
        var key = string.Join("|", player.Relics.Select(r => $"{r.Id}:{r.ShowCounter}:{r.DisplayAmount}")) + ":"
            + string.Join("|", player.Creature.Powers.Select(p => $"{p.Id}:{p.DisplayAmount}"));
        if (key == _inventoryKey) return;
        _inventoryKey = key;
        foreach (var child in _relics.GetChildren()) { _relics.RemoveChild(child); child.QueueFree(); }
        foreach (var child in _powers.GetChildren()) { _powers.RemoveChild(child); child.QueueFree(); }
        foreach (var relic in player.Relics)
        {
            var box = new HBoxContainer { MouseFilter = MouseFilterEnum.Stop };
            var icon = DemoUi.Icon(relic.Icon, new(44, 44));
            box.TooltipText = DemoUi.Plain(relic.Title.GetFormattedText() + "\n" + relic.DynamicDescription.GetFormattedText());
            box.AddChild(icon);
            if (relic.ShowCounter) box.AddChild(DemoUi.Label(relic.DisplayAmount.ToString(), 20));
            _relics.AddChild(box);
        }
        foreach (var power in player.Creature.Powers)
        {
            var box = new HBoxContainer { MouseFilter = MouseFilterEnum.Stop };
            box.TooltipText = DemoUi.Plain(power.Title.GetFormattedText() + "\n" + power.SmartDescription.GetFormattedText());
            box.AddChild(DemoUi.Icon(power.Icon, new(32, 32)));
            box.AddChild(DemoUi.Label(power.DisplayAmount.ToString(), 22));
            _powers.AddChild(box);
        }
    }

    private void UpdateCursor(Player target)
    {
        if (_cursor == null) return;
        var input = RunManager.Instance.InputSynchronizer;
        _cursor.SetNextPosition(input.GetControlSpaceFocusPosition(target.NetId, this));
        _cursor.UpdateImage(input.GetMouseDown(target.NetId), MegaCrit.Sts2.Core.Nodes.Screens.Map.DrawingMode.None);
        _cursor.Visible = input.GetScreenType(target.NetId) == NetScreenType.Room;
    }

    private void SendHand()
    {
        if (_net?.IsConnected != true || RunManager.Instance.DebugOnlyGetState()?.Players.Count < 2) return;
        var cards = new List<DemoCardPose>();
        var arrowPoses = new List<DemoCardPose>();
        var arrowTint = Colors.White;
        bool suspended = _session.Active || NRun.Instance?.GlobalUi.Overlays.ScreenCount > 0;
        if (!suspended)
        {
            var inverse = GetGlobalTransformWithCanvas().AffineInverse();
            foreach (var node in DemoUi.Descendants<NCard>(Ui))
            {
                var model = node.Model;
                if (!node.IsVisibleInTree() || model?.Owner?.NetId != _net.NetId || model.Pile?.Type != PileType.Hand) continue;
                var transform = inverse * node.GetGlobalTransformWithCanvas();
                var pose = new DemoCardPose
                {
                    CardId = NetCombatCard.FromModel(model).CombatCardIndex,
                    Position = transform.Origin / Ui.Size, Scale = transform.Scale,
                    Rotation = transform.Rotation, ZIndex = node.GetParent() is CanvasItem parent ? parent.ZIndex : node.ZIndex,
                    Selected = node.GetParent() is NSelectedHandCardHolder,
                };
                if (pose.IsValid && !cards.Any(c => c.CardId == pose.CardId)) cards.Add(pose);
                if (cards.Count >= DemoHandMessage.MaxCards) break;
            }
        }
        if (!suspended)
        {
            var native = DemoUi.Descendants<NTargetingArrow>(NGame.Instance!).FirstOrDefault(a => a.IsVisibleInTree());
            if (native != null)
            {
                arrowTint = native.Modulate;
                var inverse = GetGlobalTransformWithCanvas().AffineInverse();
                foreach (var part in native.GetChildren().OfType<Sprite2D>().Take(20))
                {
                    var transform = inverse * part.GetGlobalTransformWithCanvas();
                    var pose = new DemoCardPose { CardId = (uint)arrowPoses.Count, Position = transform.Origin / Ui.Size,
                        Scale = transform.Scale, Rotation = transform.Rotation };
                    if (pose.IsValid) arrowPoses.Add(pose);
                }
            }
        }
        _net.SendMessage(new DemoHandMessage
        {
            ProtocolVersion = DemoHandMessage.Protocol, Sequence = ++_handSequence,
            Location = RunManager.Instance.DebugOnlyGetState()!.RunLocation,
            Round = CombatManager.Instance.DebugOnlyGetState()?.RoundNumber ?? 0,
            Suspended = suspended, ReferenceSize = Ui.Size, Cards = cards,
            Arrow = arrowPoses, ArrowTintRG = new(arrowTint.R, arrowTint.G), ArrowTintBA = new(arrowTint.B, arrowTint.A),
        });
    }

    private void SendPresence()
    {
        var run = RunManager.Instance.DebugOnlyGetState();
        if (_net?.IsConnected != true || run == null || run.Players.Count < 2
            || !CombatManager.Instance.IsInProgress) return;
        _net.SendMessage(new DemoPresenceMessage
        {
            ProtocolVersion = DemoPresenceMessage.Protocol, Sequence = DemoPresenceSequence.Next(),
            Location = run.RunLocation,
            Watching = _session.Active, TargetId = _session.Target ?? 0,
        });
    }

    private void ReceivePresence(DemoPresenceMessage message, ulong sender)
    {
        if (message.ProtocolVersion != DemoPresenceMessage.Protocol || !IsConnected(sender) || sender == _net!.NetId) return;
        if (message.Location != RunManager.Instance.DebugOnlyGetState()?.RunLocation) return;
        if (message.Watching && (sender == message.TargetId || RunManager.Instance.DebugOnlyGetState()?.GetPlayer(message.TargetId) == null)) return;
        if (_presence.TryGetValue(sender, out var previous) && !DemoSession.Newer(message.Sequence, previous.Sequence)) return;
        _presence[sender] = new(message.TargetId, message.Watching, message.Sequence, Time.GetTicksMsec());
    }

    private void ReceiveHand(DemoHandMessage message, ulong sender)
    {
        if (message.ProtocolVersion != DemoHandMessage.Protocol || !IsConnected(sender) || sender == _net!.NetId
            || message.Cards == null || message.Cards.Any(c => !c.IsValid)
            || message.Arrow == null || message.Arrow.Any(c => !c.IsValid)
            || !ValidTint(message.ArrowTintRG) || !ValidTint(message.ArrowTintBA)
            || !float.IsFinite(message.ReferenceSize.X) || !float.IsFinite(message.ReferenceSize.Y)
            || message.ReferenceSize.X < 1 || message.ReferenceSize.Y < 1) return;
        if (message.Location != RunManager.Instance.DebugOnlyGetState()?.RunLocation) return;
        if (_frames.TryGetValue(sender, out var previous) && !DemoSession.Newer(message.Sequence, previous.Message.Sequence)) return;
        _frames[sender] = new(message, Time.GetTicksMsec());
    }

    private static bool ValidTint(Vector2 tint) => float.IsFinite(tint.X) && float.IsFinite(tint.Y)
        && tint.X is >= 0 and <= 2 && tint.Y is >= 0 and <= 2;

    private void UpdateArrow(Player target, ulong now)
    {
        _arrow.Visible = _frames.TryGetValue(target.NetId, out var frame) && now - frame.LastSeen < 500
            && !frame.Message.Suspended && frame.Message.Round == CombatManager.Instance.DebugOnlyGetState()?.RoundNumber
            && frame.Message.Arrow?.Count == 20;
        if (!_arrow.Visible) return;
        var message = frame!.Message;
        _arrow.Modulate = new(message.ArrowTintRG.X, message.ArrowTintRG.Y, message.ArrowTintBA.X, message.ArrowTintBA.Y);
        for (int i = 0; i < 20; i++)
        {
            var pose = message.Arrow![i];
            _arrowParts[i].Position = pose.Position * Ui.Size;
            _arrowParts[i].Scale = pose.Scale;
            _arrowParts[i].Rotation = pose.Rotation;
        }
    }

    private static string NameOf(Player player) => NameOf(player.NetId);
    private static string NameOf(ulong id) => PlatformUtil.GetPlayerNameRaw(RunManager.Instance.NetService.Platform, id);
    private void ClearCards()
    {
        // Detach now, rather than waiting until the end of the frame: switching
        // friends must not render the previous hand (including fading removals).
        foreach (var child in _hand.GetChildren())
        {
            _hand.RemoveChild(child);
            child.QueueFree();
        }
        _cards.Clear();
        _highlighted.Clear();
    }

    public override void _ExitTree()
    {
        var rewardsTarget = !CombatManager.Instance.IsInProgress ? _session.Target : null;
        ExitDemo();
        if (rewardsTarget.HasValue && RunManager.Instance.IsInProgress) RewardDemoController.ContinueAfterCombat(rewardsTarget);
        _net?.UnregisterMessageHandler<DemoPresenceMessage>(ReceivePresence);
        _net?.UnregisterMessageHandler<DemoHandMessage>(ReceiveHand);
        if (RunManager.Instance.RunLobby is { } lobby)
        {
            lobby.RemotePlayerDisconnected -= ResetPeer;
            lobby.PlayerRejoined -= ResetPeer;
        }
        _frames.Clear();
        _presence.Clear();
        if (Current == this) Current = null;
    }
}
