using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

namespace Jellyfin.Plugin.Streamyfin.Configuration;

/// <summary>
/// What an earlier version of the plugin changed in Jellyfin's plugin XML since this
/// version last read it.
/// </summary>
/// <remarks>
/// An administrator who goes back to 0.68.1.0 and then updates again has changed
/// settings in a file this version had read once, at the import, and never again. The
/// update served the earlier values and said nothing.
///
/// <para>
/// This version never writes that file, so comparing it with the copy taken the last
/// time it was read says exactly what changed there. Those changes are named, not taken
/// over. The file is not a faithful record of what an administrator chose: an older
/// version writes it without the settings it does not know, and Jellyfin fills a missing
/// one with that version's defaults, a placeholder hidden library among them.
/// </para>
///
/// <para>
/// An entry is one setting, one notification, one key under <c>other</c>: each child of
/// a top level object, or the top level value itself when it is not an object. An entry
/// the file lost is not named, since that is what an older version writing the file
/// does to every setting it never had. One the file changed to the value already used
/// here is not named either: there is nothing to set again.
/// </para>
/// </remarks>
internal static class LegacyFileChanges
{
    /// <summary>
    /// Finds the entries the file changed or gained, where it now differs from the
    /// configuration here.
    /// </summary>
    /// <param name="lastRead">The file as this version last read it.</param>
    /// <param name="now">The file as it stands.</param>
    /// <param name="current">The configuration here.</param>
    /// <returns>The entries, named as the Yaml tab shows them.</returns>
    internal static IReadOnlyList<string> NotUsedHere(JsonObject lastRead, JsonObject now, JsonObject current)
    {
        ArgumentNullException.ThrowIfNull(lastRead);
        ArgumentNullException.ThrowIfNull(now);
        ArgumentNullException.ThrowIfNull(current);

        var entries = new List<string>();

        foreach (var path in Paths(lastRead).Union(Paths(now)))
        {
            var after = Get(now, path);

            if (after is null
                || Same(Get(lastRead, path), after)
                || Same(Get(current, path), after))
            {
                continue;
            }

            entries.Add(path.Name);
        }

        return entries;
    }

    /// <summary>
    /// Compares two configurations the way the file can tell them apart.
    /// </summary>
    /// <param name="left">One configuration.</param>
    /// <param name="right">The other.</param>
    /// <returns>True when they hold the same values.</returns>
    /// <remarks>
    /// Jellyfin's XML serializer creates every list it meets, so a list left null is read
    /// back empty. The file cannot tell the two apart, and neither does this.
    /// </remarks>
    internal static bool Same(JsonNode? left, JsonNode? right) =>
        JsonNode.DeepEquals(WithoutEmptyLists(left), WithoutEmptyLists(right));

    private static JsonNode? WithoutEmptyLists(JsonNode? node)
    {
        if (node is JsonArray { Count: 0 })
        {
            return null;
        }

        var copy = node?.DeepClone();
        Prune(copy);
        return copy;
    }

    private static void Prune(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject entries:
                foreach (var key in entries.Where(e => e.Value is JsonArray { Count: 0 }).Select(e => e.Key).ToList())
                {
                    entries.Remove(key);
                }

                foreach (var (_, value) in entries)
                {
                    Prune(value);
                }

                break;
            case JsonArray items:
                foreach (var item in items)
                {
                    Prune(item);
                }

                break;
        }
    }

    private static IEnumerable<EntryPath> Paths(JsonObject document)
    {
        foreach (var (section, value) in document)
        {
            if (value is JsonObject entries)
            {
                foreach (var (entry, _) in entries)
                {
                    yield return new EntryPath(section, entry);
                }
            }
            else
            {
                yield return new EntryPath(section, null);
            }
        }
    }

    private static JsonNode? Get(JsonObject document, EntryPath path)
    {
        var value = document[path.Section];

        if (path.Entry is null)
        {
            return value;
        }

        return value is JsonObject entries ? entries[path.Entry] : null;
    }

    private readonly record struct EntryPath(string Section, string? Entry)
    {
        // The name an administrator finds in the Yaml tab.
        public string Name => Entry is null ? Section : Section + "." + Entry;
    }
}
