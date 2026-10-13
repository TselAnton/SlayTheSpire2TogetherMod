using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2Demo.Networking;

// Self-contained cosmetic state, never part of a saved run or an INetAction.
public struct DemoPresenceMessage : INetMessage
{
    public const byte Protocol = 1;
    public byte ProtocolVersion;
    public uint Sequence;
    public RunLocation Location;
    public bool Watching;
    public ulong TargetId;
    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;
    public bool ShouldBuffer => false;
    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(ProtocolVersion);
        writer.WriteUInt(Sequence);
        writer.Write(Location);
        writer.WriteBool(Watching);
        writer.WriteULong(TargetId);
    }
    public void Deserialize(PacketReader reader)
    {
        ProtocolVersion = reader.ReadByte();
        Sequence = reader.ReadUInt();
        Location = reader.Read<RunLocation>();
        Watching = reader.ReadBool();
        TargetId = reader.ReadULong();
    }
}

public struct DemoCardPose : IPacketSerializable
{
    public uint CardId;
    public Vector2 Position;
    public Vector2 Scale;
    public float Rotation;
    public int ZIndex;
    public bool Selected;
    public void Serialize(PacketWriter writer)
    {
        writer.WriteUInt(CardId, 16);
        writer.WriteVector2(Position);
        writer.WriteVector2(Scale);
        writer.WriteFloat(Rotation);
        writer.WriteInt(ZIndex, 16);
        writer.WriteBool(Selected);
    }
    public void Deserialize(PacketReader reader)
    {
        CardId = reader.ReadUInt(16);
        Position = reader.ReadVector2();
        Scale = reader.ReadVector2();
        Rotation = reader.ReadFloat();
        ZIndex = reader.ReadInt(16);
        Selected = reader.ReadBool();
    }
    public readonly bool IsValid => float.IsFinite(Position.X) && float.IsFinite(Position.Y)
        && Position.X is >= -1 and <= 2 && Position.Y is >= -1 and <= 2
        && float.IsFinite(Scale.X) && float.IsFinite(Scale.Y)
        && Scale.X is > 0 and <= 4 && Scale.Y is > 0 and <= 4
        && float.IsFinite(Rotation) && Math.Abs(Rotation) < 10;
}

public struct DemoHandMessage : INetMessage
{
    public const int MaxCards = 64;
    public byte ProtocolVersion;
    public uint Sequence;
    public RunLocation Location;
    public int Round;
    public bool Suspended;
    public Vector2 ReferenceSize;
    public List<DemoCardPose>? Cards;
    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Unreliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;
    public bool ShouldBuffer => false;
    public void Serialize(PacketWriter writer)
    {
        writer.WriteByte(ProtocolVersion);
        writer.WriteUInt(Sequence);
        writer.Write(Location);
        writer.WriteInt(Round);
        writer.WriteBool(Suspended);
        writer.WriteVector2(ReferenceSize);
        int count = Math.Min(Cards?.Count ?? 0, MaxCards);
        writer.WriteByte((byte)count);
        for (int i = 0; i < count; i++) writer.Write(Cards![i]);
    }
    public void Deserialize(PacketReader reader)
    {
        ProtocolVersion = reader.ReadByte();
        Sequence = reader.ReadUInt();
        Location = reader.Read<RunLocation>();
        Round = reader.ReadInt();
        Suspended = reader.ReadBool();
        ReferenceSize = reader.ReadVector2();
        int count = reader.ReadByte();
        if (count > MaxCards) throw new InvalidDataException("Demo hand exceeds limit.");
        Cards = new(count);
        for (int i = 0; i < count; i++) Cards.Add(reader.Read<DemoCardPose>());
    }
}
