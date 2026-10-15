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
    ArrowVisible = true, ArrowFrom = new(.3f, .8f), ArrowTo = new(.7f, .3f),
    ArrowTintRG = new(.8f, 1), ArrowTintBA = new(.5f, 1) });
Check(arrow.ArrowVisible && arrow.ArrowFrom == new Vector2(.3f, .8f) && arrow.ArrowTo == new Vector2(.7f, .3f)
    && arrow.ArrowTintRG.Y == 1 && arrow.ArrowTintBA.X == .5f, "Native arrow transform/tint roundtrip");
Check(!DemoScreenPolicy.Allowed("NCrystalSphereScreen", false, false), "Predictions disabled");
Check(!DemoScreenPolicy.Allowed("NMerchantInventory", false, false) && !DemoScreenPolicy.Allowed("NEventRoom", false, false), "Shops and events disabled");
foreach (var screen in new[] { "NDeckCardSelectScreen", "NChooseACardSelectionScreen", "NChooseABundleSelectionScreen", "NDeckUpgradeSelectScreen", "NDeckTransformSelectScreen", "NDeckEnchantSelectScreen", "NSimpleCardSelectScreen", "NInspectCardScreen", "NDeckViewScreen" })
    Check(!DemoScreenPolicy.Allowed(screen, false, false), "No remote activity: " + screen);
Check(!DemoScreenPolicy.Allowed("NMerchantRoom", false, false) && !DemoScreenPolicy.Allowed("NFakeMerchant", false, false)
    && !DemoScreenPolicy.Allowed("NFakeMerchantInventory", false, false), "All merchant variants blocked");
Check(!DemoScreenPolicy.Allowed("NRewardsScreen", true, false), "Rest site disabled even with reward overlay");
Check(!DemoScreenPolicy.Allowed("NSharedRelicPickingScreen", false, false), "Relic choices disabled");
Check(!DemoScreenPolicy.Allowed("NSettingsScreen", false, false) && !DemoScreenPolicy.Allowed("UnknownModScreen", false, false), "Private/unknown screens denied");
Check(!DemoScreenPolicy.Allowed("NMapScreen", false, true), "No off-combat stream during combat");
Check(!DemoScreenPolicy.Allowed("NMapRoom", false, false), "Standalone map room is not an observed activity");
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
Check(assembler.Accept(fragment, 6000) == null, "Expired partial frame discarded");
Check(!DemoScreenAssembly.SafeMetadata([0xff, 0xd8, 0xff]), "Image payload is not UI metadata");
var metadata = new DemoVisualUpdate { Full = true, Screen = "NCrystalSphereScreen", Items = [
    new DemoVisual { Id = "Board/Counter", Kind = 1, Text = "Предсказания: 3", Size = [100, 30] }] };
var packedMetadata = DemoVisualCodec.Encode(metadata);
Check(DemoScreenAssembly.SafeMetadata(packedMetadata), "Bounded metadata validated before local rendering");
var decoded = DemoVisualCodec.Decode(packedMetadata);
Check(decoded.Items[0].Text == metadata.Items[0].Text && decoded.Full, "Compressed Unicode metadata roundtrip");
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
Check(DemoInputPolicy.Resolve("ui_select", false, false, true, false) == DemoInputAction.ToggleFriend, "Native focused select (A) enters demo");
Check(DemoInputPolicy.Resolve("ui_accept", false, false, true, false) == DemoInputAction.None, "Native confirm (Y) is not an entry shortcut");
Check(DemoInputPolicy.Resolve("ui_select", false, false, false, false) == DemoInputAction.None, "No focused friend leaves native selection unchanged");
Check(DemoInputPolicy.Resolve("ui_cancel", true, false, false, false) == DemoInputAction.Exit, "Combat cancel exits globally");
Check(DemoInputPolicy.Resolve("ui_cancel", false, true, false, true) == DemoInputAction.Exit, "Reward cancel exits instead of claiming or skipping");
Check(DemoInputPolicy.Resolve("ui_accept", true, false, true, true) == DemoInputAction.None, "Combat Y stays LOCAL End Turn, never preview upgrade");
Check(DemoInputPolicy.Resolve("ui_accept", false, true, false, true) == DemoInputAction.Upgrades, "Reward Y upgrades copies regardless of focus");
Check(DemoInputPolicy.Resolve("ui_accept", false, true, true, false) == DemoInputAction.Block, "Reward summary confirm cannot claim underlying rewards");
Check(DemoInputPolicy.Resolve("ui_select", true, false, true, false) == DemoInputAction.ToggleFriend, "Combat select can switch or leave the green friend");
Check(DemoInputPolicy.Resolve("ui_select", true, false, false, false) == DemoInputAction.Block, "Observed hand cannot be selected");
Check(DemoInputPolicy.Resolve("mega_view_map", false, true, false, false) == DemoInputAction.Block, "Observer cannot simultaneously activate local map shortcut");
Check(DemoInputPolicy.Resolve("ui_right", true, false, false, false) == DemoInputAction.Navigate, "Combat observer navigates owned controls");
Check(DemoInputPolicy.Resolve("ui_up", false, true, false, true) == DemoInputAction.Navigate, "Reward observer navigates owned controls");
Check(DemoInputPolicy.Resolve("mega_view_map", false, false, false, false) == DemoInputAction.None, "Normal gameplay shortcuts remain untouched");
Console.WriteLine($"PASS: {assertions} final controller-policy assertions.");
for (int i = 0; i < 3; i++)
{
    var center = DemoRewardLayout.Center(1920, 1080, 3, i) / new Vector2(1920, 1080);
    Check(DemoRewardLayout.Hover(center, 1920, 1080, 3) == i, $"Cursor chooses reward card {i}, not last hitbox");
    Check(DemoRewardLayout.Hover(center, 1280, 720, 3) == i, $"Scaled cursor chooses reward card {i}");
}
Check(DemoRewardLayout.Hover(new(.01f, .05f), 1920, 1080, 3) == -1, "No reward hover outside cards");
var visualState = new DemoVisualState();
Check(visualState.Apply(metadata, 1) && visualState.Items.Count == 1, "Metadata full state initialized");
var delta = new DemoVisualUpdate { Screen = metadata.Screen, Base = 1, Items = [
    metadata.Items[0] with { Text = "2" }] };
