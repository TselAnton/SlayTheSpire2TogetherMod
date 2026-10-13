using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Quality;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync;
using MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput;
using Sts2Demo;
using Sts2Demo.Networking;

namespace Sts2DemoLab;

[ModInitializer(nameof(Initialize))]
public static class LabEntry
{
    public static void Initialize()
    {
        // Refuse to touch a normal profile or Steam/Cloud-backed instance.
        if (!OS.GetUserDataDir().Contains("Sts2TogetherDemoLab", StringComparison.Ordinal)
            || !CommandLineHelper.GetValue("force-steam")!.Equals("off", StringComparison.Ordinal))
            throw new InvalidOperationException("Lab requires isolated user directory and --force-steam off.");
        new Harmony("together.sts2.demo.lab").PatchAll(typeof(LabEntry).Assembly);
        Log.Info("[DemoLab] Isolated lab initialized; no Steam, no saving run.");
    }
}

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
static class StartLab
{
    private static bool _started;
    static void Postfix()
    {
        if (_started) return;
        _started = true;
        Callable.From(() => { TaskHelper.RunSafely(Run()); }).CallDeferred();
    }
    static async Task Run()
    {
        try
        {
            var game = NGame.Instance!;
            ProfileOracle.Run();
            await game.ToSignal(game.GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
            SaveManager.Instance.Progress.EnableFtues = false; // In-memory lab profile only.
            game.GetWindow().Size = new(1280, 720);
            var players = new[]
            {
                Player.CreateForNewRun<Ironclad>(UnlockState.all, 1),
                Player.CreateForNewRun<Silent>(UnlockState.all, 2),
                Player.CreateForNewRun<Regent>(UnlockState.all, 3),
            };
            var run = RunState.CreateForNewRun(players, ActModel.GetDefaultList().Select(a => a.ToMutable()).ToList(), [], GameMode.Standard, 0, "DEMOLAB");
            var net = new LabNetService();
            RunManager.Instance.SetUpTest(run, net, disableCombatStateSync: true, shouldSave: false);
            RunManager.Instance.GenerateRooms();
            await PreloadManager.LoadRunAssets(players.Select(p => p.Character));
            RunManager.Instance.Launch();
            game.RootSceneContainer.SetCurrentScene(NRun.Create(run));
            await RunManager.Instance.SetActInternal(0);
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster, MapPointType.Unassigned, ModelDb.Encounter<ToadpolesWeak>().ToMutable());
            game.AddChild(new LabOracle { Players = players, Net = net });
            Log.Info("[DemoLab] Combat created with three players (synthetic host transport).");
        }
        catch (Exception e) { Log.Error($"[DemoLab] START FAILED: {e}"); }
    }
}

