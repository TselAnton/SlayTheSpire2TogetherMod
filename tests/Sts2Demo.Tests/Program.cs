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
var arrow = Roundtrip(new DemoHandMessage { ProtocolVersion = DemoHandMessage.Protocol, Cards = [],
    Arrow = [new DemoCardPose { CardId = 19, Position = new(.5f, .5f), Scale = Vector2.One, Rotation = 2 }],
    ArrowTintRG = new(.8f, 1), ArrowTintBA = new(.5f, 1) });
Check(arrow.Arrow!.Count == 1 && arrow.Arrow[0].CardId == 19 && arrow.Arrow[0].Rotation == 2
    && arrow.ArrowTintRG.Y == 1 && arrow.ArrowTintBA.X == .5f, "Native arrow transform/tint roundtrip");
Check(DemoScreenPolicy.Allowed("NCrystalSphereScreen", false, false), "Crystal sphere predictions can mirror native viewport");
Check(DemoScreenPolicy.Allowed("NMerchantInventory", false, false) && DemoScreenPolicy.Allowed("NEventRoom", false, false), "Shop and event allowlist");
Check(!DemoScreenPolicy.Allowed("NRewardsScreen", true, false), "Rest site disabled even with reward overlay");
Check(!DemoScreenPolicy.Allowed("NSharedRelicPickingScreen", false, false), "Relic choices disabled");
Check(!DemoScreenPolicy.Allowed("NSettingsScreen", false, false) && !DemoScreenPolicy.Allowed("UnknownModScreen", false, false), "Private/unknown screens denied");
Check(!DemoScreenPolicy.Allowed("NMapScreen", false, true), "No off-combat stream during combat");
var fragment = Roundtrip(new DemoScreenMessage { ProtocolVersion = 1, Sequence = 9, Count = 2, Index = 0,
    Location = presence.Location, Cursor = new(.3f, .4f), MouseDown = true, Data = [1, 2, 3], ImageSize = Vector2.One });
Check(fragment.Data!.SequenceEqual(new byte[] { 1, 2, 3 }) && fragment.Cursor.Y == .4f && fragment.MouseDown, "UI fragment wire roundtrip");
var assembler = new DemoScreenAssembly();
Check(assembler.Accept(fragment, 100) == null, "Incomplete UI frame not displayed");
fragment.Index = 1; fragment.Data = [4, 5];
Check(assembler.Accept(fragment, 200)!.SequenceEqual(new byte[] { 1, 2, 3, 4, 5 }), "UI frame assembled in order");
Check(assembler.Accept(fragment, 210) == null, "Duplicate completed frame ignored");
fragment.Sequence = 8;
Check(assembler.Accept(fragment, 220) == null, "Old frame cannot replace new UI");
fragment.Sequence = 10; fragment.Index = 0;
Check(assembler.Accept(fragment, 300) == null, "New frame discards incomplete state");
fragment.Index = 1;
Check(assembler.Accept(fragment, 2000) == null, "Expired partial frame discarded");
Check(!DemoScreenAssembly.SafeJpeg([0xff, 0xd8, 0xff]), "Truncated JPEG safely rejected");
byte[] jpeg = [0xff, 0xd8, 0xff, 0xc0, 0, 11, 8, 2, 208, 5, 0, 1, 1, 0x11, 0, 0xff, 0xd9];
Check(DemoScreenAssembly.SafeJpeg(jpeg), "JPEG dimensions checked before native decode");
jpeg[9] = 127;
Check(!DemoScreenAssembly.SafeJpeg(jpeg), "Decompression bomb dimensions rejected");
Console.WriteLine($"PASS: {assertions} total observer, arrow, screen-policy and bounded-stream assertions.");
Check(DemoScreenMessage.ValidImageRect(new(.3f, .2f), new(.3f, .6f)), "Native reward crop coordinates allowed");
Check(!DemoScreenMessage.ValidImageRect(new(float.NaN, 0), Vector2.One), "NaN image rect rejected");
Check(!DemoScreenMessage.ValidImageRect(Vector2.Zero, new(0, 1)) && !DemoScreenMessage.ValidImageRect(new(.9f, 0), new(.2f, 1)), "Empty/offscreen image rect rejected");
var oversizedFrame = new PacketWriter { WarnOnGrow = false };
oversizedFrame.WriteByte(DemoScreenMessage.Protocol); oversizedFrame.WriteUInt(1); oversizedFrame.Write(default(RunLocation));
oversizedFrame.WriteByte(0); oversizedFrame.WriteByte((byte)(DemoScreenMessage.MaxChunks + 1));
oversizedFrame.WriteVector2(Vector2.Zero); oversizedFrame.WriteBool(false); oversizedFrame.WriteVector2(Vector2.Zero);
oversizedFrame.WriteVector2(Vector2.One); oversizedFrame.WriteInt(DemoScreenMessage.ChunkBytes + 1);
packetReader.Reset(oversizedFrame.Buffer); rejected = false;
try { var packet = new DemoScreenMessage(); packet.Deserialize(packetReader); } catch (InvalidDataException) { rejected = true; }
Check(rejected, "Oversized UI frame rejected before payload allocation");
Console.WriteLine($"PASS: {assertions} final assertions.");
Check(!DemoScreenPolicy.Allowed("NEventRoom", false, false, ancientRelicChoice: true), "Ancient relic offers disabled until author narrows relic-choice scope");
Console.WriteLine($"PASS: {assertions} release-candidate assertions.");