Check(visualState.Apply(delta, 2) && visualState.Items["Board/Counter"].Text == "2", "Only changed UI label applied");
Check(!visualState.Apply(delta with { Base = 1 }, 3), "Missing delta baseline rejected");
Check(!visualState.Apply(metadata, 1), "Old UI full state rejected");
Check(visualState.Apply(metadata with { Screen = "NMerchantInventory" }, 4), "New screen replaces old UI state");
Check(!DemoVisualCodec.SafeAsset("user://settings.save") && !DemoVisualCodec.SafeAsset("res://images/../../settings.save"),
    "Remote metadata cannot load save files or traverse paths");
Check(!DemoVisualCodec.SafeAsset("https://host/asset.png") && !DemoVisualCodec.SafeAsset("res://scripts/danger.cs"),
    "No remote network assets or scripts");
Check(!DemoVisualCodec.Valid(metadata with { Items = [metadata.Items[0] with { Transform = [1, 0, 0, 1, float.NaN, 0] }] }),
    "Invalid visual geometry rejected");
Check(!DemoVisualCodec.Valid(metadata with { Items = [metadata.Items[0] with { Text = new string('a', 4097) }] }),
    "UI text length bounded");
Check(!DemoVisualCodec.Valid(metadata with { Items = null! }), "Null collections rejected safely");
var many = metadata with { Items = Enumerable.Range(0, 121).Select(i => metadata.Items[0] with { Id = $"Cell{i}", Text = i.ToString() }).ToList() };
Check(DemoVisualCodec.Encode(many).Length < 8000, "121 prediction cells use compact metadata, not JPEG-sized frames");
Check(DemoVisualCodec.Encode(new DemoVisualUpdate { Screen = metadata.Screen, Base = 1 }).Length < 256, "Steady UI sends tiny heartbeat");
var staticArrow = DemoArrowLayout.Build(new(960, 900), new(1500, 500), new(1920, 1080));
Check(staticArrow.Count == 20 && staticArrow[19].Scale == Vector2.One * .95f, "Local native-art arrow has static head scale");
Check(staticArrow.All(p => p.IsValid), "Static local arrow poses finite");
var arrowWriter = new PacketWriter { WarnOnGrow = false };
new DemoHandMessage { Cards = [], ReferenceSize = new(1920, 1080), ArrowVisible = true, ArrowFrom = new(.5f, .8f), ArrowTo = new(.8f, .4f) }.Serialize(arrowWriter);
Check(arrowWriter.BytePosition < 100, "Arrow sends endpoints only, not 20 sprite transforms");
var typedReward = Roundtrip(new DemoRewardMessage { Cards = [], Labels = [], Header = "Награды",
    Entries = [new DemoRewardEntry("GoldReward", "100 gold", [])], Cursor = new(.5f, .5f), Controller = false });