public partial class LabOracle : Node
{
    public Player[] Players = [];
    public LabNetService Net = null!;
    private int _phase;
    private ulong _start;
    private uint _sequence;
    private string _before = "";
    private ulong _lastFrame;
    private List<MegaCrit.Sts2.Core.Saves.Runs.SerializableCard> _rewardCards = [];
    private List<MegaCrit.Sts2.Core.Saves.Runs.SerializableCard> _thirdRewardCards = [];
    private NCardRewardSelectionScreen? _localChoice;
    private int _deckBefore;
    private bool _turnVerified;
    private bool _deadVerified;
    private ulong _rewardStarted;
    private bool _cardChoiceVerified;
    private bool _skipVerified;
    private bool _switchVerified;
    private bool _rewardSwitchVerified;
    public override void _Process(double delta)
    {
        if (_phase < 5 && (DemoController.Current == null || Players[1].PlayerCombatState?.Hand.Cards.Count == 0)) return;
        var now = Time.GetTicksMsec();
        if (_start == 0) _start = now;
        if (_phase == 0 && now - _start > 3500)
        {
            _before = Fingerprint();
            if (!DemoController.Current!.EnterDemo(2)) { Log.Error("[DemoLab] ENTER FAILED"); _phase = 99; return; }
            _phase = 1;
            Log.Info("[DemoLab] Entered demo; read-only test begins.");
        }
        if ((_phase == 1 || _phase == 4) && now - _lastFrame > 66)
        {
            var poses = Players[1].PlayerCombatState!.Hand.Cards.Select((c, i) => new DemoCardPose
            {
                CardId = NetCombatCard.FromModel(c).CombatCardIndex,
                Position = new(.5f + (i - (Players[1].PlayerCombatState!.Hand.Cards.Count - 1) / 2f) * .085f, i == 2 ? .68f : .84f),
                Scale = new(i == 2 ? .85f : .65f, i == 2 ? .85f : .65f),
                Rotation = (i - 2) * .06f, ZIndex = i == 2 ? 20 : i, Selected = i == 2,
            }).ToList();
            var location = RunManager.Instance.DebugOnlyGetState()!.RunLocation;
            var msg = new DemoHandMessage { ProtocolVersion = 1, Sequence = ++_sequence, Location = location, Round = CombatManager.Instance.DebugOnlyGetState()!.RoundNumber, ReferenceSize = new(1920, 1080), Cards = poses };
            Net.Inject(msg, 2);
            Net.Inject(new DemoPresenceMessage { ProtocolVersion = 1, Sequence = _sequence, Location = location, Watching = true, TargetId = 1 }, 2);
            Net.Inject(new PeerInputMessage
            {
                screenType = NetScreenType.Room, netMousePos = new(.1f, .5f),
                hoveredModelData = HoveredModelData.FromModel(Players[1].PlayerCombatState!.Hand.Cards[2]),
            }, 2);
            _lastFrame = now;
        }
        if (_phase == 1 && now - _start > 6000)
        {
            NCombatRoom.Instance!.Ui.EndTurnButton.CallReleaseLogic();
            if (_before != Fingerprint()) Log.Error("[DemoLab] FAIL: gameplay changed while observing");
            else Log.Info("[DemoLab] PASS: hand, energy, HP, block, powers, relics and piles unchanged by viewing.");
            Log.Info($"[DemoLab] Mirrors visible: {CountMirrors()}; native hand visible: {NCombatRoom.Instance.Ui.Hand.Visible}");
            _phase = 2;
        }
        if (_phase == 2 && !_turnVerified && now - _start > 7000)
        {
            if (CombatManager.Instance.IsPlayerReadyToEndTurn(Players[0])
                && !CombatManager.Instance.IsPlayerReadyToEndTurn(Players[1])
                && !CombatManager.Instance.IsPlayerReadyToEndTurn(Players[2]))
                Log.Info("[DemoLab] PASS: native end turn ended the observer's own turn, not the friend's.");
            else Log.Error("[DemoLab] FAIL: observer could not end their native turn.");
            NCombatRoom.Instance!.Ui.EndTurnButton.CallReleaseLogic(); // Native undo for the lab only.
            _turnVerified = true;
        }
        if (_phase == 2 && !_switchVerified && now - _start > 10000)
        {
            var controller = RewardDemoController.Current!;
            var third = controller.GetNode<TextureButton>("AvatarDemoEye_3");
            third.EmitSignal(BaseButton.SignalName.Pressed);
            bool switched = DemoController.ObservedPlayerId == 3 && CountMirrors() == Players[2].PlayerCombatState!.Hand.Cards.Count;
            bool green = third.Modulate.G > third.Modulate.R;
            var second = controller.GetNode<TextureButton>("AvatarDemoEye_2");
            second.EmitSignal(BaseButton.SignalName.Pressed);
            if (switched && green && third.Modulate == Colors.White && second.Modulate.G > second.Modulate.R
                && DemoController.ObservedPlayerId == 2 && _before == Fingerprint())
                Log.Info("[DemoLab] PASS: three-player combat switches friend 2 -> 3 -> 2; green tint follows target, state unchanged.");
            else Log.Error($"[DemoLab] FAIL: three-player combat switched={switched} green={green} thirdTint={third.Modulate} secondTint={second.Modulate} target={DemoController.ObservedPlayerId} unchanged={_before == Fingerprint()}.");
            _switchVerified = true;
        }
        if (_phase == 2 && now - _start > 16000)
        {
            var controller = RewardDemoController.Current!;
            var avatarEye = controller.GetNode<TextureButton>("AvatarDemoEye_2");
            int eyes = Descendants<TextureButton>(controller).Count(b => b.Name.ToString().StartsWith("AvatarDemoEye_") && b.Visible);
            bool green = avatarEye.Modulate.G > avatarEye.Modulate.R;
            avatarEye.EmitSignal(BaseButton.SignalName.Pressed);
            if (eyes == 2 && green && !DemoController.IsViewing)
                Log.Info("[DemoLab] PASS: combat has one eye per friend, one green; pressing green leaves demo.");
            else Log.Error($"[DemoLab] FAIL: combat eye count={eyes} green={green} active={DemoController.IsViewing}.");
            Log.Info($"[DemoLab] PASS: exit active={!DemoController.IsViewing}; native hand restored={NCombatRoom.Instance!.Ui.Hand.Visible}");
            _phase = 3;
        }
        if (_phase == 3 && now - _start > 19000)
        {
            DemoController.Current!.EnterDemo(2);
            _phase = 4;
            Log.Info("[DemoLab] Ready for screenshot in demo mode.");
            TaskHelper.RunSafely(Capture("combat-design"));
        }
        if (_phase == 4 && !_deadVerified && now - _start > 24000)
        {
            _deadVerified = true;
            TaskHelper.RunSafely(TestDeadObserver());
        }
        if (_phase == 4 && now - _start > 42000)
        {
            _phase = 5;
            TaskHelper.RunSafely(FinishForRewards());
        }
        if (_phase == 5 && !CombatManager.Instance.IsInProgress && RewardDemoController.Current != null && _rewardCards.Count > 0)
        {
            InjectRewards(now);
            if (RewardDemoController.Current.Enter(2))
            {
                _deckBefore = Players[0].Deck.Cards.Count;
                _phase = 6;
                Log.Info("[DemoLab] Entered reward-card observation; three serialized options.");
            }
        }
        if (_phase is 6 or 7 && now - _lastFrame > 100)
        {
            InjectRewards(now); _lastFrame = now;
            if (_phase == 6)
            {
                if (GodotObject.IsInstanceValid(_localChoice) && _localChoice!.IsInsideTree())
                {
                    var holder = _localChoice.GetCardHolder(GetLocalCards()[0]);
                    AccessTools.Method(typeof(NCardRewardSelectionScreen), "SelectCard").Invoke(_localChoice, [holder]);
                }
                if (Players[0].Deck.Cards.Count == _deckBefore)
                    Log.Info("[DemoLab] PASS: observing reward options did not change deck; local reward click blocked.");
                else Log.Error("[DemoLab] FAIL: deck changed during reward observation.");
                _phase = 7;
                _rewardStarted = now;
                Log.Info("[DemoLab] Ready for screenshot of read-only reward choices.");
                TaskHelper.RunSafely(Capture("reward-choices"));
            }
        }
        if (_phase == 7 && !_cardChoiceVerified && now - _rewardStarted > 3000)
        {
            var view = RewardDemoController.Current!.GetNode<Control>("ReadOnlyRewards");
            int cards = Descendants<NCard>(view).Count();
            int labels = view.GetChildren().SelectMany(c => c.GetChildren()).OfType<Label>().Count();
            int buttons = Descendants<BaseButton>(view).Count(b => b.Visible);
            int avatars = Descendants<TextureButton>(RewardDemoController.Current).Count(b => b.Name.ToString().StartsWith("AvatarDemoEye_") && b.Visible);
            if (cards == 3 && labels == 0 && buttons == 1 && avatars == 0)
                Log.Info("[DemoLab] PASS: reward demo contains three cards and one exit eye, no Skip label or duplicate avatar eyes.");
            else Log.Error($"[DemoLab] FAIL: reward UI cards={cards} labels={labels} buttons={buttons} avatars={avatars}.");
            _cardChoiceVerified = true;
        }
        if (_phase == 7 && !_skipVerified && now - _rewardStarted > 6000)
        {
            if (Players[0].Deck.Cards.Count == _deckBefore)
                Log.Info("[DemoLab] PASS: friend's selected card/skip snapshots do not modify observer deck.");
            else Log.Error("[DemoLab] FAIL: reward snapshots modified deck.");
            _skipVerified = true;
        }
        if (_phase == 7 && now - _rewardStarted > 7000)
        {
            var exit = Descendants<TextureButton>(RewardDemoController.Current!.GetNode("ReadOnlyRewards")).Single();
            exit.EmitSignal(BaseButton.SignalName.Pressed);
            if (!RewardDemoController.IsViewing && _localChoice!.IsInsideTree() && Players[0].Deck.Cards.Count == _deckBefore)
                Log.Info("[DemoLab] PASS: exit eye restores original local reward screen without claiming cards.");
            else Log.Error("[DemoLab] FAIL: exit eye did not restore original reward screen.");
            _phase = 8; _rewardStarted = now;
        }
        if (_phase == 7 && !_rewardSwitchVerified && now - _rewardStarted > 4500)
        {
            var controller = RewardDemoController.Current!;
            var exit = Descendants<TextureButton>(controller.GetNode("ReadOnlyRewards")).Single();
            exit.EmitSignal(BaseButton.SignalName.Pressed);
            controller.GetNode<TextureButton>("AvatarDemoEye_3").EmitSignal(BaseButton.SignalName.Pressed);
            bool third = RewardDemoController.ObservedPlayerId == 3;
            bool thirdTint = SelectedAvatarTint(3) && !SelectedAvatarTint(2);
            exit.EmitSignal(BaseButton.SignalName.Pressed);
            controller.GetNode<TextureButton>("AvatarDemoEye_2").EmitSignal(BaseButton.SignalName.Pressed);
            bool second = RewardDemoController.ObservedPlayerId == 2;
            if (third && thirdTint && second && SelectedAvatarTint(2) && !SelectedAvatarTint(3)
                && Players[0].Deck.Cards.Count == _deckBefore)
                Log.Info("[DemoLab] PASS: three-player card rewards switch via exit and avatar 2 -> 3 -> 2; tint follows target, no cards claimed.");
            else Log.Error("[DemoLab] FAIL: three-player reward switch or avatar tint.");
            _rewardSwitchVerified = true;
        }
        if (_phase == 8 && now - _lastFrame > 100)
        {
            Net.Inject(new DemoRewardMessage
            {
                ProtocolVersion = 1, Sequence = ++_sequence, Location = RunManager.Instance.DebugOnlyGetState()!.RunLocation,
                Kind = DemoRewardKind.Rewards, Cards = [], Labels = ["20 gold", "Card reward"], Hovered = 1, Chosen = -1,
            }, 2);
            _lastFrame = now;
            if (!RewardDemoController.IsViewing)
            {
                RewardDemoController.Current!.Enter(2);
                TaskHelper.RunSafely(Capture("reward-list"));
            }
        }
        if (_phase == 8 && now - _rewardStarted > 2500)
        {
            var controller = RewardDemoController.Current!;
            var exit = Descendants<TextureButton>(controller.GetNode("ReadOnlyRewards")).Single();
            var avatarEye = controller.GetNode<TextureButton>("AvatarDemoEye_2");
            var thirdEye = controller.GetNode<TextureButton>("AvatarDemoEye_3");
            thirdEye.EmitSignal(BaseButton.SignalName.Pressed);
            bool switched = RewardDemoController.ObservedPlayerId == 3 && thirdEye.Modulate.G > thirdEye.Modulate.R && avatarEye.Modulate == Colors.White;
            avatarEye.EmitSignal(BaseButton.SignalName.Pressed);
            if (switched && RewardDemoController.ObservedPlayerId == 2 && avatarEye.Modulate.G > avatarEye.Modulate.R && thirdEye.Modulate == Colors.White)
                Log.Info("[DemoLab] PASS: three-player reward-list avatar eyes switch 2 -> 3 -> 2 and transfer green tint.");
            else Log.Error("[DemoLab] FAIL: reward-list target/tint switch.");
            if (!exit.Visible && avatarEye.Visible)
                Log.Info("[DemoLab] PASS: reward list offers only the eye beside the friend's avatar.");
            else Log.Error("[DemoLab] FAIL: reward list has missing/duplicate exit eyes.");
            avatarEye.EmitSignal(BaseButton.SignalName.Pressed);
            controller.Enter(2);
            controller._Input(new InputEventKey { Keycode = Key.Escape, Pressed = true });
            if (!RewardDemoController.IsViewing && _localChoice!.IsInsideTree())
                Log.Info("[DemoLab] PASS: avatar toggle and Escape leave reward demo and preserve local choices.");
            else Log.Error("[DemoLab] FAIL: Escape did not close reward view.");
            _phase = 9;
        }
    }
    private List<CardModel> _localCards = [];
    private static bool SelectedAvatarTint(ulong id)
    {
        var row = Descendants<MegaCrit.Sts2.Core.Nodes.Multiplayer.NMultiplayerPlayerState>(NRun.Instance!.GlobalUi.MultiplayerPlayerContainer)
            .Single(s => s.Player.NetId == id);
        var tint = row.GetNode<TextureRect>("%CharacterIcon").SelfModulate;
        return tint.G > tint.R;
    }
    private static IEnumerable<T> Descendants<T>(Node root) where T : Node
    {
        foreach (var child in root.GetChildren())
        {
            if (child is T match) yield return match;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }
    private List<CardModel> GetLocalCards() => _localCards;
    private async Task TestDeadObserver()
    {
        await CreatureCmd.Kill(Players[0].Creature, true);
        await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
        DemoController.Current?.ExitDemo();
        bool entered = DemoController.Current?.EnterDemo(2) == true;
        bool visible = DemoController.Current?.IsVisibleInTree() == true;
        if (!Players[0].Creature.IsAlive && Players[1].Creature.IsAlive && entered && visible && CountMirrors() > 0)
        {
            Log.Info("[DemoLab] PASS: dead local player re-entered demo of living friend; hand remains visible.");
            await Capture("dead-observer");
        }
        else Log.Error($"[DemoLab] FAIL: dead observer entered={entered} visible={visible}.");
    }
    private async Task Capture(string name)
    {
        await ToSignal(GetTree().CreateTimer(.5), SceneTreeTimer.SignalName.Timeout);
        string path = Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath())!, "..", $"lab-{name}.png");
        GetViewport().GetTexture().GetImage().SavePng(path);
        Log.Info($"[DemoLab] Screenshot saved: {name}.");
    }
    private async Task FinishForRewards()
    {
        try
        {
            foreach (var creature in CombatManager.Instance.DebugOnlyGetState()!.Enemies.ToArray())
                await CreatureCmd.Kill(creature, true);
            await CombatManager.Instance.CheckWinCondition();
            await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(3), SceneTreeTimer.SignalName.Timeout);
            foreach (var model in ModelDb.AllCards.Where(c => c.Pool == Players[1].Character.CardPool).Take(3))
            {
                var detached = model.ToMutable(); detached.Owner = Players[1];
                _rewardCards.Add(detached.ToSerializable());
            }
            foreach (var model in ModelDb.AllCards.Where(c => c.Pool == Players[2].Character.CardPool).Take(3))
            {
                var detached = model.ToMutable(); detached.Owner = Players[2];
                _thirdRewardCards.Add(detached.ToSerializable());
            }
            _localCards = ModelDb.AllCards.Where(c => c.Pool == Players[0].Character.CardPool).Take(3)
                .Select(c => { var card = c.ToMutable(); card.Owner = Players[0]; return card; }).ToList();
            _localChoice = NCardRewardSelectionScreen.ShowScreen(_localCards.Select(c => new CardCreationResult(c)).ToList(), []);
            if (_localChoice != null) _ = _localChoice.OptionSelected();
            Log.Info("[DemoLab] Combat completed for reward-view verification; no run saving.");
        }
        catch (Exception e) { Log.Error($"[DemoLab] REWARD TEST FAILED: {e}"); _phase = 99; }
    }
    private void InjectRewards(ulong now)
    {
        var location = RunManager.Instance.DebugOnlyGetState()!.RunLocation;
        Net.Inject(new DemoRewardMessage
        {
            ProtocolVersion = 1, Sequence = ++_sequence, Location = location, Kind = DemoRewardKind.CardChoice,
            Cards = _rewardCards, Labels = ["Пропустить"], Hovered = 1,
            Chosen = _rewardStarted == 0 || now - _rewardStarted < 3500 ? -1 : now - _rewardStarted < 5000 ? 1 : _rewardCards.Count,
        }, 2);
        Net.Inject(new PeerInputMessage { screenType = NetScreenType.CardSelection, netMousePos = new(0, .1f) }, 2);
        Net.Inject(new DemoRewardMessage
        {
            ProtocolVersion = 1, Sequence = _sequence, Location = location, Kind = DemoRewardKind.CardChoice,
            Cards = _thirdRewardCards, Labels = ["Пропустить"], Hovered = 0, Chosen = -1,
        }, 3);
    }
    private static int CountMirrors() => DemoController.Current!.GetNode("DemoView/ReadOnlyHand").GetChildCount();
    private string Fingerprint() => string.Join(";", Players.Select(p =>
        $"{p.NetId}:{p.Creature.CurrentHp}:{p.Creature.Block}:{p.PlayerCombatState!.Energy}:" +
        string.Join(",", p.PlayerCombatState.AllPiles.Select(pile => $"{pile.Type}:{string.Join('/', pile.Cards.Select(c => c.Id))}")) +
        string.Join(",", p.Creature.Powers.Select(power => $"{power.Id}:{power.Amount}")) +
        string.Join(",", p.Relics.Select(r => r.Id))));
}

