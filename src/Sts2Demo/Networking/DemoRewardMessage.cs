using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;
using Godot;
using Sts2Demo.Core;
using System.Text.Json;

namespace Sts2Demo.Networking;

public enum DemoRewardKind : byte { None, Rewards, CardChoice, Activity, Map }

public sealed record DemoRewardEntry(string Type, string Text, List<DemoVisual> Icons);

public struct DemoRewardMessage : INetMessage
{
    public const int MaxOptions = 16;
    public const byte Protocol = 3;
    public byte ProtocolVersion;
    public uint Sequence;
    public string? Screen;
    public RunLocation Location;
    public DemoRewardKind Kind;
    public int Hovered;
    public int Chosen;
    public List<SerializableCard>? Cards;
    public List<string>? Labels;
    public Vector2 Cursor;
    public List<Vector2>? Centers;
    public bool Controller;
    public List<DemoRewardEntry>? Entries;
    public string? Header;
    public bool Delta; // Cursor/selection-only update; keep the prior option descriptions.
    public DemoTargetingState Targeting;
    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => Delta ? NetTransferMode.Unreliable : NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;
    public bool ShouldBuffer => false;
    public readonly string ContentKey()
    {
        // Include every serialized card field (enchantment amount, modifiers, etc.)
        // and every icon property, not just the model name and upgrade level.
        var writer = new PacketWriter { WarnOnGrow = false };
        foreach (var card in Cards ?? []) writer.Write(card);
        return $"{Screen}:{Kind}:{Header}:" + Convert.ToHexString(writer.Buffer.AsSpan(0, writer.BytePosition))
            + JsonSerializer.Serialize(Entries ?? [], DemoVisualCodec.Json)
            + JsonSerializer.Serialize(Labels ?? [], DemoVisualCodec.Json);
    }
    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(ProtocolVersion); writer.WriteUInt(Sequence); writer.Write(Location);
        writer.WriteByte((byte)Kind); writer.WriteInt(Hovered); writer.WriteInt(Chosen);
        int count = Math.Min(Cards?.Count ?? 0, MaxOptions);
        writer.WriteByte((byte)count);
        for (int i = 0; i < count; i++) writer.Write(Cards![i]);
        count = Math.Min(Labels?.Count ?? 0, MaxOptions);
        writer.WriteByte((byte)count);
        for (int i = 0; i < count; i++) writer.WriteString(Labels![i][..Math.Min(Labels[i].Length, 512)]);
        writer.WriteVector2(Cursor); writer.WriteBool(Controller);
        count = Math.Min(Centers?.Count ?? 0, MaxOptions); writer.WriteByte((byte)count);
        for (int i = 0; i < count; i++) writer.WriteVector2(Centers![i]);
        writer.WriteString((Header ?? "")[..Math.Min(Header?.Length ?? 0, 512)]);
        count = Math.Min(Entries?.Count ?? 0, MaxOptions); writer.WriteByte((byte)count);
        for (int i = 0; i < count; i++)
        {
            var entry = Entries![i];
            string json = JsonSerializer.Serialize(entry, DemoVisualCodec.Json);
            // The sender must obey the same bound as the receiver. A complex icon
            // never turns an otherwise valid reward list into a rejected packet.
            while (System.Text.Encoding.UTF8.GetByteCount(json) > 8192 && entry.Icons.Count > 0)
            {
                entry = entry with { Icons = entry.Icons.Take(entry.Icons.Count - 1).ToList() };
                json = JsonSerializer.Serialize(entry, DemoVisualCodec.Json);
            }
            if (System.Text.Encoding.UTF8.GetByteCount(json) > 8192) throw new InvalidDataException("Reward metadata too long.");
            writer.WriteString(json);
        }
        writer.WriteBool(Delta); writer.WriteString(Screen ?? ""); writer.Write(Targeting);
    }
    public void Deserialize(PacketReader reader)
    {
        ProtocolVersion = reader.ReadByte(); Sequence = reader.ReadUInt(); Location = reader.Read<RunLocation>();
        Kind = (DemoRewardKind)reader.ReadByte(); Hovered = reader.ReadInt(); Chosen = reader.ReadInt();
        int count = reader.ReadByte();
        if (count > MaxOptions) throw new InvalidDataException("Too many reward cards.");
        Cards = new(count);
        for (int i = 0; i < count; i++) Cards.Add(reader.Read<SerializableCard>());
        count = reader.ReadByte();
        if (count > MaxOptions) throw new InvalidDataException("Too many reward labels.");
        Labels = new(count);
        for (int i = 0; i < count; i++)
        {
            // Bound the byte count before allocating a string from an incoming packet.
            int bytes = reader.ReadInt();
            if (bytes is < 0 or > 2048) throw new InvalidDataException("Reward label too long.");
            var buffer = new byte[bytes];
            reader.ReadBytes(buffer, bytes);
            Labels.Add(System.Text.Encoding.UTF8.GetString(buffer));
        }
        Cursor = reader.ReadVector2(); Controller = reader.ReadBool();
        count = reader.ReadByte();
        if (count > MaxOptions) throw new InvalidDataException("Too many reward anchors.");
        Centers = new(count);
        for (int i = 0; i < count; i++)
        {
            var point = reader.ReadVector2();
            if (!float.IsFinite(point.X) || !float.IsFinite(point.Y) || point.X is < -1 or > 2 || point.Y is < -1 or > 2)
                throw new InvalidDataException("Invalid reward anchor.");
            Centers.Add(point);
        }
        Header = ReadBounded(reader, 2048);
        if (!float.IsFinite(Cursor.X) || !float.IsFinite(Cursor.Y) || Cursor.X is < -2 or > 2 || Cursor.Y is < -2 or > 2)
            throw new InvalidDataException("Invalid reward cursor.");
        count = reader.ReadByte();
        if (count > MaxOptions) throw new InvalidDataException("Too many reward entries.");
        Entries = new(count);
        for (int i = 0; i < count; i++)
        {
            var entry = JsonSerializer.Deserialize<DemoRewardEntry>(ReadBounded(reader, 8192), DemoVisualCodec.Json)
                ?? throw new InvalidDataException("Invalid reward entry.");
            if (entry.Type == null || entry.Text == null || entry.Icons == null || entry.Type.Length > 80 || entry.Text.Length > 512 || entry.Icons.Count > 8
                || !DemoVisualCodec.SafeText(entry.Text) || !DemoVisualCodec.Valid(new DemoVisualUpdate { Items = entry.Icons })) throw new InvalidDataException("Invalid reward entry.");
            Entries.Add(entry);
        }
        Delta = reader.ReadBool(); Screen = ReadBounded(reader, 160); Targeting = reader.Read<DemoTargetingState>();
    }
    private static string ReadBounded(PacketReader reader, int limit)
    {
        int length = reader.ReadInt();
        if (length < 0 || length > limit) throw new InvalidDataException("Reward metadata too long.");
        byte[] bytes = new byte[length]; reader.ReadBytes(bytes, length);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }
}
