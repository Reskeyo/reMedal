using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MedalShadowSync;

// Minimal pure-C# Electron ASAR reader & single-file replacer (no Node.js dependency)
public static class AsarArchive
{
    public static byte[] ReadFile(string asarPath, string innerPath)
    {
        using var fs = new FileStream(asarPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var br = new BinaryReader(fs, Encoding.UTF8, leaveOpen: true);

        var (header, dataOffset) = ReadHeader(br);
        var node = FindNode(header, innerPath)
            ?? throw new FileNotFoundException($"'{innerPath}' not found inside {asarPath}");

        var offset = long.Parse(node["offset"]!.GetValue<string>());
        var size = node["size"]!.GetValue<int>();

        fs.Seek(dataOffset + offset, SeekOrigin.Begin);
        return br.ReadBytes(size);
    }

    public static void ReplaceFile(string asarPath, string innerPath, byte[] newContent)
    {
        var tempPath = asarPath + ".remedal.tmp";
        try
        {
            using (var src = new FileStream(asarPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var br = new BinaryReader(src, Encoding.UTF8, leaveOpen: true))
            {
                var (header, oldDataOffset) = ReadHeader(br);
                var targetNode = FindNode(header, innerPath)
                    ?? throw new FileNotFoundException($"'{innerPath}' not found inside {asarPath}");

                // Collect all packed file entries in order so we can rebuild offsets cleanly
                var packedEntries = new List<(JsonObject Node, long OldOffset, int OldSize, bool IsTarget)>();
                CollectPackedEntries(header, targetNode, packedEntries);

                long newOffset = 0;
                foreach (var entry in packedEntries)
                {
                    entry.Node["offset"] = newOffset.ToString();
                    if (entry.IsTarget)
                    {
                        entry.Node["size"] = newContent.Length;
                        UpdateIntegrity(entry.Node, newContent);
                        newOffset += newContent.Length;
                    }
                    else
                    {
                        newOffset += entry.OldSize;
                    }
                }

                var newHeaderJson = header.ToJsonString();
                var jsonBytes = Encoding.UTF8.GetBytes(newHeaderJson);
                // ASAR Chromium Pickle aligns string payload to 4-byte boundary
                var paddedLen = (jsonBytes.Length + 3) & ~3;
                var stringPicklePayload = 4 + paddedLen; // uint32 stringLen + padded bytes
                var headerPickleSize = 4 + stringPicklePayload; // uint32 innerPickleSize + innerPickle

                using var dst = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                using var bw = new BinaryWriter(dst, Encoding.UTF8, leaveOpen: true);

                // Outer pickle: payloadSize = 4, value = headerPickleSize
                bw.Write((uint)4);
                bw.Write((uint)headerPickleSize);
                // Inner pickle: payloadSize = stringPicklePayload, stringLen = jsonBytes.Length, bytes + padding
                bw.Write((uint)stringPicklePayload);
                bw.Write((uint)jsonBytes.Length);
                bw.Write(jsonBytes);
                for (int i = jsonBytes.Length; i < paddedLen; i++)
                    bw.Write((byte)0);

                // Stream all file payloads into the new archive
                var buffer = new byte[65536];
                foreach (var entry in packedEntries)
                {
                    if (entry.IsTarget)
                    {
                        dst.Write(newContent, 0, newContent.Length);
                    }
                    else if (entry.OldSize > 0)
                    {
                        src.Seek(oldDataOffset + entry.OldOffset, SeekOrigin.Begin);
                        int remaining = entry.OldSize;
                        while (remaining > 0)
                        {
                            int toRead = Math.Min(buffer.Length, remaining);
                            int read = src.Read(buffer, 0, toRead);
                            if (read <= 0)
                                throw new EndOfStreamException("Unexpected EOF while repacking app.asar");
                            dst.Write(buffer, 0, read);
                            remaining -= read;
                        }
                    }
                }
            }

            File.Move(tempPath, asarPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    private static (JsonObject Header, long DataOffset) ReadHeader(BinaryReader br)
    {
        _ = br.ReadUInt32(); // outer pickle payload size (4)
        var headerSize = br.ReadUInt32();
        _ = br.ReadUInt32(); // inner pickle payload size
        var jsonSize = br.ReadUInt32();
        var jsonBytes = br.ReadBytes((int)jsonSize);
        var header = JsonNode.Parse(jsonBytes)?.AsObject()
            ?? throw new InvalidDataException("Invalid ASAR JSON header");
        return (header, 8L + headerSize);
    }

    private static JsonObject? FindNode(JsonObject root, string innerPath)
    {
        JsonObject? current = root;
        foreach (var part in innerPath.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current?["files"] is not JsonObject files || files[part] is not JsonObject next)
                return null;
            current = next;
        }
        return current;
    }

    private static void CollectPackedEntries(
        JsonObject node,
        JsonObject target,
        List<(JsonObject Node, long OldOffset, int OldSize, bool IsTarget)> list)
    {
        if (node["files"] is JsonObject files)
        {
            foreach (var kv in files)
            {
                if (kv.Value is JsonObject child)
                    CollectPackedEntries(child, target, list);
            }
            return;
        }

        // Skip unpacked files or symlinks (no "offset" field)
        if (node["offset"] is null || node["size"] is null)
            return;

        list.Add((node, long.Parse(node["offset"]!.GetValue<string>()), node["size"]!.GetValue<int>(), ReferenceEquals(node, target)));
    }

    private static void UpdateIntegrity(JsonObject node, byte[] content)
    {
        if (node["integrity"] is not JsonObject integrity)
            return;

        var blockSize = integrity["blockSize"]?.GetValue<int>() ?? 4 * 1024 * 1024;
        var fullHash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        integrity["hash"] = fullHash;

        var blocks = new JsonArray();
        for (int pos = 0; pos < content.Length; pos += blockSize)
        {
            var len = Math.Min(blockSize, content.Length - pos);
            blocks.Add(Convert.ToHexString(SHA256.HashData(content.AsSpan(pos, len))).ToLowerInvariant());
        }
        if (blocks.Count == 0)
            blocks.Add(fullHash);
        integrity["blocks"] = blocks;
    }
}
