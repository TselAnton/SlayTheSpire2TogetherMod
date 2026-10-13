using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace Sts2Demo.Networking;

public enum DemoRewardKind : byte { None, Rewards, CardChoice }

public struct DemoRewardMessage : INetMessage
{
    public const int MaxOptions = 16;
    public byte ProtocolVersion;
    public uint Sequence;
    public RunLocation Location;
    public DemoRewardKind Kind;
    public int Hovered;
    public int Chosen;
    public List<SerializableCard>? Cards;
    public List<string>? Labels;
    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;
    public bool ShouldBuffer => false;
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
    }
}