Check(typedReward.Entries![0].Type == "GoldReward" && typedReward.Entries[0].Text == "100 gold"
    && typedReward.Cursor.X == .5f, "Reward descriptions/types/cursor roundtrip without images or Reward objects");
Console.WriteLine($"PASS: {assertions} final 1.2.2 assertions.");
var anchors = new[] { new Vector2(.2f, .4f), new Vector2(.5f, .4f), new Vector2(.8f, .4f) };
for (int i = 0; i < anchors.Length; i++)
    Check(DemoRewardLayout.Hover(anchors[i], new(1920, 1080), anchors) == i, $"Source model-mapped anchor selects card {i}");
Check(DemoRewardLayout.Hover(new(float.NaN, .4f), new(1920, 1080), anchors) == -1, "Non-finite reward cursor cannot select a card");
Check(!DemoVisualCodec.Valid(metadata with { Items = [metadata.Items[0] with { Clip = [0, 0, 1] }] }), "Invalid prediction tile clip rejected");
Check(!DemoVisualCodec.Valid(metadata with { Items = [metadata.Items[0] with { Shader = "user://shader.gdshader" }] }), "Only installed-game shader paths accepted");
Check(!DemoVisualCodec.Valid(metadata with { Items = [metadata.Items[0] with { Uniforms = new() { ["h"] = [float.NaN] } }] }), "Non-finite shader metadata rejected");
Check(!DemoVisualCodec.SafeText("[img]user://settings.save[/img]"), "BBCode images cannot load private files");
Check(DemoVisualCodec.SafeText("[gold]20 gold[/gold] [img]res://images/icon.png[/img]"), "Native formatting and installed icons allowed");
var textStyle = metadata with { Items = [metadata.Items[0] with { VerticalAlign = 1, Wrap = 3, OutlineSize = 4, OutlineColor = [.1f, .2f, .3f, 1] }] };
var restoredTextStyle = DemoVisualCodec.Decode(DemoVisualCodec.Encode(textStyle)).Items[0];
Check(restoredTextStyle.VerticalAlign == 1 && restoredTextStyle.Wrap == 3 && restoredTextStyle.OutlineSize == 4
    && restoredTextStyle.OutlineColor.SequenceEqual(textStyle.Items[0].OutlineColor), "Native text alignment, wrapping and outline round trip");
Check(!DemoVisualCodec.Valid(metadata with { Items = [metadata.Items[0] with { OutlineSize = 999 }] }), "Oversized text outline rejected");
Check(!DemoVisualCodec.Valid(metadata with { Items = [metadata.Items[0] with { OutlineColor = [0, 0, float.NaN, 1] }] }), "Non-finite outline color rejected");
Check(!DemoVisualCodec.Valid(metadata with { Items = [metadata.Items[0] with { VerticalAlign = 9 }] }), "Invalid text alignment rejected");
var cursorOnly = Roundtrip(new DemoRewardMessage { ProtocolVersion = DemoRewardMessage.Protocol, Delta = true,
    Cursor = new(.2f, .4f), Cards = [], Labels = [], Entries = [] });
Check(cursorOnly.Delta && cursorOnly.Cards!.Count == 0, "Cursor-only reward update contains no option retransmission");
Console.WriteLine($"PASS: {assertions} final validated 1.2.2 assertions.");

Check(!DemoScreenPolicy.KnownActivity("NTreasureRoom"), "Treasure is prohibited");
foreach (var screen in new[] { "NEventRoom", "NMerchantRoom", "NMerchantInventory", "NFakeMerchantInventory", "NCrystalSphereScreen", "NRestSiteRoom", "NChooseARelicSelection", "NDeckUpgradeSelectScreen", "NMapScreen", "NMapRoom" })
    Check(DemoScreenPolicy.Allowed(screen, screen == "NRestSiteRoom", false, allActivities: true), "All-activities allows " + screen);
Check(!DemoScreenPolicy.Allowed("FutureRoom", false, false, allActivities: true), "Unknown future room fails closed");
Check(!DemoScreenPolicy.Allowed("NSettingsScreen", false, false, allActivities: true), "Personal settings remain local");
Check(!DemoScreenPolicy.Allowed("NCombatRoom", false, true, allActivities: true), "Combat room uses its dedicated renderer");
Check(DemoScreenPolicy.Allowed("NCombatPileCardSelectScreen", false, true, allActivities: true), "In-combat selection uses native activity projection");
var typedScreen = Roundtrip(new DemoRewardMessage { ProtocolVersion = DemoRewardMessage.Protocol, Screen = "NMerchantInventory",
    Kind = DemoRewardKind.Activity, Cards = [], Labels = [], Entries = [] });
