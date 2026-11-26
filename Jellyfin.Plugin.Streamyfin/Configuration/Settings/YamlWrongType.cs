using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// Names the setting a Yaml document gave a value of the wrong type, and what it takes.
/// </summary>
/// <remarks>
/// The deserializer says which CLR type it could not make, never which setting, so on a long
/// document only the line and column led to the mistake. Only one setting's value, its lock,
/// or the setting itself written as a bare value is named; anything else keeps its words.
/// </remarks>
public static class YamlWrongType
{
    private const int LongestShown = 40;
    private const int DeepestWalked = 64;

    /// <summary>
    /// The sentence for a value of the wrong type where the reader stopped, if it is one setting's.
    /// </summary>
    /// <param name="yaml">The document.</param>
    /// <param name="where">Where the reader stopped.</param>
    /// <param name="overflowed">Whether the reader read a whole number too large for the setting's type.</param>
    /// <returns>The sentence, or <c>null</c> when the place is not one setting's value or lock.</returns>
    /// <remarks>
    /// It only rewords a refusal, so it never throws: a document it cannot read, or one deeper
    /// than any configuration, keeps the deserializer's words.
    /// </remarks>
    public static string? Describe(string yaml, Mark where, bool overflowed = false)
    {
        try
        {
            return Name(yaml, where, overflowed);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? Name(string yaml, Mark where, bool overflowed)
    {
        if (Locate(yaml, where) is not { Path: var path, Found: var found }
            || path.Length < 2 || path.Length > 3 || path[0] != "settings")
        {
            return null;
        }

        // Found under the name it was written with, an alias included, which the path keeps.
        var key = path[1];
        var descriptor = SettingsSchema.Descriptors.FirstOrDefault(d => d.Key == key
            || d.Property.GetCustomAttributes<AlsoKnownAsAttribute>().Any(alias => alias.Name == key));
        if (descriptor is null)
        {
            return null;
        }

        var title = descriptor.DisplayName ?? descriptor.Key;
        if (path.Length == 2)
        {
            // The setting written as a bare value or a list rather than as its block. A block
            // that is wrong inside is the deserializer's to say.
            if (found == "a block of keys")
            {
                return null;
            }

            var bare = descriptor.IsLockable ? "a block with locked and value" : Takes(descriptor.ValueType);
            return bare is null ? null : Sentence(title, $"settings.{key}", bare, found);
        }

        var takes = path[2] switch
        {
            "locked" when descriptor.IsLockable => "true or false",
            "value" when descriptor.IsLockable => (overflowed ? Range(descriptor) : null) ?? Takes(descriptor.ValueType),
            _ => null,
        };

        return takes is null ? null : Sentence(title, $"settings.{key}.{path[2]}", takes, found);
    }

    // The keys down to the node that starts where the reader stopped, and what that node is.
    // Read event by event rather than as a tree: a tree is walked by recursion, which a deep
    // document overflows, and an alias would be walked once for every place it is used.
    private static (string[] Path, string Found)? Locate(string yaml, Mark where)
    {
        var parser = new Parser(new StringReader(yaml));
        var frames = new List<Frame>();

        while (parser.MoveNext())
        {
            var current = parser.Current;
            switch (current)
            {
                case StreamStart or DocumentStart or Comment:
                    continue;
                case DocumentEnd or StreamEnd or null:
                    return null;
                case MappingEnd or SequenceEnd:
                    frames.RemoveAt(frames.Count - 1);
                    Answered(frames);
                    continue;
            }

            var top = frames.Count > 0 ? frames[^1] : null;
            if (top is { IsMapping: true, AtKey: true })
            {
                // A key that is not plain text is past what any configuration writes.
                if (current is not Scalar { Value: var name })
                {
                    return null;
                }

                top.Key = name;
                top.AtKey = false;
                continue;
            }

            if (current.Start.Index == where.Index)
            {
                // A tag the reader does not know is what has to change, not the value's type.
                if (current is NodeEvent { Tag.IsEmpty: false })
                {
                    return null;
                }

                var found = current switch
                {
                    Scalar { Value: var text } => $"'{Shown(text)}'",
                    SequenceStart => "a list",
                    MappingStart => "a block of keys",
                    _ => null,
                };

                return found is null ? null : ([.. frames.Select(frame => frame.Key ?? "[]")], found);
            }

            switch (current)
            {
                case MappingStart:
                    frames.Add(new Frame { IsMapping = true });
                    break;
                case SequenceStart:
                    frames.Add(new Frame());
                    break;
                default:
                    Answered(frames);
                    break;
            }

            if (frames.Count > DeepestWalked)
            {
                return null;
            }
        }

        return null;
    }

    // A value was read: the mapping it belongs to waits for its next key.
    private static void Answered(List<Frame> frames)
    {
        if (frames.Count > 0 && frames[^1].IsMapping)
        {
            frames[^1].AtKey = true;
        }
    }

    // A whole number too large for its type is a whole number: what it takes is the range,
    // the setting's own when it declares one. Told by the reader, which knows every way YAML
    // writes one (2_147_483_648, 0x80000000), and not by reading the text again.
    private static string? Range(SettingDescriptor descriptor)
    {
        var plain = Nullable.GetUnderlyingType(descriptor.ValueType) ?? descriptor.ValueType;
        if (!(plain == typeof(int) || plain == typeof(long) || plain == typeof(short)))
        {
            return null;
        }

        var (lowest, highest) = descriptor.Bounds is { } bounds
            ? (bounds.Minimum.ToString(CultureInfo.InvariantCulture), bounds.Maximum.ToString(CultureInfo.InvariantCulture))
            : plain == typeof(int) ? (int.MinValue.ToString(CultureInfo.InvariantCulture), int.MaxValue.ToString(CultureInfo.InvariantCulture))
            : plain == typeof(long) ? (long.MinValue.ToString(CultureInfo.InvariantCulture), long.MaxValue.ToString(CultureInfo.InvariantCulture))
            : (short.MinValue.ToString(CultureInfo.InvariantCulture), short.MaxValue.ToString(CultureInfo.InvariantCulture));

        return $"a whole number from {lowest} to {highest}";
    }

    // On the one line of a banner: line breaks become spaces, and the cut never splits a
    // character written as two UTF-16 units.
    private static string Shown(string text)
    {
        var line = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (line.Length <= LongestShown)
        {
            return line;
        }

        var cut = char.IsHighSurrogate(line[LongestShown - 1]) ? LongestShown - 1 : LongestShown;
        return line[..cut] + "…";
    }

    private static string Sentence(string title, string path, string takes, string found) =>
        string.Format(CultureInfo.InvariantCulture, "{0} ({1}) takes {2}, and this is {3}.", title, path, takes, found);

    private static string? Takes(Type type)
    {
        var plain = Nullable.GetUnderlyingType(type) ?? type;

        if (plain == typeof(bool))
        {
            return "true or false";
        }

        if (plain == typeof(int) || plain == typeof(long) || plain == typeof(short))
        {
            return "a whole number";
        }

        if (plain == typeof(double) || plain == typeof(float) || plain == typeof(decimal))
        {
            return "a number";
        }

        if (plain == typeof(string))
        {
            return "text";
        }

        // Under the names a document is written with, which [EnumMember] gives when a C#
        // name could not be one.
        if (plain.IsEnum)
        {
            return "one of " + string.Join(", ", plain.GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(field => field.GetCustomAttribute<EnumMemberAttribute>()?.Value ?? field.Name));
        }

        return typeof(IEnumerable).IsAssignableFrom(plain) ? "a list" : null;
    }

    private sealed class Frame
    {
        public bool IsMapping { get; init; }

        public bool AtKey { get; set; } = true;

        public string? Key { get; set; }
    }
}
