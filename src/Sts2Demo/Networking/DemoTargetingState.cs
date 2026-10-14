using Godot;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;

namespace Sts2Demo.Networking;

public struct DemoTargetingState : IPacketSerializable
{
    public bool Visible;
    public Vector2 From, To, Offset, TintRG, TintBA;
    public byte TargetKind; // 0 = free pointer, 1 = player, 2 = combat creature
    public ulong TargetId;
    public void Serialize(PacketWriter writer)
    {
        writer.WriteBool(Visible);
        if (!Visible) return;
        writer.WriteVector2(From); writer.WriteVector2(To); writer.WriteVector2(Offset);
        writer.WriteVector2(TintRG); writer.WriteVector2(TintBA);
        writer.WriteByte(TargetKind); writer.WriteULong(TargetId);
    }
    public void Deserialize(PacketReader reader)
    {
        Visible = reader.ReadBool();
        if (!Visible) return;
        From = reader.ReadVector2(); To = reader.ReadVector2(); Offset = reader.ReadVector2();
        TintRG = reader.ReadVector2(); TintBA = reader.ReadVector2();
        TargetKind = reader.ReadByte(); TargetId = reader.ReadULong();
        if (!Valid) throw new InvalidDataException("Invalid targeting state.");
    }
    public readonly bool Valid => !Visible || TargetKind <= 2
        && new[] { From, To, Offset }.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && p.X is >= -2 and <= 2 && p.Y is >= -2 and <= 2)
        && new[] { TintRG, TintBA }.All(p => float.IsFinite(p.X) && float.IsFinite(p.Y) && p.X is >= 0 and <= 1 && p.Y is >= 0 and <= 1);
}