Check(typedScreen.Screen == "NMerchantInventory", "Reliable state identifies its native screen");
Check(typedScreen.Mode == MegaCrit.Sts2.Core.Multiplayer.Transport.NetTransferMode.Reliable, "Complete reward/window states are reliable");
typedScreen.Delta = true;
Check(typedScreen.Mode == MegaCrit.Sts2.Core.Multiplayer.Transport.NetTransferMode.Unreliable, "Cursor deltas never queue behind state");
var resync = Roundtrip(new DemoPresenceMessage { RequestSnapshot = true });
Check(resync.RequestSnapshot, "Observer can request snapshot after switch or desynchronization");
Console.WriteLine($"PASS: {assertions} total 1.3.1 assertions.");
foreach (byte targetKind in new byte[] { 0, 1, 2 })
{
    var aim = new DemoTargetingState { Visible = true, From = new(.3f, .1f), To = new(.8f, .4f), Offset = new(.01f, -.02f),
        TargetKind = targetKind, TargetId = 123, TintRG = new(1, .4f), TintBA = new(.3f, 1) };
    var restoredAim = Roundtrip(aim);
    Check(restoredAim.Valid && restoredAim.TargetKind == targetKind && restoredAim.TargetId == 123
        && restoredAim.Offset == aim.Offset && restoredAim.To == aim.To, "Potion target identity and coordinates roundtrip " + targetKind);
    var withArrow = Roundtrip(new DemoRewardMessage { Cards = [], Labels = [], Targeting = aim });
    Check(withArrow.Targeting.To == aim.To, "Outside-combat potion target survives window protocol " + targetKind);
}
Check(!new DemoTargetingState { Visible = true, To = new(float.NaN, 0) }.Valid, "Non-finite potion target rejected");
Check(!new DemoTargetingState { Visible = true, TargetKind = 99 }.Valid, "Unknown potion target kind rejected");
Check(!new DemoTargetingState { Visible = true, Offset = new(99, 0) }.Valid, "Potion target offset bounded");
Check(DemoVisualCodec.SafeAsset("res://animations/characters/silent/silent_skel_data.tres"), "Native character animation assets accepted");
Check(!DemoVisualCodec.Valid(metadata with { Items = [metadata.Items[0] with { SpineMaterials = new() { ["script"] = "res://scenes/code.cs" } }] }), "Spine property names restricted to rendering materials");
Check(!DemoVisualCodec.Valid(metadata with { Items = [metadata.Items[0] with { SpineMaterials = new() { ["normal_material"] = "user://private.tres" } }] }), "Spine material cannot read private paths");
var iconA = new DemoRewardMessage { Cards = [], Labels = [], Entries = [new("Gold", "10", [new() { Id = "icon" }])] };
var iconB = iconA;
iconB.Entries = [new("Gold", "10", [new() { Id = "icon", Tint = [.2f, .2f, .2f, 1] }])];
Check(iconA.ContentKey() != iconB.ContentKey(), "Changed reward icon state invalidates the render cache");
Console.WriteLine($"PASS: {assertions} complete 1.3.1 protocol assertions.");
var effects = metadata with { Items = [metadata.Items[0] with { Kind = 7,
    ParticleMaterial = "res://materials/fog.tres", Particles = [24, 8, 0, 1, 1, 1, 0, 1, 8],
    AncientBanner = [1, 2, 3], SpinePlayback = [0, 0, 1.5f], LineSeparation = -8, ParagraphSeparation = 12 }] };
var decodedEffects = DemoVisualCodec.Decode(DemoVisualCodec.Encode(effects)).Items[0];
Check(decodedEffects.Particles.SequenceEqual(effects.Items[0].Particles)
    && decodedEffects.AncientBanner.SequenceEqual(effects.Items[0].AncientBanner)
    && decodedEffects.SpinePlayback.SequenceEqual(effects.Items[0].SpinePlayback)
    && decodedEffects.LineSeparation == -8 && decodedEffects.ParagraphSeparation == 12,
    "Native particles, banner and text spacing survive metadata serialization");
