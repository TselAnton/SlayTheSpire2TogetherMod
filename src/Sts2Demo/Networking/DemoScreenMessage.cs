using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Runs;

namespace Sts2Demo.Networking;

/// <summary>Bounded compressed asset/text/layout metadata. No image bytes or GPU readback.</summary>
public struct DemoScreenMessage : INetMessage
{
    public const byte Protocol = 4;
    public const int MaxFrameBytes = 32 * 1024;
    public const int ChunkBytes = 4 * 1024;
    public const int MaxChunks = MaxFrameBytes / ChunkBytes;
    public byte ProtocolVersion;
    public uint Sequence;
    public RunLocation Location;
    public byte Index;
    public byte Count;
    public Vector2 Cursor;
    public bool MouseDown;
    public Vector2 ImageOrigin;
    public Vector2 ImageSize;
    public byte[]? Data;
    public bool ShouldBroadcast => true;
    public NetTransferMode Mode => NetTransferMode.Reliable;
    public LogLevel LogLevel => LogLevel.VeryDebug;
    public bool ShouldBuffer => false;
    public void Serialize(PacketWriter writer)
    {
        if (Data == null || Data.Length > ChunkBytes || Count is 0 or > MaxChunks || Index >= Count)
            throw new InvalidDataException("Invalid UI frame fragment.");
        writer.WriteByte(ProtocolVersion); writer.WriteUInt(Sequence); writer.Write(Location);
        writer.WriteByte(Index); writer.WriteByte(Count); writer.WriteVector2(Cursor); writer.WriteBool(MouseDown);
        writer.WriteVector2(ImageOrigin); writer.WriteVector2(ImageSize);
        writer.WriteInt(Data.Length); writer.WriteBytes(Data, Data.Length);
    }
    public void Deserialize(PacketReader reader)
    {
        ProtocolVersion = reader.ReadByte(); Sequence = reader.ReadUInt(); Location = reader.Read<RunLocation>();
        Index = reader.ReadByte(); Count = reader.ReadByte(); Cursor = reader.ReadVector2(); MouseDown = reader.ReadBool();
        ImageOrigin = reader.ReadVector2(); ImageSize = reader.ReadVector2();
        int length = reader.ReadInt();
        if (length is <= 0 or > ChunkBytes || Count is 0 or > MaxChunks || Index >= Count
            || !float.IsFinite(Cursor.X) || !float.IsFinite(Cursor.Y)
            || Cursor.X is < -1 or > 2 || Cursor.Y is < -1 or > 2
            || !ValidImageRect(ImageOrigin, ImageSize))
            throw new InvalidDataException("Invalid UI frame fragment.");
        Data = new byte[length]; reader.ReadBytes(Data, length);
    }
    public static bool ValidImageRect(Vector2 origin, Vector2 size) => float.IsFinite(origin.X) && float.IsFinite(origin.Y)
        && float.IsFinite(size.X) && float.IsFinite(size.Y) && origin.X is >= 0 and <= 1 && origin.Y is >= 0 and <= 1
        && size.X is > 0 and <= 1 && size.Y is > 0 and <= 1 && origin.X + size.X <= 1.001f && origin.Y + size.Y <= 1.001f;
}
