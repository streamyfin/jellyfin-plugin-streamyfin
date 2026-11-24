using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Streamyfin.Db;

/// <summary>
/// What a group may be called. The store holds every name to it, so the routes, a restore and
/// any later writer store the same names, and a name stored before it is repaired at start-up.
/// </summary>
public static class GroupName
{
    /// <summary>
    /// The longest name a group may have, counted in the characters a person sees, as Safari
    /// counts a field's maxlength; Chrome and Firefox count UTF-16 units, never fewer, so no
    /// browser's field takes a name the server refuses. A name is read in a list, and nothing
    /// longer than this reads there.
    /// </summary>
    public const int Longest = 100;

    // What does not show on one line as itself: every control character, the line and paragraph
    // separators, what shows nothing (the zero width space, the word joiner, the soft hyphen, the
    // byte order mark, the Mongolian vowel separator), and the bidi embeddings, overrides and
    // isolates, which turn the text around them. The zero width joiner and non-joiner stay:
    // emoji sequences and several scripts are written with them.
    private static readonly Regex Unshown = new(
        "[\\p{Cc}\\u2028\\u2029\\u00AD\\u180E\\u200B\\u2060\\uFEFF\\u202A-\\u202E\\u2066-\\u2069]+",
        RegexOptions.Compiled);

    // The characters JavaScript's String.prototype.trim() takes off, which is what the page
    // trims a name with: the space separators, the tab and line ends, and the byte order mark.
    // .NET's Trim() keeps the byte order mark and takes off U+0085, which the page keeps.
    private static readonly char[] Spaces =
    [
        '\t', '\n', '\v', '\f', '\r', ' ', '\u00A0', '\u1680', '\u2000', '\u2001', '\u2002', '\u2003', '\u2004',
        '\u2005', '\u2006', '\u2007', '\u2008', '\u2009', '\u200A', '\u2028', '\u2029', '\u202F', '\u205F', '\u3000', '\uFEFF'
    ];

    /// <summary>
    /// The name as it is stored, without the spaces around it that the page trims off. The page
    /// compares its field with the stored name trimmed, so with them a group counted as changed
    /// as soon as it was opened, and was another group than the same name without them.
    /// </summary>
    /// <param name="name">The name sent.</param>
    /// <returns>The name to store.</returns>
    public static string Tidy(string? name) => name?.Trim(Spaces) ?? string.Empty;

    /// <summary>
    /// Why a group cannot be called <paramref name="name"/>, judged on the name as it would be
    /// stored.
    /// </summary>
    /// <param name="name">The name sent.</param>
    /// <returns>A sentence for the page, or <c>null</c> when the name is taken as it is.</returns>
    public static string? Problem(string? name)
    {
        var tidied = Tidy(name);

        if (tidied.Length == 0)
        {
            return "A group needs a name";
        }

        // The page's field holds one line, so it dropped a line break and saving renamed the
        // group; two names apart by a character that does not show read the same on the page
        // and in the log, and an override shows both backwards.
        if (Unshown.IsMatch(tidied))
        {
            return "A group name cannot hold a line break, a control character or one that does not show";
        }

        return new StringInfo(tidied).LengthInTextElements > Longest
            ? $"A group name can be at most {Longest} characters long"
            : null;
    }

    /// <summary>
    /// The nearest name the store takes, for a name stored before it checked them: what does not
    /// show becomes a space, the result is tidied and cut to the longest, and a name with nothing
    /// left reads Group.
    /// </summary>
    /// <param name="name">The name as stored.</param>
    /// <returns>A name <see cref="Problem"/> takes.</returns>
    public static string Repaired(string? name)
    {
        var shown = Tidy(Unshown.Replace(name ?? string.Empty, " "));
        var cut = Tidy(Cut(shown, Longest));
        return cut.Length == 0 ? "Group" : cut;
    }

    /// <summary>
    /// <paramref name="name"/> with <paramref name="suffix"/> after it, the name cut so both fit
    /// in the longest: how two repaired names that came out the same are told apart.
    /// </summary>
    /// <param name="name">A name <see cref="Problem"/> takes.</param>
    /// <param name="suffix">What tells it apart, such as " (2)".</param>
    /// <returns>A name <see cref="Problem"/> takes.</returns>
    public static string WithSuffix(string name, string suffix) =>
        Tidy(Cut(name, Longest - new StringInfo(suffix).LengthInTextElements)) + suffix;

    private static string Cut(string text, int characters)
    {
        var info = new StringInfo(text);
        return info.LengthInTextElements <= characters ? text : info.SubstringByTextElements(0, characters);
    }
}