Check(!DemoVisualCodec.Valid(effects with { Items = [effects.Items[0] with { Particles = [1] }] }), "Incomplete particle state rejected");
Check(!DemoVisualCodec.Valid(effects with { Items = [effects.Items[0] with { Particles = [9000, 8, 0, 1, 1, 1, 0, 1, 8] }] }), "Particle budget bounded");
Check(!DemoVisualCodec.Valid(effects with { Items = [effects.Items[0] with { AncientBanner = [float.NaN, 0, 0] }] }), "Nonfinite native text effect rejected");
Console.WriteLine($"PASS: {assertions} complete 1.3.1 assertions including native effects.");
var shadowed = metadata with { Items = [metadata.Items[0] with { Shadow = [2, -3, 4], ShadowColor = [0, 0, 0, .75f] }] };
var decodedShadow = DemoVisualCodec.Decode(DemoVisualCodec.Encode(shadowed)).Items[0];
Check(decodedShadow.Shadow.SequenceEqual(shadowed.Items[0].Shadow) && decodedShadow.ShadowColor[3] == .75f, "Native text shadow roundtrip");
Check(!DemoVisualCodec.Valid(shadowed with { Items = [shadowed.Items[0] with { Shadow = [2, 9999, 0] }] }), "Oversized text shadow rejected");
Check(!DemoVisualCodec.Valid(shadowed with { Items = [shadowed.Items[0] with { ShadowColor = [0, 0, 0, float.NaN] }] }), "Nonfinite shadow rejected");
Console.WriteLine($"PASS: {assertions} final 1.3.1 assertions.");
var masked = metadata with { Items = [metadata.Items[0] with { AlphaMasks = [new() { Asset = "res://images/ui/noise_mask_for_lines.tres" }] }] };
Check(DemoVisualCodec.Decode(DemoVisualCodec.Encode(masked)).Items[0].AlphaMasks[0].Asset == masked.Items[0].AlphaMasks[0].Asset, "Native alpha mask roundtrip");
Check(!DemoVisualCodec.Valid(masked with { Items = [masked.Items[0] with { AlphaMasks = [new() { Asset = "user://secret.tres" }] }] }), "Private alpha mask path rejected");
Check(!DemoVisualCodec.Valid(masked with { Items = [masked.Items[0] with { AlphaMasks = Enumerable.Repeat(masked.Items[0].AlphaMasks[0], 5).ToList() }] }), "Alpha mask nesting bounded");
Console.WriteLine($"PASS: {assertions} final 1.3.1 assertions with alpha masks.");
var hoverMetadata = metadata with { Items = [metadata.Items[0] with { HoverGroup = "cards/one", Hover = [100, 200, 50, 100, 100, 200, 1.25f, 1.25f, 1] }] };
Check(DemoVisualCodec.Decode(DemoVisualCodec.Encode(hoverMetadata)).Items[0].Hover.SequenceEqual(hoverMetadata.Items[0].Hover), "Local hover geometry roundtrip");
Check(!DemoVisualCodec.Valid(hoverMetadata with { Items = [hoverMetadata.Items[0] with { Hover = [1] }] }), "Partial hover geometry rejected");
Check(!DemoVisualCodec.Valid(hoverMetadata with { Items = [hoverMetadata.Items[0] with { Hover = [0, 0, 0, 0, 10, 10, 1000, 1, 1] }] }), "Hover scale bounded");
Check(!DemoVisualCodec.Valid(hoverMetadata with { Items = [hoverMetadata.Items[0] with { Hover = [0, 0, 0, 0, 10, 10, 1, 1, 99] }] }), "Unknown hover effect rejected");
var waiting = Roundtrip(new DemoRewardMessage { ProtocolVersion = DemoRewardMessage.Protocol, Kind = DemoRewardKind.Waiting, Screen = "NRewardsScreen", Cards = [], Labels = [] });
Check(waiting.Kind == DemoRewardKind.Waiting && waiting.Screen == "NRewardsScreen", "Completed rewards descriptor roundtrip");
Console.WriteLine($"PASS: {assertions} final 1.3.1 assertions including local hover and reward completion.");
var additive = metadata with { Items = [metadata.Items[0] with { CanvasMaterial = [1, 0, 1, 8, 8, 1] }] };
Check(DemoVisualCodec.Decode(DemoVisualCodec.Encode(additive)).Items[0].CanvasMaterial.SequenceEqual(additive.Items[0].CanvasMaterial), "Native additive/flipbook material roundtrip");
Check(!DemoVisualCodec.Valid(additive with { Items = [additive.Items[0] with { CanvasMaterial = [99, 0, 1, 8, 8, 1] }] }), "Unknown canvas blend mode rejected");
Console.WriteLine($"PASS: {assertions} final 1.3.1 protocol assertions.");

var completedRoom = Roundtrip(new DemoRewardMessage { ProtocolVersion = DemoRewardMessage.Protocol, RoomComplete = true, Cards = [], Labels = [] });
Check(completedRoom.RoomComplete, "Room completion wire roundtrip");

Console.WriteLine($"PASS: {assertions} final 1.4.0 protocol assertions.");
