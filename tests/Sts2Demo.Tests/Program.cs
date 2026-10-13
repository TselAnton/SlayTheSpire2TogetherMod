using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using Sts2Demo.Core;
using Sts2Demo.Networking;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Map;

int assertions = 0;
void Check(bool condition, string description)
{
    assertions++;
    if (!condition) throw new Exception(description);
}
var session = new DemoSession();
Check(!session.TryEnter(1, 1, true, true, false), "Cannot observe self");
Check(!session.TryEnter(1, 2, false, true, false), "Cannot observe disconnected peer");
Check(!session.TryEnter(1, 2, true, false, false), "Cannot enter outside combat");
Check(!session.TryEnter(1, 2, true, true, true), "Cannot interrupt card play or required choice");
Check(session.TryEnter(1, 2, true, true, false) && session.Target == 2, "Can observe connected teammate");
Check(session.TryEnter(1, 3, true, true, false) && session.Target == 3, "Can switch teammate");
session.Exit();
Check(!session.Active && session.Target == null, "Exit clears target");
Check(DemoSession.Newer(0, uint.MaxValue), "Sequence rollover");
Check(!DemoSession.Newer(7, 7) && !DemoSession.Newer(6, 7), "Reject old and duplicate state");

T Roundtrip<T>(T message) where T : IPacketSerializable, new()
{
    var writer = new PacketWriter { WarnOnGrow = false };
    message.Serialize(writer);
    var reader = new PacketReader();
    reader.Reset(writer.Buffer.AsSpan(0, writer.BytePosition).ToArray());
    var copy = new T();
    copy.Deserialize(reader);
    return copy;
}
var presence = Roundtrip(new DemoPresenceMessage
{
    ProtocolVersion = 1, Sequence = 123, Watching = true, TargetId = ulong.MaxValue,
    Location = new RunLocation(1, new MapCoord(2, 3), 0),
});
Check(presence.Watching && presence.TargetId == ulong.MaxValue && presence.Sequence == 123 && presence.ProtocolVersion == 1, "Presence wire roundtrip");
Check(presence.Location == new RunLocation(1, new MapCoord(2, 3), 0), "Combat location wire roundtrip");
var pose = new DemoCardPose { CardId = 65535, Position = new(.5f, .8f), Scale = new(.65f, .65f), Rotation = -.1f, ZIndex = 9, Selected = true };
var hand = Roundtrip(new DemoHandMessage { ProtocolVersion = 1, Sequence = 17, Round = 2, ReferenceSize = new(1920, 1080), Cards = [pose] });
Check(hand.Cards!.Count == 1 && hand.Cards[0].CardId == pose.CardId && hand.Cards[0].Position == pose.Position
    && hand.Cards[0].Scale == pose.Scale && hand.Cards[0].Rotation == pose.Rotation && hand.Cards[0].ZIndex == 9 && hand.Cards[0].Selected, "Card pose wire roundtrip");
Check(hand.Sequence == 17 && hand.Round == 2 && hand.ReferenceSize == new Vector2(1920, 1080), "Hand envelope roundtrip");
Check(Roundtrip(new DemoHandMessage { Cards = [], Suspended = true }).Suspended, "Empty suspended hand");
Check(pose.IsValid, "Finite pose allowed");
pose.Position = new(float.NaN, 0);
Check(!pose.IsValid, "Reject NaN position");
pose.Position = new(.5f, .8f); pose.Scale = new(-1, 1);
Check(!pose.IsValid, "Reject invalid scale");
pose.Scale = Vector2.One; pose.Rotation = float.PositiveInfinity;
Check(!pose.IsValid, "Reject infinite rotation");
var huge = Roundtrip(new DemoHandMessage { Cards = Enumerable.Range(0, 100).Select(_ => default(DemoCardPose)).ToList() });
Check(huge.Cards!.Count == 64, "Bound outgoing card count");
var invalid = new PacketWriter { WarnOnGrow = false };
invalid.WriteByte(1); invalid.WriteUInt(0); invalid.Write(default(RunLocation)); invalid.WriteInt(0); invalid.WriteBool(false);
invalid.WriteVector2(new(1920, 1080)); invalid.WriteByte(65);
var packetReader = new PacketReader(); packetReader.Reset(invalid.Buffer);
bool rejected = false;
try { var packet = new DemoHandMessage(); packet.Deserialize(packetReader); }
catch (InvalidDataException) { rejected = true; }
Check(rejected, "Reject oversized incoming packet before allocation");
var rewards = Roundtrip(new DemoRewardMessage
{
    ProtocolVersion = 1, Sequence = 52, Kind = DemoRewardKind.Rewards, Hovered = 2, Chosen = -1,
    Location = new RunLocation(1, new MapCoord(2, 3), 0), Cards = [], Labels = ["Gold", "Зелье", "Выбор карты"],
});
Check(rewards.Labels!.SequenceEqual(new[] { "Gold", "Зелье", "Выбор карты" }) && rewards.Hovered == 2 && rewards.Chosen == -1, "Unicode reward labels and hover roundtrip");
Check(rewards.Sequence == 52 && rewards.Kind == DemoRewardKind.Rewards && rewards.Location == presence.Location, "Reward envelope roundtrip");
var rewardBounds = Roundtrip(new DemoRewardMessage { Cards = [], Labels = Enumerable.Repeat(new string('я', 900), 32).ToList() });
Check(rewardBounds.Labels!.Count == 16 && rewardBounds.Labels.All(s => s.Length == 512), "Bound reward label count and length");
var badReward = new PacketWriter { WarnOnGrow = false };
badReward.WriteByte(1); badReward.WriteUInt(1); badReward.Write(default(RunLocation));
badReward.WriteByte((byte)DemoRewardKind.Rewards); badReward.WriteInt(-1); badReward.WriteInt(-1);
badReward.WriteByte(0); badReward.WriteByte(1); badReward.WriteInt(2049);
packetReader.Reset(badReward.Buffer); rejected = false;
try { var packet = new DemoRewardMessage(); packet.Deserialize(packetReader); }
catch (InvalidDataException) { rejected = true; }
Check(rejected, "Reject huge reward label before allocation");
Console.WriteLine($"PASS: {assertions} observer-state and network assertions.");
