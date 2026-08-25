using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Pasyot_Launcher.Services
{
    // Minimal generic NBT (big-endian, uncompressed) reader/writer - just enough to read an
    // existing servers.dat losslessly (whatever tags it holds), and add/update one entry in its
    // "servers" list without touching anything else the user or another launcher put there.
    internal static class NbtServerList
    {
        private enum TagType : byte
        {
            End = 0, Byte = 1, Short = 2, Int = 3, Long = 4, Float = 5, Double = 6,
            ByteArray = 7, String = 8, List = 9, Compound = 10, IntArray = 11, LongArray = 12
        }

        private class NbtTag
        {
            public TagType Type;
            public string Name = "";
            public object? Value;
            public List<NbtTag>? Compound;
            public TagType ListElementType;
            public List<NbtTag>? ListItems;
        }

        public static void AddOrUpdateServer(string serversDatPath, string name, string ip)
        {
            NbtTag root;

            try
            {
                root = File.Exists(serversDatPath)
                    ? ReadFile(serversDatPath)
                    : NewRootCompound();
            }
            catch
            {
                // Corrupt/unrecognized file - don't destroy the user's data, just skip silently.
                return;
            }

            if (root.Compound == null) return;

            NbtTag? serversTag = root.Compound.FirstOrDefault(t => t.Name == "servers" && t.Type == TagType.List);
            if (serversTag == null)
            {
                serversTag = new NbtTag { Type = TagType.List, Name = "servers", ListElementType = TagType.Compound, ListItems = new List<NbtTag>() };
                root.Compound.Add(serversTag);
            }

            serversTag.ListElementType = TagType.Compound;
            serversTag.ListItems ??= new List<NbtTag>();

            NbtTag? existing = serversTag.ListItems.FirstOrDefault(item =>
                item.Compound?.FirstOrDefault(t => t.Name == "name")?.Value as string == name);

            if (existing?.Compound != null)
            {
                NbtTag? ipTag = existing.Compound.FirstOrDefault(t => t.Name == "ip");
                if (ipTag != null)
                {
                    ipTag.Value = ip;
                }
                else
                {
                    existing.Compound.Add(new NbtTag { Type = TagType.String, Name = "ip", Value = ip });
                }
            }
            else
            {
                serversTag.ListItems.Insert(0, new NbtTag
                {
                    Type = TagType.Compound,
                    Compound = new List<NbtTag>
                    {
                        new NbtTag { Type = TagType.String, Name = "name", Value = name },
                        new NbtTag { Type = TagType.String, Name = "ip", Value = ip },
                    }
                });
            }

            string? dir = Path.GetDirectoryName(serversDatPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            string tmpPath = serversDatPath + ".tmp";
            using (var stream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                WriteTag(writer, root, writeHeader: true);
            }
            File.Move(tmpPath, serversDatPath, overwrite: true);
        }

        private static NbtTag NewRootCompound() => new NbtTag
        {
            Type = TagType.Compound,
            Name = "",
            Compound = new List<NbtTag>
            {
                new NbtTag { Type = TagType.List, Name = "servers", ListElementType = TagType.Compound, ListItems = new List<NbtTag>() }
            }
        };

        private static NbtTag ReadFile(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read);
            using var reader = new BinaryReader(stream);
            return ReadTaggedTag(reader);
        }

        private static NbtTag ReadTaggedTag(BinaryReader reader)
        {
            var type = (TagType)reader.ReadByte();
            string name = type == TagType.End ? "" : ReadNbtString(reader);
            var tag = new NbtTag { Type = type, Name = name };
            ReadPayload(reader, tag);
            return tag;
        }

        private static void ReadPayload(BinaryReader reader, NbtTag tag)
        {
            switch (tag.Type)
            {
                case TagType.End:
                    break;
                case TagType.Byte:
                    tag.Value = reader.ReadByte();
                    break;
                case TagType.Short:
                    tag.Value = ReadInt16BE(reader);
                    break;
                case TagType.Int:
                    tag.Value = ReadInt32BE(reader);
                    break;
                case TagType.Long:
                    tag.Value = ReadInt64BE(reader);
                    break;
                case TagType.Float:
                    tag.Value = ReadSingleBE(reader);
                    break;
                case TagType.Double:
                    tag.Value = ReadDoubleBE(reader);
                    break;
                case TagType.ByteArray:
                    tag.Value = reader.ReadBytes(ReadInt32BE(reader));
                    break;
                case TagType.String:
                    tag.Value = ReadNbtString(reader);
                    break;
                case TagType.List:
                    tag.ListElementType = (TagType)reader.ReadByte();
                    int count = ReadInt32BE(reader);
                    tag.ListItems = new List<NbtTag>(Math.Max(count, 0));
                    for (int i = 0; i < count; i++)
                    {
                        var item = new NbtTag { Type = tag.ListElementType };
                        ReadPayload(reader, item);
                        tag.ListItems.Add(item);
                    }
                    break;
                case TagType.Compound:
                    tag.Compound = new List<NbtTag>();
                    while (true)
                    {
                        NbtTag child = ReadTaggedTag(reader);
                        if (child.Type == TagType.End) break;
                        tag.Compound.Add(child);
                    }
                    break;
                case TagType.IntArray:
                    {
                        int n = ReadInt32BE(reader);
                        var arr = new int[n];
                        for (int i = 0; i < n; i++) arr[i] = ReadInt32BE(reader);
                        tag.Value = arr;
                        break;
                    }
                case TagType.LongArray:
                    {
                        int n = ReadInt32BE(reader);
                        var arr = new long[n];
                        for (int i = 0; i < n; i++) arr[i] = ReadInt64BE(reader);
                        tag.Value = arr;
                        break;
                    }
            }
        }

        private static void WriteTag(BinaryWriter writer, NbtTag tag, bool writeHeader)
        {
            if (writeHeader)
            {
                writer.Write((byte)tag.Type);
                WriteNbtString(writer, tag.Name);
            }
            WritePayload(writer, tag);
        }

        private static void WritePayload(BinaryWriter writer, NbtTag tag)
        {
            switch (tag.Type)
            {
                case TagType.End:
                    break;
                case TagType.Byte:
                    writer.Write((byte)tag.Value!);
                    break;
                case TagType.Short:
                    WriteInt16BE(writer, (short)tag.Value!);
                    break;
                case TagType.Int:
                    WriteInt32BE(writer, (int)tag.Value!);
                    break;
                case TagType.Long:
                    WriteInt64BE(writer, (long)tag.Value!);
                    break;
                case TagType.Float:
                    WriteSingleBE(writer, (float)tag.Value!);
                    break;
                case TagType.Double:
                    WriteDoubleBE(writer, (double)tag.Value!);
                    break;
                case TagType.ByteArray:
                    {
                        var bytes = (byte[])tag.Value!;
                        WriteInt32BE(writer, bytes.Length);
                        writer.Write(bytes);
                        break;
                    }
                case TagType.String:
                    WriteNbtString(writer, (string)tag.Value!);
                    break;
                case TagType.List:
                    writer.Write((byte)tag.ListElementType);
                    var items = tag.ListItems ?? new List<NbtTag>();
                    WriteInt32BE(writer, items.Count);
                    foreach (var item in items)
                    {
                        item.Type = tag.ListElementType;
                        WritePayload(writer, item);
                    }
                    break;
                case TagType.Compound:
                    foreach (var child in tag.Compound ?? new List<NbtTag>())
                    {
                        WriteTag(writer, child, writeHeader: true);
                    }
                    writer.Write((byte)TagType.End);
                    break;
                case TagType.IntArray:
                    {
                        var arr = (int[])tag.Value!;
                        WriteInt32BE(writer, arr.Length);
                        foreach (int v in arr) WriteInt32BE(writer, v);
                        break;
                    }
                case TagType.LongArray:
                    {
                        var arr = (long[])tag.Value!;
                        WriteInt32BE(writer, arr.Length);
                        foreach (long v in arr) WriteInt64BE(writer, v);
                        break;
                    }
            }
        }

        private static string ReadNbtString(BinaryReader reader)
        {
            int length = ReadUInt16BE(reader);
            byte[] bytes = reader.ReadBytes(length);
            return Encoding.UTF8.GetString(bytes);
        }

        private static void WriteNbtString(BinaryWriter writer, string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            WriteUInt16BE(writer, (ushort)bytes.Length);
            writer.Write(bytes);
        }

        private static short ReadInt16BE(BinaryReader r) => (short)ReadUInt16BE(r);
        private static ushort ReadUInt16BE(BinaryReader r)
        {
            byte[] b = r.ReadBytes(2);
            return (ushort)((b[0] << 8) | b[1]);
        }
        private static int ReadInt32BE(BinaryReader r)
        {
            byte[] b = r.ReadBytes(4);
            return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
        }
        private static long ReadInt64BE(BinaryReader r)
        {
            byte[] b = r.ReadBytes(8);
            long result = 0;
            for (int i = 0; i < 8; i++) result = (result << 8) | b[i];
            return result;
        }
        private static float ReadSingleBE(BinaryReader r)
        {
            byte[] b = r.ReadBytes(4);
            Array.Reverse(b);
            return BitConverter.ToSingle(b, 0);
        }
        private static double ReadDoubleBE(BinaryReader r)
        {
            byte[] b = r.ReadBytes(8);
            Array.Reverse(b);
            return BitConverter.ToDouble(b, 0);
        }

        private static void WriteInt16BE(BinaryWriter w, short v) => WriteUInt16BE(w, (ushort)v);
        private static void WriteUInt16BE(BinaryWriter w, ushort v)
        {
            w.Write((byte)(v >> 8));
            w.Write((byte)v);
        }
        private static void WriteInt32BE(BinaryWriter w, int v)
        {
            w.Write((byte)(v >> 24));
            w.Write((byte)(v >> 16));
            w.Write((byte)(v >> 8));
            w.Write((byte)v);
        }
        private static void WriteInt64BE(BinaryWriter w, long v)
        {
            for (int shift = 56; shift >= 0; shift -= 8)
                w.Write((byte)(v >> shift));
        }
        private static void WriteSingleBE(BinaryWriter w, float v)
        {
            byte[] b = BitConverter.GetBytes(v);
            Array.Reverse(b);
            w.Write(b);
        }
        private static void WriteDoubleBE(BinaryWriter w, double v)
        {
            byte[] b = BitConverter.GetBytes(v);
            Array.Reverse(b);
            w.Write(b);
        }
    }
}