// Only in the lab assembly. No real sockets or remote players. Received visual
// packets are serialized/deserialized before delivery to exercise the wire format.
public class LabNetService : INetGameService
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = [];
    public ulong NetId => 1;
    public bool IsConnected => true;
    public bool IsGameLoading { get; private set; }
    public NetGameType Type => NetGameType.Host;
    public PlatformType Platform => PlatformType.None;
    public event Action<NetErrorInfo>? Disconnected { add { } remove { } }
    public void SendMessage<T>(T message, ulong playerId) where T : INetMessage { }
    public void SendMessage<T>(T message) where T : INetMessage { }
    public void RegisterMessageHandler<T>(MessageHandlerDelegate<T> handler) where T : INetMessage
    {
        if (!_handlers.TryGetValue(typeof(T), out var handlers)) _handlers[typeof(T)] = handlers = [];
        handlers.Add(handler);
    }
    public void UnregisterMessageHandler<T>(MessageHandlerDelegate<T> handler) where T : INetMessage
    { if (_handlers.TryGetValue(typeof(T), out var handlers)) handlers.Remove(handler); }
    public void Inject<T>(T message, ulong sender) where T : INetMessage, new()
    {
        var writer = new PacketWriter(); message.Serialize(writer);
        var reader = new PacketReader(); reader.Reset(writer.Buffer.AsSpan(0, writer.BytePosition).ToArray());
        var copy = new T(); copy.Deserialize(reader);
        if (_handlers.TryGetValue(typeof(T), out var handlers))
            foreach (var handler in handlers.ToArray()) ((MessageHandlerDelegate<T>)handler)(copy, sender);
    }
    public void Update() { }
    public void Disconnect(NetError reason, bool now = false) { }
    public ConnectionStats? GetStatsForPeer(ulong peerId) => null;
    public void SetGameLoading(bool isLoading) => IsGameLoading = isLoading;
    public void SetBufferMessages(bool bufferMessages) { }
    public string? GetRawLobbyIdentifier() => null;
}
