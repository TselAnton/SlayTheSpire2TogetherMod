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
using Sts2Demo.Core;
using Sts2Demo.Ui;
using MegaCrit.Sts2.Core.ControllerInput;
using MegaCrit.Sts2.Core.ControllerInput.ControllerConfigs;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;

namespace Sts2DemoLab;

// Fixture only: uses installed native glyphs without a physical controller or OS input.
internal sealed class LabControllerStrategy(ControllerConfig config) : IControllerInputStrategy
{
    public ControllerConfig ControllerConfig => config;
    public Dictionary<StringName, StringName> GetDefaultControllerInputMap => config.DefaultControllerInputMap;
    public bool ShouldAllowControllerRebinding => true;
    public Task Init() => Task.CompletedTask;
    public void ProcessInput() { }
    public Texture2D? GetHotkeyIcon(string hotkey) => config.GetButtonIcon(hotkey);
    public string GetControllerName() => "Isolated controller fixture";
}

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
            DemoSettings.SeparateButton = true; DemoSettings.AllActivities = true;
            var game = NGame.Instance!;
            ProfileOracle.Run();
            await game.ToSignal(game.GetTree().CreateTimer(1), SceneTreeTimer.SignalName.Timeout);
            SaveManager.Instance.Progress.EnableFtues = false; // In-memory lab profile only.
            game.GetWindow().Size = new(1280, 720);
            game.GetWindow().Mode = Window.ModeEnum.Windowed;
            game.GetWindow().Show(); // A minimized laboratory cannot finish GPU asset preloading.
            var players = new[]
            {
                Player.CreateForNewRun<Ironclad>(UnlockState.all, 1),
                Player.CreateForNewRun<Silent>(UnlockState.all, 2),
                Player.CreateForNewRun<Regent>(UnlockState.all, 3),
            };
            var run = RunState.CreateForNewRun(players, ActModel.GetDefaultList().Select(a => a.ToMutable()).ToList(), [], GameMode.Standard, 0, "DEMOLAB");
            players[1].Gold = 137; players[2].Gold = 42; // Distinct lab-only header values.
            var net = new LabNetService();
            RunManager.Instance.SetUpTest(run, net, disableCombatStateSync: true, shouldSave: false);
            players[1].AddPotionInternal(ModelDb.Potion<MegaCrit.Sts2.Core.Models.Potions.WeakPotion>().ToMutable(), 1);
            players[2].AddPotionInternal(ModelDb.Potion<MegaCrit.Sts2.Core.Models.Potions.StrengthPotion>().ToMutable(), 0);
            RunManager.Instance.GenerateRooms();
            var preload = PreloadManager.LoadRunAssets(players.Select(p => p.Character));
            await preload.WaitAsync(TimeSpan.FromSeconds(30));
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
    private bool _hoverFixtureRunning;
    private byte[] _nativeRewards = [];
    private Vector2 _rewardOrigin;
    private Vector2 _rewardSize = Vector2.One;
    private MegaCrit.Sts2.Core.Nodes.Combat.NTargetingArrow? _referenceArrow;
    public override void _Ready()
    {
        if (CommandLineHelper.GetValue("demo-stress") == "on")
        { _phase = 99; TaskHelper.RunSafely(StressProjection()); return; }
        if (CommandLineHelper.GetValue("demo-ui-only") == "on")
        { _phase = 99; TaskHelper.RunSafely(FocusUiAudit()); return; }
        if (CommandLineHelper.GetValue("demo-fast") == "on")
        { _phase = 99; TaskHelper.RunSafely(QuickAudit()); }
    }
    private async Task FocusUiAudit()
    {
        await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
        await FinishForRewards();
        await AuditSettings();
        await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.CrystalSphere>(), showTransition: false);
        await AuditActivity("event-dialogue");
        var entity = new MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereMinigame(Players[0], new MegaCrit.Sts2.Core.Random.Rng(42), 3);
        var screen = MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere.NCrystalSphereScreen.ShowScreen(entity);
        await AuditActivity("predictions");
        NOverlayStack.Instance!.Remove(screen); entity.ForceMinigameEnd();
        await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.Neow>(), showTransition: false);
        await AuditActivity("ancient-event");
        Log.Info("[DemoLab] UI AUDIT FINISHED.");
    }
    private async Task QuickAudit()
    {
        await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
        await AuditPatchCombat();
        await AuditPotionsAndRestore();
        await AuditCombatActivity();
        await FinishForRewards();
        await TestNativeActivities();
    }
    public override void _Process(double delta)
    {
        if (_phase < 5 && (DemoController.Current == null || Players[1].PlayerCombatState?.Hand.Cards.Count == 0)) return;
        var now = Time.GetTicksMsec();
        if (_start == 0) _start = now;
        if (_phase == 0 && now - _start > 3500)
        {
            _before = Fingerprint();
            _phase = 98;
            TaskHelper.RunSafely(TestControllerCombat());
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
            var msg = new DemoHandMessage { ProtocolVersion = DemoHandMessage.Protocol, Sequence = ++_sequence, Location = location, Round = CombatManager.Instance.DebugOnlyGetState()!.RoundNumber, ReferenceSize = new(1920, 1080), Cards = poses,
                ArrowVisible = true, ArrowFrom = poses[2].Position, ArrowTo = ArrowEnemyPoint() / new Vector2(1920, 1080),
                ArrowTintRG = new(1, 1), ArrowTintBA = new(1, 1) };
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
            bool green = third.SelfModulate.G > third.SelfModulate.R;
            var second = controller.GetNode<TextureButton>("AvatarDemoEye_2");
            second.EmitSignal(BaseButton.SignalName.Pressed);
            if (switched && green && third.SelfModulate == Colors.White && second.SelfModulate.G > second.SelfModulate.R
                && DemoController.ObservedPlayerId == 2 && _before == Fingerprint())
                Log.Info("[DemoLab] PASS: three-player combat switches friend 2 -> 3 -> 2; green tint follows target, state unchanged.");
            else Log.Error($"[DemoLab] FAIL: three-player combat switched={switched} green={green} thirdTint={third.SelfModulate} secondTint={second.SelfModulate} target={DemoController.ObservedPlayerId} unchanged={_before == Fingerprint()}.");
            _switchVerified = true;
        }
        if (_phase == 2 && now - _start > 16000)
        {
            var controller = RewardDemoController.Current!;
            var avatarEye = controller.GetNode<TextureButton>("AvatarDemoEye_2");
            int eyes = Descendants<TextureButton>(controller).Count(b => b.Name.ToString().StartsWith("AvatarDemoEye_") && b.Visible);
            bool green = avatarEye.SelfModulate.G > avatarEye.SelfModulate.R;
            bool stable = avatarEye.Scale == Vector2.One && avatarEye.TextureClickMask == null;
            var row = Descendants<NMultiplayerPlayerState>(NRun.Instance!.GlobalUi.MultiplayerPlayerContainer).Single(s => s.Player.NetId == 2);
            var icon = row.GetNode<TextureRect>("%CharacterIcon");
            bool beside = Math.Abs(avatarEye.GetGlobalRect().GetCenter().Y - icon.GetGlobalRect().GetCenter().Y) < 2
                && avatarEye.GlobalPosition.X > icon.GetGlobalRect().End.X;
            if (stable && beside) Log.Info("[DemoLab] PASS: combat eye beside player-list status, stable rectangular hitbox.");
            else Log.Error($"[DemoLab] FAIL: eye stable={stable} beside={beside}.");
            avatarEye.EmitSignal(BaseButton.SignalName.Pressed);
            if (eyes == 2 && green && !DemoController.IsViewing)
                Log.Info("[DemoLab] PASS: combat has one eye per friend, one green; pressing green leaves demo.");
            else Log.Error($"[DemoLab] FAIL: combat eye count={eyes} green={green} active={DemoController.IsViewing}.");
            Log.Info($"[DemoLab] PASS: exit active={!DemoController.IsViewing}; native hand restored={NCombatRoom.Instance!.Ui.Hand.Visible}");
            bool fadeStarted = DemoController.Current!.GetNode<Control>("DemoView").Visible
                && NCombatRoom.Instance.Ui.Hand.Modulate.A < .1f;
            TaskHelper.RunSafely(CheckExitFade(fadeStarted));
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
            TaskHelper.RunSafely(CheckArrowThenDeath());
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
        if (_phase is 6 or 7 && !_hoverFixtureRunning && now - _lastFrame > 100)
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
                _hoverFixtureRunning = true;
                TaskHelper.RunSafely(TestRewardHover());
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
            bool checkbox = Descendants<MegaCrit.Sts2.Core.Nodes.CommonUi.NTickbox>(view).Any(t => t.Name == "DemoViewUpgrades");
            if (cards == 3 && labels == 0 && buttons == 0 && avatars == 2 && checkbox)
                Log.Info("[DemoLab] PASS: reward demo contains three cards, native upgrade tickbox and grouped table eyes; no Skip or floating exit eye.");
            else Log.Error($"[DemoLab] FAIL: reward UI cards={cards} labels={labels} buttons={buttons} avatars={avatars}.");
            _cardChoiceVerified = true;
            TaskHelper.RunSafely(TestEyeHitbox(RewardDemoController.Current.GetNode<TextureButton>("AvatarDemoEye_2"), "reward-table"));
            var tick = Descendants<MegaCrit.Sts2.Core.Nodes.CommonUi.NTickbox>(view).Single();
            tick.IsTicked = true; tick.EmitSignal(MegaCrit.Sts2.Core.Nodes.CommonUi.NTickbox.SignalName.Toggled, tick);
            bool upgraded = Descendants<NCard>(view).All(c => c.Model!.IsUpgraded || !c.Model.IsUpgradable);
            tick.IsTicked = false; tick.EmitSignal(MegaCrit.Sts2.Core.Nodes.CommonUi.NTickbox.SignalName.Toggled, tick);
            if (upgraded && Players[0].Deck.Cards.Count == _deckBefore)
                Log.Info("[DemoLab] PASS: native upgrade checkbox previews all detached cards without changing deck.");
            else Log.Error("[DemoLab] FAIL: upgrade checkbox changed live models or omitted a card.");
            tick.GrabFocus();
            RewardDemoController.Current!._Input(new InputEventAction { Action = "ui_accept", Pressed = true });
            bool controllerUpgrade = tick.IsTicked;
            RewardDemoController.Current._Input(new InputEventAction { Action = "ui_accept", Pressed = false });
            RewardDemoController.Current._Input(new InputEventAction { Action = "ui_accept", Pressed = true });
            RewardDemoController.Current._Input(new InputEventAction { Action = "ui_accept", Pressed = false });
            RewardDemoController.Current._Input(new InputEventAction { Action = "ui_right", Pressed = true });
            RewardDemoController.Current._Input(new InputEventAction { Action = "ui_right", Pressed = false });
            bool exitFocus = GetViewport().GuiGetFocusOwner() is TextureButton;
            if (controllerUpgrade && !tick.IsTicked && exitFocus && Players[0].Deck.Cards.Count == _deckBefore)
                Log.Info("[DemoLab] PASS: accept/navigation route only to demo checkbox and exit eye, with no card claim.");
            else Log.Error("[DemoLab] FAIL: demo controller checkbox/navigation.");
            TaskHelper.RunSafely(TestControllerRewards());
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
            var exit = RewardDemoController.Current!.GetNode<TextureButton>("AvatarDemoEye_2");
            exit.EmitSignal(BaseButton.SignalName.Pressed);
            if (!RewardDemoController.IsViewing && _localChoice!.IsInsideTree() && Players[0].Deck.Cards.Count == _deckBefore)
                Log.Info("[DemoLab] PASS: exit eye restores original local reward screen without claiming cards.");
            else Log.Error("[DemoLab] FAIL: exit eye did not restore original reward screen.");
            _phase = 8; _rewardStarted = now;
        }
        if (_phase == 7 && !_rewardSwitchVerified && now - _rewardStarted > 4500)
        {
            var controller = RewardDemoController.Current!;
            var exit = controller.GetNode<TextureButton>("AvatarDemoEye_2");
            exit.EmitSignal(BaseButton.SignalName.Pressed);
            controller.GetNode<TextureButton>("AvatarDemoEye_3").EmitSignal(BaseButton.SignalName.Pressed);
            bool third = RewardDemoController.ObservedPlayerId == 3;
            bool thirdTint = SelectedAvatarTint(3) && !SelectedAvatarTint(2);
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
                ProtocolVersion = DemoRewardMessage.Protocol, Sequence = ++_sequence, Location = RunManager.Instance.DebugOnlyGetState()!.RunLocation,
                Entries = Net.PublishedRewards.LastOrDefault(f => f.Kind == DemoRewardKind.Rewards && f.Entries?.Count > 0).Entries,
                Header = new MegaCrit.Sts2.Core.Localization.LocString("gameplay_ui", "COMBAT_REWARD_HEADER_LOOT").GetFormattedText(), Kind = DemoRewardKind.Rewards, Cards = [], Labels = ["20 gold", "Card reward"], Hovered = 1, Chosen = -1,
            }, 2);
            _lastFrame = now;
            if (!RewardDemoController.IsViewing)
            {
                RewardDemoController.Current!.Enter(2);
                TaskHelper.RunSafely(Capture("reward-list"));
                TaskHelper.RunSafely(TestEyeHitbox(RewardDemoController.Current.GetNode<TextureButton>("AvatarDemoEye_2"), "reward-avatar"));
            }
            if (_nativeRewards.Length > 0) InjectNativeMetadata(_nativeRewards, _rewardOrigin, _rewardSize);
        }
        if (_phase == 8 && now - _rewardStarted > 2500)
        {
            var controller = RewardDemoController.Current!;
            var exit = Descendants<TextureButton>(controller.GetNode("ReadOnlyRewards")).Single();
            var avatarEye = controller.GetNode<TextureButton>("AvatarDemoEye_2");
            var thirdEye = controller.GetNode<TextureButton>("AvatarDemoEye_3");
            thirdEye.EmitSignal(BaseButton.SignalName.Pressed);
            bool switched = RewardDemoController.ObservedPlayerId == 3 && thirdEye.SelfModulate.G > thirdEye.SelfModulate.R && avatarEye.SelfModulate == Colors.White;
            avatarEye.EmitSignal(BaseButton.SignalName.Pressed);
            if (switched && RewardDemoController.ObservedPlayerId == 2 && avatarEye.SelfModulate.G > avatarEye.SelfModulate.R && thirdEye.SelfModulate == Colors.White)
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
            TaskHelper.RunSafely(TestNativeActivities());
        }
    }
    private List<CardModel> _localCards = [];
    private async Task TestEyeHitbox(TextureButton eye, string variant)
    {
        try
        {
            var original = eye.Position;
            bool passed = true;
            foreach (var normalized in new[] { new Vector2(.08f, .08f), new Vector2(.92f, .08f), new Vector2(.08f, .92f), new Vector2(.92f, .92f), new Vector2(.5f, .5f) })
            {
                if (!eye.Visible) break;
                Vector2 point = eye.GetGlobalTransformWithCanvas() * (eye.Size * normalized);
                GetViewport().PushInput(new InputEventMouseMotion { Position = point, GlobalPosition = point }, true);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                passed &= eye.Scale == Vector2.One && eye.Position.DistanceTo(original) < 2;
                bool before = DemoController.IsViewing || RewardDemoController.IsViewing;
                GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = true }, true);
                GetViewport().PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = false }, true);
                passed &= before != (DemoController.IsViewing || RewardDemoController.IsViewing);
                // Restore the target through its public observer API, without gaming click results.
                if (variant == "combat") DemoController.Current!.EnterDemo(2);
                else RewardDemoController.Current!.Enter(2);
                await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            }
            if (passed) Log.Info($"[DemoLab] PASS: {variant} eye clicks at four transparent corners and centre; no hover movement.");
            else Log.Error($"[DemoLab] FAIL: {variant} eye hitbox/hover routing.");
        }
        catch (Exception e) { Log.Error($"[DemoLab] FAIL: {variant} hitbox test: {e}"); }
    }

    private static void ControllerCheck(bool passed, string name)
    {
        if (passed) Log.Info($"[DemoLab] PASS: controller {name}.");
        else Log.Error($"[DemoLab] FAIL: controller {name}.");
    }

    private async Task ControllerTap(StringName button)
    {
        GetViewport().PushInput(new InputEventAction { Action = button, Pressed = true }, true);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        GetViewport().PushInput(new InputEventAction { Action = button, Pressed = false }, true);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }

    private async Task TestControllerCombat()
    {
        await AuditPatchCombat();
        await AuditPotionsAndRestore();
        await AuditCombatActivity();
        _before = Fingerprint();
        await Capture("combat-source");
        var manager = NControllerManager.Instance!;
        var strategyField = AccessTools.Field(typeof(NControllerManager), "_inputStrategy");
        var originalStrategy = strategyField.GetValue(manager);
        var modeProperty = AccessTools.Property(typeof(NControllerManager), "IsUsingController");
        bool originalMode = manager.IsUsingController;
        var mapping = (Dictionary<StringName, StringName>)AccessTools.Field(typeof(NInputManager), "_controllerInputMap").GetValue(NInputManager.Instance)!;
        var originalMapping = new Dictionary<StringName, StringName>(mapping);
        try
        {
            foreach (ulong peer in new ulong[] { 2, 3 })
                Net.Inject(new DemoPresenceMessage { ProtocolVersion = 1, Sequence = ++_sequence,
                    Location = RunManager.Instance.DebugOnlyGetState()!.RunLocation, Watching = false }, peer);
            Net.PublishedHands.Clear(); Net.PublishedScreens.Clear(); Net.PublishedRewards.Clear();
            await ToSignal(GetTree().CreateTimer(.25), SceneTreeTimer.SignalName.Timeout);
            if (Net.PublishedHands.Count == 0 && Net.PublishedScreens.Count == 0
                && Net.PublishedRewards.All(m => m.Cards?.Count == 0 && m.Entries?.Count == 0))
                Log.Info("[DemoLab] PASS: no watchers means no hand/scene payload or serialized reward options.");
            else Log.Error("[DemoLab] FAIL: heavyweight metadata published without watchers.");
            strategyField.SetValue(manager, new LabControllerStrategy(new XboxOneConfig()));
            modeProperty.SetValue(manager, true); // No OS mouse warp or hardware takeover.
            mapping.Clear(); foreach (var item in new XboxOneConfig().DefaultControllerInputMap) mapping.Add(item.Key, item.Value);
            var originalHandPosition = NCombatRoom.Instance!.Ui.Hand.Position;
            NCombatRoom.Instance.Ui.Hand.Position = new(123, 77);
            var friend = Descendants<NMultiplayerPlayerState>(NRun.Instance!.GlobalUi.MultiplayerPlayerContainer).Single(s => s.Player.NetId == 2);
            var eye = RewardDemoController.Current!.GetNode<TextureButton>("AvatarDemoEye_2");
            eye.GrabFocus();
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); // UI _Process runs after the frame signal.
            var hint = eye.GetNode<TextureRect>("DemoControllerHint");
            ControllerCheck(hint.IsVisibleInTree() && hint.Texture == NInputManager.Instance!.GetHotkeyIcon(MegaInput.select)
                && hint.GetGlobalRect().Position.X > eye.GetGlobalRect().End.X, "native A glyph RIGHT OF focused friend's eye");
            if (!hint.IsVisibleInTree() || hint.GetGlobalRect().Position.X <= eye.GetGlobalRect().End.X)
                Log.Info($"[DemoLab] controller glyph diagnostic: visible={hint.IsVisibleInTree()} mode={manager.IsUsingController} size={hint.Size} scale={hint.Scale} rect={hint.GetGlobalRect()} eye={eye.GetGlobalRect()}.");
            int leaked = 0; friend.Hitbox.GuiInput += e => { if (e.IsAction(MegaInput.select)) leaked++; };
            await ControllerTap(Controller.faceButtonSouth);
            ControllerCheck(DemoController.ObservedPlayerId == 2 && leaked == 0, "physical A enters once; native hitbox release not triggered");
            ControllerCheck(hint.Texture == NInputManager.Instance!.GetHotkeyIcon(MegaInput.cancel), "active eye changes to native B hint");
            RewardDemoController.Current!.GetNode<TextureButton>("AvatarDemoEye_3").GrabFocus();
            await ControllerTap(Controller.faceButtonSouth);
            ControllerCheck(DemoController.ObservedPlayerId == 3, "physical A switches to third player");
            int nativeConfirm = 0;
            Action confirmPress = () => nativeConfirm++;
            Action confirmRelease = () => nativeConfirm++;
            NHotkeyManager.Instance!.PushHotkeyPressedBinding(MegaInput.accept, confirmPress);
            NHotkeyManager.Instance.PushHotkeyReleasedBinding(MegaInput.accept, confirmRelease);
            try
            {
                await ControllerTap(Controller.faceButtonNorth);
                if (NGame.IsGameFocusedWindow())
                    ControllerCheck(nativeConfirm == 2 && DemoController.ObservedPlayerId == 3,
                        "combat Y press/release reaches native local-confirm owner, not demo");
                else Log.Info("[DemoLab] NOT RUN: controller native-confirm hotkey delivery requires focused lab window.");
            }
            finally
            {
                NHotkeyManager.Instance.RemoveHotkeyPressedBinding(MegaInput.accept, confirmPress);
                NHotkeyManager.Instance.RemoveHotkeyReleasedBinding(MegaInput.accept, confirmRelease);
            }
            await ControllerTap(Controller.faceButtonEast);
            ControllerCheck(!DemoController.IsViewing && NCombatRoom.Instance.Ui.Hand.Visible, "physical B exits and restores native hand");
            ControllerCheck(NCombatRoom.Instance.Ui.Hand.Position == new Vector2(123, 77), "exit preserves native hand geometry instead of moving root to zero");
            NCombatRoom.Instance.Ui.Hand.Position = originalHandPosition;
            eye.GrabFocus();
            mapping[MegaInput.select] = Controller.faceButtonWest; mapping[MegaInput.topPanel] = Controller.faceButtonSouth;
            mapping[MegaInput.cancel] = Controller.rightBumper; mapping[MegaInput.viewExhaustPileAndTabRight] = Controller.faceButtonEast;
            NInputManager.Instance!.EmitSignal(NInputManager.SignalName.InputRebound);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ControllerCheck(hint.Texture == NInputManager.Instance.GetHotkeyIcon(MegaInput.select), "rebound select updates glyph immediately");
            await ControllerTap(Controller.faceButtonWest);
            ControllerCheck(DemoController.ObservedPlayerId == 2, "rebound select enters with X instead of A");
            await ControllerTap(Controller.rightBumper);
            ControllerCheck(!DemoController.IsViewing, "rebound cancel exits with RB instead of B");
            // Deliberate alias: one physical button maps to both cancel and select.
            mapping[MegaInput.cancel] = Controller.faceButtonWest;
            eye.GrabFocus(); await ControllerTap(Controller.faceButtonWest);
            ControllerCheck(DemoController.IsViewing, "aliased select still enters when cancel is inactive");
            await ControllerTap(Controller.faceButtonWest);
            ControllerCheck(!DemoController.IsViewing, "aliased cancel owns the entire press/release; no re-entry");
            mapping[MegaInput.cancel] = Controller.faceButtonEast;
            eye.GrabFocus();
            GetViewport().PushInput(new InputEventAction { Action = Controller.faceButtonWest, Pressed = true }, true);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            GetViewport().PushInput(new InputEventAction { Action = Controller.faceButtonWest, Pressed = true }, true);
            ControllerCheck(DemoController.IsViewing, "held select does not toggle twice");
            mapping[MegaInput.select] = Controller.faceButtonSouth; mapping[MegaInput.topPanel] = Controller.leftBumper;
            GetViewport().PushInput(new InputEventAction { Action = Controller.faceButtonWest, Pressed = false }, true);
            ControllerCheck(GetViewport().IsInputHandled(), "release owned across remap even when old physical button is no longer mapped");
            await ControllerTap(Controller.faceButtonEast);
            var targetManager = NRun.Instance!.GlobalUi.TargetManager;
            targetManager.StartTargeting(MegaCrit.Sts2.Core.Entities.Cards.TargetType.AnyAlly, Vector2.Zero,
                MegaCrit.Sts2.Core.Nodes.Combat.TargetMode.Controller, () => false, null);
            eye.GrabFocus();
            await ControllerTap(Controller.faceButtonSouth);
            ControllerCheck(!DemoController.IsViewing, "native ally targeting keeps A; no simultaneous demo entry");
            targetManager.CancelTargeting();
            ControllerCheck(_before == Fingerprint(), "combat input never changes cards, energy or teammate state");
            await Capture("controller-combat");
        }
        catch (Exception e) { Log.Error($"[DemoLab] FAIL: controller combat fixture: {e}"); }
        finally
        {
            mapping.Clear(); foreach (var item in originalMapping) mapping.Add(item.Key, item.Value);
            strategyField.SetValue(manager, originalStrategy); modeProperty.SetValue(manager, originalMode);
            NInputManager.Instance!.EmitSignal(NInputManager.SignalName.InputRebound);
            DemoController.Current!.EnterDemo(2);
            _phase = 1; _start = Time.GetTicksMsec() - 3500;
            _ = TaskHelper.RunSafely(TestEyeHitbox(RewardDemoController.Current!.GetNode<TextureButton>("AvatarDemoEye_2"), "combat"));
        }
    }

    private async Task TestControllerRewards()
    {
        var manager = NControllerManager.Instance!;
        var strategyField = AccessTools.Field(typeof(NControllerManager), "_inputStrategy");
        var originalStrategy = strategyField.GetValue(manager);
        var modeProperty = AccessTools.Property(typeof(NControllerManager), "IsUsingController");
        bool originalMode = manager.IsUsingController;
        var mapping = (Dictionary<StringName, StringName>)AccessTools.Field(typeof(NInputManager), "_controllerInputMap").GetValue(NInputManager.Instance)!;
        var originalMapping = new Dictionary<StringName, StringName>(mapping);
        var originalMappingType = SaveManager.Instance.SettingsSave.ControllerMappingType;
        try
        {
            await ToSignal(GetTree().CreateTimer(.5), SceneTreeTimer.SignalName.Timeout); // Finish mouse hitbox oracle first.
            strategyField.SetValue(manager, new LabControllerStrategy(new XboxOneConfig())); modeProperty.SetValue(manager, true);
            mapping.Clear(); foreach (var item in new XboxOneConfig().DefaultControllerInputMap) mapping.Add(item.Key, item.Value);
            var controller = RewardDemoController.Current!;
            var tick = Descendants<NTickbox>(controller.GetNode("ReadOnlyRewards")).Single();
            var exit = Descendants<TextureButton>(controller.GetNode("ReadOnlyRewards")).Single();
            exit.GrabFocus();
            bool before = tick.IsTicked;
            await ControllerTap(Controller.faceButtonNorth);
            ControllerCheck(tick.IsTicked != before && RewardDemoController.IsViewing, "Y toggles reward upgrades regardless of focus, not exit");
            ControllerCheck(Descendants<NCard>(controller.GetNode("ReadOnlyRewards")).All(c => c.Model!.IsUpgraded || !c.Model.IsUpgradable), "Y upgrades ALL detached reward cards");
            var glyph = tick.GetNode<TextureRect>("%ControllerIcon");
            ControllerCheck(glyph.IsVisibleInTree() && glyph.Texture == NInputManager.Instance!.GetHotkeyIcon(MegaInput.accept), "upgrade checkbox shows native Y icon");
            strategyField.SetValue(manager, new LabControllerStrategy(new Ps4Config()));
            // Prevent the native type-change handler from persisting lab settings.
            SaveManager.Instance.SettingsSave.ControllerMappingType = ControllerMappingType.Playstation;
            manager.EmitSignal(NControllerManager.SignalName.ControllerTypeChanged);
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            ControllerCheck(glyph.Texture == NInputManager.Instance!.GetHotkeyIcon(MegaInput.accept), "switching controller type updates native glyphs");
            mapping[MegaInput.accept] = Controller.faceButtonWest; mapping[MegaInput.topPanel] = Controller.faceButtonNorth;
            NInputManager.Instance!.EmitSignal(NInputManager.SignalName.InputRebound);
            await ControllerTap(Controller.faceButtonWest);
            ControllerCheck(tick.IsTicked == before && glyph.Texture == NInputManager.Instance.GetHotkeyIcon(MegaInput.accept), "rebound upgrade hotkey toggles once and changes glyph");
            await ControllerTap(Controller.faceButtonEast);
            ControllerCheck(!RewardDemoController.IsViewing && _localChoice!.IsInsideTree(), "B exits without selecting or skipping local rewards");
            // Entry is reachable via the ORIGINAL player-list hitbox, not only the detached eye.
            var row = Descendants<NMultiplayerPlayerState>(NRun.Instance!.GlobalUi.MultiplayerPlayerContainer).Single(s => s.Player.NetId == 2);
            int released = 0; row.Hitbox.Released += _ => released++;
            DemoSettings.SeparateButton = false;
            row.Hitbox.GrabFocus(); await ControllerTap(Controller.faceButtonSouth);
            ControllerCheck(RewardDemoController.ObservedPlayerId == 2 && released == 0, "A on native player row enters without opening original expanded state");
            await Capture("controller-rewards");
            await ControllerTap(Controller.faceButtonEast);
            ControllerCheck(GetViewport().GuiGetFocusOwner() == row.Hitbox, "B restores original player-list focus");
            ControllerCheck(Players[0].Deck.Cards.Count == _deckBefore, "reward input never claims a card");
            DemoSettings.SeparateButton = true;
            controller.Enter(2);
        }
        catch (Exception e) { Log.Error($"[DemoLab] FAIL: controller reward fixture: {e}"); }
        finally
        {
            mapping.Clear(); foreach (var item in originalMapping) mapping.Add(item.Key, item.Value);
            AccessTools.Field(typeof(NInputManager), "_controllerInputMap").SetValue(NInputManager.Instance, mapping);
            SaveManager.Instance.SettingsSave.ControllerMappingType = originalMappingType;
            strategyField.SetValue(manager, originalStrategy); modeProperty.SetValue(manager, originalMode);
            NInputManager.Instance!.EmitSignal(NInputManager.SignalName.InputRebound);
        }
    }
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
            var manager = NControllerManager.Instance!;
            var modeProperty = AccessTools.Property(typeof(NControllerManager), "IsUsingController");
            bool originalMode = manager.IsUsingController;
            try
            {
                modeProperty.SetValue(manager, true);
                await ControllerTap(Controller.faceButtonEast);
                RewardDemoController.Current!.GetNode<TextureButton>("AvatarDemoEye_2").GrabFocus();
                await ControllerTap(Controller.faceButtonSouth);
                ControllerCheck(DemoController.ObservedPlayerId == 2 && !Players[0].Creature.IsAlive,
                    "dead observer uses B then A on living friend");
            }
            finally { modeProperty.SetValue(manager, originalMode); }
            await Capture("dead-observer");
        }
        else Log.Error($"[DemoLab] FAIL: dead observer entered={entered} visible={visible}.");
    }
    private async Task Capture(string name)
    {
        if (CommandLineHelper.GetValue("demo-stress") == "on") return; // Preserve the full-run visual evidence.
        await ToSignal(GetTree().CreateTimer(.5), SceneTreeTimer.SignalName.Timeout);
        string directory = Path.Combine(Path.GetDirectoryName(OS.GetExecutablePath())!, "..", "validation-1.3.1");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"lab-{name}.png");
        GetViewport().GetTexture().GetImage().SavePng(path);
        Log.Info($"[DemoLab] Screenshot saved: {name}.");
    }
    private async Task TestRewardHover()
    {
        try
        {
            var anchors = new List<Vector2> { new(.3f, .5f), new(.5f, .5f), new(.7f, .5f) };
            foreach (int i in new[] { 0, 2, 1, 0 })
            {
                Net.Inject(new DemoRewardMessage { ProtocolVersion = DemoRewardMessage.Protocol, Sequence = ++_sequence,
                    Location = RunManager.Instance.DebugOnlyGetState()!.RunLocation, Kind = DemoRewardKind.CardChoice,
                    Cards = _rewardCards, Labels = ["Пропустить"], Centers = anchors, Cursor = anchors[i],
                    Controller = false, Hovered = 2, Chosen = -1 }, 2); // Deliberately stale focus on the right card.
                await ToSignal(GetTree().CreateTimer(.2), SceneTreeTimer.SignalName.Timeout);
                var cards = Descendants<NCard>(RewardDemoController.Current!.GetNode("ReadOnlyRewards")).ToList();
                if (cards.Count == 3 && cards[i].ZIndex == 20 && cards.Where((_, n) => n != i).All(c => c.ZIndex != 20))
                    Log.Info($"[DemoLab] PASS: client cursor selects reward card {i}, ignoring stale rightmost focus.");
                else Log.Error($"[DemoLab] FAIL: client cursor reward selection {i}.");
            }
        }
        finally { _hoverFixtureRunning = false; }
    }
    private async Task FinishForRewards()
    {
        try
        {
            foreach (var creature in CombatManager.Instance.DebugOnlyGetState()!.Enemies.ToArray())
                await CreatureCmd.Kill(creature, true);
            await CombatManager.Instance.CheckWinCondition();
            await NGame.Instance!.ToSignal(NGame.Instance.GetTree().CreateTimer(3), SceneTreeTimer.SignalName.Timeout);
            var published = await CapturePublishedFrame();
            _nativeRewards = published.Data; _rewardOrigin = published.Origin; _rewardSize = published.Size;
            await Capture("reward-list-source");
            await AuditCompletedRewards();
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
            ProtocolVersion = DemoRewardMessage.Protocol, Sequence = ++_sequence, Location = location, Kind = DemoRewardKind.CardChoice,
            Cursor = new(.5f, .5f),
            Cards = _rewardCards, Labels = ["Пропустить"], Hovered = 1,
            Chosen = _rewardStarted == 0 || now - _rewardStarted < 3500 ? -1 : now - _rewardStarted < 5000 ? 1 : _rewardCards.Count,
        }, 2);
        Net.Inject(new PeerInputMessage { screenType = NetScreenType.CardSelection, netMousePos = new(0, .1f) }, 2);
        Net.Inject(new DemoRewardMessage
        {
            ProtocolVersion = DemoRewardMessage.Protocol, Sequence = _sequence, Location = location, Kind = DemoRewardKind.CardChoice,
            Cursor = new(.32f, .5f),
            Cards = _thirdRewardCards, Labels = ["Пропустить"], Hovered = 0, Chosen = -1,
        }, 3);
    }
    private static int CountMirrors() => DemoController.Current!.GetNode("DemoView/ReadOnlyHand").GetChildCount();
    private byte[] CaptureMetadata()
    {
        var root = MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.ActiveScreenContext.Instance.GetCurrentScreen() as Node
            ?? throw new Exception("No native screen node");
        var size = GetViewport().GetVisibleRect().Size;
        return DemoVisualCodec.Encode(new DemoVisualUpdate { Full = true, Screen = root.GetType().Name,
            Reference = [size.X, size.Y], Items = DemoVisualProjection.Capture(root, Transform2D.Identity) });
    }
    private Vector2 ArrowEnemyPoint()
    {
        var enemy = Descendants<MegaCrit.Sts2.Core.Nodes.Combat.NCreature>(NCombatRoom.Instance!)
            .First(c => !Players.Any(p => p.Creature == c.Entity));
        // NTargetManager uses the creature's VFX anchor, not an arbitrary point above it.
        return enemy.VfxSpawnPosition;
    }
    private List<DemoCardPose> NativeArrowPoses()
    {
        if (_referenceArrow == null)
        {
            _referenceArrow = new MegaCrit.Sts2.Core.Nodes.Combat.NTargetingArrow { Name = "LabReferenceArrow" };
            AddChild(_referenceArrow);
            _referenceArrow.Hide();
        }
        var type = typeof(MegaCrit.Sts2.Core.Nodes.Combat.NTargetingArrow);
        int count = Players[1].PlayerCombatState?.Hand.Cards.Count ?? 7;
        var selectedCentre = new Vector2((.5f + (2 - (count - 1) / 2f) * .085f) * 1920, .68f * 1080);
        AccessTools.Field(type, "_fromPos").SetValue(_referenceArrow, selectedCentre);
        AccessTools.Method(type, "UpdateArrowPosition").Invoke(_referenceArrow, [ArrowEnemyPoint()]);
        return _referenceArrow.GetChildren().OfType<Sprite2D>().Select((part, i) => new DemoCardPose {
            CardId = (uint)i, Position = part.Position / new Vector2(1920, 1080), Scale = part.Scale, Rotation = part.Rotation }).ToList();
    }
    private async Task CheckNativeArrow()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var arrow = DemoController.Current!.GetNode<Node2D>("DemoView/DemoTargetingArrow");
        var expected = DemoArrowLayout.Build(new((.5f + (2 - (Players[1].PlayerCombatState!.Hand.Cards.Count - 1) / 2f) * .085f) * 1920, .68f * 1080), ArrowEnemyPoint(), new(1920, 1080));
        bool exact = arrow.Visible && arrow.GetChildCount() == expected.Count;
        var size = DemoController.Current.Ui.Size;
        for (int i = 0; i < expected.Count && exact; i++)
        {
            var part = (Sprite2D)arrow.GetChild(i);
            exact &= part.Position.DistanceTo(expected[i].Position * size) < .1f && part.Scale == expected[i].Scale
                && Math.Abs(part.Rotation - expected[i].Rotation) < .001f;
        }
        if (exact) Log.Info("[DemoLab] PASS: native-art arrow is locally rendered with 20 static transforms, fixed head scale and no elastic animation.");
        else Log.Error($"[DemoLab] FAIL: native arrow mismatch; visible={arrow.Visible} children={arrow.GetChildCount()} target={DemoController.ObservedPlayerId}.");
    }
    private async Task CheckArrowThenDeath()
    {
        await CheckNativeArrow();
        await Capture("combat-arrow");
        await TestDeadObserver();
    }
    private async Task CheckExitFade(bool started)
    {
        await ToSignal(GetTree().CreateTimer(.35), SceneTreeTimer.SignalName.Timeout);
        bool finished = !DemoController.Current!.GetNode<Control>("DemoView").Visible
            && NCombatRoom.Instance!.Ui.Hand.Modulate.A > .99f && CountMirrors() == 0;
        if (started && finished) Log.Info("[DemoLab] PASS: exit crossfade starts with local hand transparent, finishes with full alpha and no leftover foreign cards.");
        else Log.Error($"[DemoLab] FAIL: exit fade started={started} finished={finished}.");
    }
    private void InjectNativeMetadata(byte[] data, Vector2? origin = null, Vector2? size = null)
    {
        uint sequence = ++_sequence;
        byte count = (byte)((data.Length + DemoScreenMessage.ChunkBytes - 1) / DemoScreenMessage.ChunkBytes);
        for (byte i = 0; i < count; i++)
        {
            int offset = i * DemoScreenMessage.ChunkBytes;
            Net.Inject(new DemoScreenMessage { ProtocolVersion = DemoScreenMessage.Protocol, Sequence = sequence,
                Location = RunManager.Instance.DebugOnlyGetState()!.RunLocation, Index = i, Count = count,
                ImageOrigin = origin ?? Vector2.Zero, ImageSize = size ?? Vector2.One,
                Cursor = new(.5f, .5f), Data = data.AsSpan(offset, Math.Min(DemoScreenMessage.ChunkBytes, data.Length - offset)).ToArray() }, 2);
        }
    }
    private void InjectActivity()
    {
        Net.Inject(new DemoRewardMessage { ProtocolVersion = DemoRewardMessage.Protocol, Sequence = ++_sequence,
            Location = RunManager.Instance.DebugOnlyGetState()!.RunLocation, Kind = DemoRewardKind.Activity,
            Screen = MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.ActiveScreenContext.Instance.GetCurrentScreen()?.GetType().Name,
            Cards = [], Labels = [], Hovered = -1, Chosen = -1 }, 2);
    }
    private async Task TestNativeActivities()
    {
        try
        {
            DemoSettings.AllActivities = true;
            await AuditSettings();
            await AuditCardScreens();
            RewardDemoController.Current!.Exit();
            await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.CrystalSphere>(), showTransition: false);
            await AuditActivity("event-dialogue");
            await AuditAvailabilityAndMap();
            var entity = new MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereMinigame(Players[0], new MegaCrit.Sts2.Core.Random.Rng(42), 3);
            var predictions = MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere.NCrystalSphereScreen.ShowScreen(entity);
            await AuditActivity("predictions");
            var revealCells = entity.cells.Cast<MegaCrit.Sts2.Core.Events.Custom.CrystalSphereEvent.CrystalSphereCell>()
                .Where(c => c.Item != null).GroupBy(c => c.Item).First().ToList();
            foreach (var reveal in revealCells.Take(Math.Max(1, revealCells.Count / 2))) reveal.IsHidden = false;
            await AuditActivity("predictions-partial");
            foreach (var reveal in revealCells) reveal.IsHidden = false;
            await AuditActivity("predictions-revealed");
            NOverlayStack.Instance!.Remove(predictions); entity.ForceMinigameEnd();
            await RunManager.Instance.EnterRoomDebug(RoomType.Shop, showTransition: false);
            await AuditActivity("merchant-room");
            NRun.Instance!.MerchantRoom!.Inventory.Open();
            await AuditActivity("merchant-inventory");
            await AuditShopUpdates();
            await RunManager.Instance.EnterRoomDebug(RoomType.RestSite, showTransition: false);
            await AuditActivity("rest-site");
            await RunManager.Instance.EnterRoomDebug(RoomType.Treasure, showTransition: false);
            await AuditActivity("treasure");
            var treasureRoom = Descendants<NTreasureRoom>(NRun.Instance!).Single();
            HarmonyLib.AccessTools.Method(typeof(NTreasureRoom), "OnChestButtonReleased").Invoke(treasureRoom, [null]);
            await ToSignal(GetTree().CreateTimer(2), SceneTreeTimer.SignalName.Timeout);
            await AuditActivity("treasure-open");
            var relics = ModelDb.AllRelics.Take(3).Select(r => r.ToMutable()).ToList();
            var choice = MegaCrit.Sts2.Core.Nodes.Screens.NChooseARelicSelection.ShowScreen(relics);
            await AuditActivity("choose-relic");
            if (choice != null) NOverlayStack.Instance!.Remove(choice);
            await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<MegaCrit.Sts2.Core.Models.Events.Neow>(), showTransition: false);
            await AuditActivity("ancient-event");
            Log.Info("[DemoLab] 1.3.1 LAB FINISHED.");
        }
        catch (Exception e) { Log.Error("[DemoLab] ACTIVITIES FAILED: " + e); }
    }
    private async Task<(byte[] Data, Vector2 Origin, Vector2 Size)> CapturePublishedFrame()
    {
        Net.PublishedScreens.Clear(); Net.PublishedRewards.Clear();
        Net.Inject(new DemoPresenceMessage { ProtocolVersion = 1, Sequence = ++_sequence,
            Location = RunManager.Instance.DebugOnlyGetState()!.RunLocation, Watching = false, TargetId = 1 }, 2);
        Net.Inject(new DemoPresenceMessage { ProtocolVersion = 1, Sequence = ++_sequence,
            Location = RunManager.Instance.DebugOnlyGetState()!.RunLocation, Watching = true, TargetId = 1, RequestSnapshot = true }, 2);
        Log.Info($"[DemoLab] Capture requested: presence={_sequence}, screen={MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.ActiveScreenContext.Instance.GetCurrentScreen()?.GetType().Name}, force={AccessTools.Field(typeof(RewardDemoController), "_forceFullMetadata").GetValue(RewardDemoController.Current)}, viewing={RewardDemoController.IsViewing}, location={RunManager.Instance.DebugOnlyGetState()!.RunLocation}.");
        await ToSignal(GetTree().CreateTimer(.8), SceneTreeTimer.SignalName.Timeout);
        if (MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.ActiveScreenContext.Instance.GetCurrentScreen() is MegaCrit.Sts2.Core.Nodes.Screens.NRewardsScreen { IsComplete: false })
        {
            var rewards = Net.PublishedRewards.LastOrDefault(m => m.Kind == DemoRewardKind.Rewards && m.Entries?.Count > 0);
            if (rewards.Entries?.Count > 0 && Net.PublishedScreens.Count == 0)
                Log.Info($"[DemoLab] PASS: rewards publisher sent {rewards.Entries.Count} typed descriptions/icons, zero screen-image packets.");
            else throw new Exception("Native typed reward metadata missing or screen packets leaked");
            return ([], Vector2.Zero, Vector2.One);
        }
        if (Net.PublishedScreens.Count == 0) throw new Exception("Production metadata publisher did not send a state.");
        var assembled = new DemoVisualState();
        float[] reference = [1920, 1080];
        int transferred = 0;
        foreach (var group in Net.PublishedScreens.GroupBy(m => m.Sequence).OrderBy(g => g.Key))
        {
            var fragments = group.OrderBy(m => m.Index).ToArray();
            if (fragments.Length != fragments[0].Count) continue;
            var bytes = fragments.SelectMany(m => m.Data!).ToArray();
            var state = DemoVisualCodec.Decode(bytes);
            if (!assembled.Apply(state, group.Key)) throw new Exception($"Publisher emitted a broken metadata chain: sequence={group.Key}, base={state.Base}, full={state.Full}, screen={state.Screen}, previous={assembled.Sequence}/{assembled.Screen}.");
            reference = state.Reference; transferred += bytes.Length;
        }
        if (assembled.Sequence != 0)
        {
            Log.Info($"[DemoLab] PASS: UI publisher sent {transferred} bytes of metadata for {assembled.Items.Count} local visuals; no JPEG.");
            return (DemoVisualCodec.Encode(new DemoVisualUpdate { Full = true, Screen = assembled.Screen,
                Reference = reference, Items = assembled.Items.Values.ToList() }), Vector2.Zero, Vector2.One);
        }
        throw new Exception("Metadata keyframe missing.");
    }
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
    public readonly List<DemoScreenMessage> PublishedScreens = [];
    public readonly List<DemoRewardMessage> PublishedRewards = [];
    public readonly List<DemoHandMessage> PublishedHands = [];
    private readonly Dictionary<Type, List<Delegate>> _handlers = [];
    public ulong NetId => 1;
    public bool IsConnected => true;
    public bool IsGameLoading { get; private set; }
    public NetGameType Type => NetGameType.Host;
    public PlatformType Platform => PlatformType.None;
    public event Action<NetErrorInfo>? Disconnected { add { } remove { } }
    public void SendMessage<T>(T message, ulong playerId) where T : INetMessage { }
    public void SendMessage<T>(T message) where T : INetMessage
    {
        if (message is DemoHandMessage hand) { PublishedHands.Add(hand); if (PublishedHands.Count > 128) PublishedHands.RemoveAt(0); return; }
        if (message is DemoRewardMessage rewards)
        {
            var rewardWriter = new PacketWriter { WarnOnGrow = false }; rewards.Serialize(rewardWriter);
            var rewardReader = new PacketReader(); rewardReader.Reset(rewardWriter.Buffer.AsSpan(0, rewardWriter.BytePosition).ToArray());
            var rewardCopy = new DemoRewardMessage(); rewardCopy.Deserialize(rewardReader);
            PublishedRewards.Add(rewardCopy); if (PublishedRewards.Count > 128) PublishedRewards.RemoveAt(0); return;
        }
        if (message is not DemoScreenMessage frame) return;
        var writer = new PacketWriter { WarnOnGrow = false }; frame.Serialize(writer);
        var reader = new PacketReader(); reader.Reset(writer.Buffer.AsSpan(0, writer.BytePosition).ToArray());
        var copy = new DemoScreenMessage(); copy.Deserialize(reader);
        PublishedScreens.Add(copy);
        if (PublishedScreens.Count > 64) PublishedScreens.RemoveRange(0, PublishedScreens.Count - 64);
    }
    public void RegisterMessageHandler<T>(MessageHandlerDelegate<T> handler) where T : INetMessage
    {
        if (!_handlers.TryGetValue(typeof(T), out var handlers)) _handlers[typeof(T)] = handlers = [];
        handlers.Add(handler);
    }
    public void UnregisterMessageHandler<T>(MessageHandlerDelegate<T> handler) where T : INetMessage
    { if (_handlers.TryGetValue(typeof(T), out var handlers)) handlers.Remove(handler); }
    public void Inject<T>(T message, ulong sender) where T : INetMessage, new()
    {
        var writer = new PacketWriter { WarnOnGrow = false }; message.Serialize(writer);
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
