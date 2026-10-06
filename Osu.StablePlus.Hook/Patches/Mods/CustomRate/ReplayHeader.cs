using System;
using System.IO;
using System.Security.Cryptography;

namespace Osu.StablePlus.Hook.Patches.Mods.CustomRate;

/// <summary>Reads the native fields and frame range from an osu! replay container.</summary>
internal sealed class ReplayHeader
{
    internal int Mode, Version, Mods, Total, Combo, FramesOffset, FramesLength, End;
    internal string MapHash = "";
    internal ushort[] Counts = new ushort[6];

    internal static ReplayHeader Read(BinaryReader reader)
    {
        var stream = reader.BaseStream;
        var result = new ReplayHeader { Mode = reader.ReadByte(), Version = reader.ReadInt32() };
        if (result.Mode > 3 || result.Version < 20070000) throw new InvalidDataException("Unsupported replay version or mode.");
        result.MapHash = ReadString(reader);
        ReadString(reader); // player name
        ReadString(reader); // replay hash
        for (var i = 0; i < result.Counts.Length; i++) result.Counts[i] = reader.ReadUInt16();
        result.Total = reader.ReadInt32();
        result.Combo = reader.ReadUInt16();
        reader.ReadBoolean();
        result.Mods = reader.ReadInt32();
        ReadString(reader); // life graph
        reader.ReadInt64(); // timestamp
        result.FramesLength = reader.ReadInt32();
        result.FramesOffset = checked((int)stream.Position);
        if (result.FramesLength < -1) throw new InvalidDataException("Invalid replay frames length.");
        ReplayRateMetadata.Skip(reader, Math.Max(0, result.FramesLength));
        if (result.Version >= 20140721) reader.ReadInt64();
        else if (result.Version >= 20121008) reader.ReadInt32();
        if (result.Version < 30000000 && (result.Mods & (1 << 23)) != 0) reader.ReadDouble();
        result.End = checked((int)stream.Position);
        return result;
    }

    private static string ReadString(BinaryReader reader)
    {
        var marker = reader.ReadByte();
        if (marker == 0) return "";
        if (marker != 11) throw new InvalidDataException("Invalid osu! string.");
        var value = reader.ReadString();
        if (value.Length > 1024 * 1024) throw new InvalidDataException("Oversize osu! string.");
        return value;
    }

    internal static string Hash(byte[] bytes)
    {
        using var hash = SHA256.Create();
        return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
}
