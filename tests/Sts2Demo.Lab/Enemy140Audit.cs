using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Runs;
using Sts2Demo;
using Sts2Demo.Core;
using Sts2Demo.Networking;

namespace Sts2DemoLab;

public partial class LabOracle
{
    private async Task Audit140Enemies()
    {
        AccessTools.Property(typeof(RunState), nameof(RunState.NextRoomId)).SetValue(RunManager.Instance.DebugOnlyGetState(), 0);
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, MapPointType.Unassigned, ModelDb.Encounter<InfestedPrismsElite>().ToMutable());
        await ToSignal(GetTree().CreateTimer(3), SceneTreeTimer.SignalName.Timeout);
        var context = new ThrowingPlayerChoiceContext();
        await PowerCmd.Apply<TaintedPower>(context, Players[1].Creature, 8, null, null);
        var prism = NCombatRoom.Instance!.CreatureNodes.First(n => n.Entity.Monster is InfestedPrism);
        var intent = Descendants<NIntent>(prism).First(n => AccessTools.Field(typeof(NIntent), "_intent").GetValue(n) is AttackIntent);
        var attack = (AttackIntent)AccessTools.Field(typeof(NIntent), "_intent").GetValue(intent)!;
        var targets = (IEnumerable<Creature>)AccessTools.Field(typeof(NIntent), "_targets").GetValue(intent)!;
        string Expected(ulong id)
        {
            var own = LocalContext.NetId;
            try { LocalContext.NetId = id; return attack.GetIntentLabel(targets, prism.Entity).GetFormattedText(); }
            finally { LocalContext.NetId = own; }
        }
        var expectedOwn = Expected(1); var expectedFriend = Expected(2);
        if (expectedOwn == expectedFriend) throw new Exception("Prism fixture has no per-player damage difference");
        await Capture("140-prism-source");
        DemoController.Current!.EnterDemo(2);
        await ToSignal(GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout);
        var label = (RichTextLabel)AccessTools.Field(typeof(NIntent), "_valueLabel").GetValue(intent)!;
        if (label.Text != expectedFriend) throw new Exception($"Prism perspective mismatch {label.Text}/{expectedFriend}");
        await Capture("140-prism");
        DemoController.Current.ExitDemo();
        if (label.Text != expectedOwn) throw new Exception("Prism local intent not restored");

        var state = Players[2].Creature.CombatState!;
        var card = state.CreateCard<Radiate>(Players[2]);
        await CardPileCmd.Add(card, Players[2].PlayerCombatState!.Hand);
        await PlayerCmd.GainStars(3, Players[2]);
        await PowerCmd.Apply<VulnerablePower>(context, prism.Entity, 2, null, null);
        var oracle = (CardModel)card.ClonePreservingMutability();
        oracle.UpdateDynamicVarPreview(CardPreviewMode.Normal, prism.Entity, oracle.DynamicVars);
        DemoController.Current.EnterDemo(3);
        Net.Inject(new DemoHandMessage { ProtocolVersion = DemoHandMessage.Protocol, Sequence = ++_sequence,
            Location = RunManager.Instance.DebugOnlyGetState()!.RunLocation, Round = CombatManager.Instance.DebugOnlyGetState()!.RoundNumber,
            ReferenceSize = new(1280, 720), Cards = [], Targeting = new DemoTargetingState { Visible = true,
                TargetKind = 2, TargetId = prism.Entity.CombatId!.Value, TintRG = Vector2.One, TintBA = Vector2.One } }, 3);
        await ToSignal(GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout);
        var mirror = Descendants<NCard>(DemoController.Current).First(n => n.Model?.Id == card.Id);
        if (mirror.Model!.DynamicVars.Damage.PreviewValue != oracle.DynamicVars.Damage.PreviewValue)
            throw new Exception("Target-specific Radiate preview differs from native oracle");
        await Capture("140-radiate-target-preview");
        DemoController.Current.ExitDemo();
        Log.Info("[DemoLab] PASS 1.4.0: Infested Prism per-player damage and restore; Radiate target-specific preview matches native calculation.");
        await FinishForRewards();
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, MapPointType.Unassigned, ModelDb.Encounter<ThievingHopperWeak>().ToMutable());
        await ToSignal(GetTree().CreateTimer(3), SceneTreeTimer.SignalName.Timeout);
        var hopper = NCombatRoom.Instance!.CreatureNodes.First(n => n.Entity.Monster is ThievingHopper);
        await (Task)AccessTools.Method(typeof(ThievingHopper), "ThieveryMove").Invoke(hopper.Entity.Monster, [Players.Select(p => p.Creature).ToList()])!;
        await Capture("140-hopper-source");
        DemoController.Current!.EnterDemo(2);
        await ToSignal(GetTree().CreateTimer(.3), SceneTreeTimer.SignalName.Timeout);
        var visible = hopper.GetSpecialNode<Marker2D>("%StolenCardPos")!.GetChildren().OfType<NCard>().Where(n => n.Visible).ToList();
        if (visible.Count != 1 || visible[0].Model!.Owner != Players[1]) throw new Exception("Hopper stolen card owner mismatch");
        await Capture("140-hopper");
        DemoController.Current.ExitDemo();
        Log.Info("[DemoLab] PASS 1.4.0: native Thieving Hopper steals distinct cards; DEMO displays watched owner's card.");
    }
}
