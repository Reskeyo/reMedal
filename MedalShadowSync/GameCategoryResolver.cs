using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace MedalShadowSync;

public sealed record ResolvedCategory(string CategoryId, string DisplayName, bool IsImportedFallback);

public static partial class GameCategoryResolver
{
    public const string ImportedCategoryId = "bvOYu0GmuA";

    private sealed class CatalogEntry
    {
        [JsonPropertyName("id")] public string Id { get; set; } = "";
        [JsonPropertyName("n")] public string Name { get; set; } = "";
        [JsonPropertyName("a")] public string? Alt { get; set; }
    }

    // Normalized name -> (CategoryId, DisplayName), populated lazily from embedded games.json.gz
    private static readonly Lazy<Dictionary<string, (string Id, string Name)>> Catalog = new(LoadCatalog);

    private static Dictionary<string, (string Id, string Name)> LoadCatalog()
    {
        var map = new Dictionary<string, (string Id, string Name)>(StringComparer.Ordinal);
        var asm = Assembly.GetExecutingAssembly();
        var resName = asm.GetManifestResourceNames()
            .FirstOrDefault(r => r.EndsWith("games.json.gz", StringComparison.OrdinalIgnoreCase));

        if (resName is null)
            return map;

        using var stream = asm.GetManifestResourceStream(resName);
        if (stream is null)
            return map;

        using var gz = new GZipStream(stream, CompressionMode.Decompress);
        var entries = JsonSerializer.Deserialize<List<CatalogEntry>>(gz);
        if (entries is null)
            return map;

        // Entries are sorted by popularity descending, so TryAdd keeps the most popular match
        foreach (var e in entries)
        {
            if (string.IsNullOrWhiteSpace(e.Id) || string.IsNullOrWhiteSpace(e.Name))
                continue;

            var normName = Normalize(e.Name);
            if (normName.Length > 0)
                map.TryAdd(normName, (e.Id, e.Name));

            if (!string.IsNullOrWhiteSpace(e.Alt))
            {
                var normAlt = Normalize(e.Alt);
                if (normAlt.Length > 0)
                    map.TryAdd(normAlt, (e.Id, e.Name));
            }
        }

        return map;
    }

    public static ResolvedCategory Resolve(SqliteConnection? conn, string fullVideoPath)
    {
        var folderName = Path.GetFileName(Path.GetDirectoryName(fullVideoPath));
        return ResolveByGameName(conn, folderName);
    }

    public static ResolvedCategory ResolveByGameName(SqliteConnection? conn, string? rawGameName)
    {
        if (string.IsNullOrWhiteSpace(rawGameName) ||
            rawGameName.Equals("Desktop", StringComparison.OrdinalIgnoreCase) ||
            rawGameName.Equals("Base Profile", StringComparison.OrdinalIgnoreCase))
        {
            return new ResolvedCategory(ImportedCategoryId, "Imported", true);
        }

        var candidates = BuildCandidates(rawGameName);

        // 1. Check user's own Medal DB first (learns custom/renamed categories like "Siege X" or "GTA V")
        if (conn is not null)
        {
            var localMatch = TryMatchLocalDb(conn, candidates);
            if (localMatch is not null)
                return localMatch;
        }

        // 2. Check embedded offline Medal game catalog
        var catalog = Catalog.Value;
        foreach (var candidate in candidates)
        {
            if (catalog.TryGetValue(candidate, out var hit))
                return new ResolvedCategory(hit.Id, hit.Name, false);
        }

        return new ResolvedCategory(ImportedCategoryId, "Imported", true);
    }

    private static ResolvedCategory? TryMatchLocalDb(SqliteConnection conn, List<string> candidates)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT category_id, MAX(json_extract(metadata, '$.remoteContent.category.categoryName'))
            FROM contents
            WHERE category_id IS NOT NULL AND category_id != @imported
            GROUP BY category_id
            """;
        cmd.Parameters.AddWithValue("@imported", ImportedCategoryId);

        var localMap = new Dictionary<string, (string Id, string Name)>(StringComparer.Ordinal);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var id = r.IsDBNull(0) ? null : r.GetString(0);
            var name = r.IsDBNull(1) ? null : r.GetString(1);
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(name))
                continue;

            var norm = Normalize(name);
            if (norm.Length > 0)
                localMap.TryAdd(norm, (id, name));
        }

        foreach (var c in candidates)
        {
            if (localMap.TryGetValue(c, out var hit))
                return new ResolvedCategory(hit.Id, hit.Name, false);
        }

        return null;
    }

    // Builds normalized lookup candidates, including dropping up to 2 leading publisher words
    // (e.g. "Tom Clancy's Rainbow Six  Siege" -> "rainbow six siege") and common ShadowPlay suffixes ("Enhanced", "Demo")
    private static List<string> BuildCandidates(string rawName)
    {
        var list = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string s)
        {
            var norm = Normalize(s);
            if (norm.Length > 0 && seen.Add(norm))
                list.Add(norm);
        }

        Add(rawName);

        // Strip trailing "Enhanced" / "Demo" if present
        var trimmedSuffix = TrailingSuffixRegex().Replace(rawName, "").Trim();
        if (!string.Equals(trimmedSuffix, rawName, StringComparison.Ordinal))
            Add(trimmedSuffix);

        // Drop up to 3 leading words (matches Medal's publisher-prefix stripping like "Tom Clancy's ")
        var words = Normalize(rawName).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int skip = 1; skip <= 3 && words.Length - skip >= 2; skip++)
            Add(string.Join(' ', words.Skip(skip)));

        return list;
    }

    public static string Normalize(string input)
    {
        var noApos = input.ToLowerInvariant().Replace("'", "").Replace("\u2019", "");
        var cleaned = NonAlnumRegex().Replace(noApos, " ");
        return MultiSpaceRegex().Replace(cleaned, " ").Trim();
    }

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlnumRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex MultiSpaceRegex();

    [GeneratedRegex(@"\s+(enhanced|demo|edition)$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingSuffixRegex();
}
