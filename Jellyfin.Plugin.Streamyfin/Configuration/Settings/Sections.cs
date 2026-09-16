using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// What a home section is, and what fills it.
/// </summary>
/// <remarks>
/// A section carried four nullable siblings, <c>items</c>, <c>nextUp</c>, <c>latest</c>
/// and <c>custom</c>, and nothing anywhere said they were exclusive. The app read them
/// as a cascade of ifs, so a section carrying two silently used whichever the cascade
/// tested first, and a section carrying none drew an empty row with a title. Adding a
/// kind meant adding a fifth sibling to every one of those cascades.
///
/// <para>
/// The kind is now declared. The payloads keep their names, because every copy of the
/// app in the field reads them and a plugin that renamed them would empty the home
/// screen of everyone who had not updated, but exactly one of them is allowed and the
/// server says which it is.
/// </para>
/// </remarks>
public static class Sections
{
    /// <summary>
    /// The kind a section declares, or the one its payload implies.
    /// </summary>
    /// <param name="section">The section.</param>
    /// <returns>The kind, or <c>null</c> when nothing settles it.</returns>
    /// <remarks>
    /// A configuration written before the kind existed carries no <c>kind</c> at all,
    /// and there are a lot of those. One payload is an unambiguous answer, so it is
    /// read as one rather than refused, and nothing has to be rewritten to keep
    /// working.
    /// </remarks>
    public static SectionKind? KindOf(Section? section)
    {
        if (section is null)
        {
            return null;
        }

        return section.kind ?? Implied(section);
    }

    /// <summary>
    /// Fills in the kind of every section that does not declare one.
    /// </summary>
    /// <param name="home">The home layout, which may be null.</param>
    /// <remarks>
    /// This is the whole migration. A stored configuration is not rewritten on read,
    /// which would mean writing to the database from a GET; it is answered with the
    /// kind filled in, and the stored copy gains it the next time an administrator
    /// saves. Either way the app is served a section that says what it is.
    /// </remarks>
    public static void Declare(Home? home)
    {
        foreach (var section in home?.sections ?? [])
        {
            section.kind ??= Implied(section);
        }
    }

    /// <summary>
    /// Puts the sections in the order the home screen shows them.
    /// </summary>
    /// <param name="home">The home layout, which may be null.</param>
    /// <remarks>
    /// A section that declares an <c>order</c> is placed by it, lowest first. One that
    /// does not is placed where it was written, since its position is its number, so a
    /// configuration that never mentions <c>order</c> comes out exactly as it went in.
    /// The two scales are the same one, and where they meet the written number wins:
    /// <c>order: 0</c> pins a section above the one that merely happens to be written
    /// first. Two sections claiming the same number keep the order they were written in,
    /// which is what a stable sort is for: an administrator who numbers two the same has
    /// said they do not mind, not that the server may shuffle them on each answer.
    ///
    /// <para>
    /// Every section comes out carrying its position, so sorting an answer twice gives
    /// the same answer: the rule reads a section's place from its number, and a pass
    /// that moved it would otherwise leave the next pass reading the new place as if it
    /// had been asked for.
    /// </para>
    ///
    /// <para>
    /// Done on the way out, next to the kind, and nothing is written back: a GET does
    /// not rewrite the database.
    /// </para>
    /// </remarks>
    public static void Sort(Home? home)
    {
        var sections = home?.sections;
        if (sections is null || sections.Length < 2)
        {
            return;
        }

        var sorted = sections
            .Select((section, index) => (section, index))
            .OrderBy(pair => pair.section?.order ?? pair.index)
            // A number that was written wins the tie against one that was inferred from a
            // position: an administrator who writes `order: 0` to pin a section to the top
            // means it, and the section that merely happens to sit first does not.
            .ThenBy(pair => pair.section?.order is null ? 1 : 0)
            .ThenBy(pair => pair.index)
            .Select(pair => pair.section)
            .ToArray();

        // Each section leaves saying where it ended up. Without that, a second pass over
        // the same objects would read a position that the first pass had already moved,
        // and two routes that both sort would answer differently: the legacy shim and the
        // resolved route disagreed exactly that way before this line existed.
        for (var index = 0; index < sorted.Length; index++)
        {
            if (sorted[index] is { } section)
            {
                section.order = index;
            }
        }

        home!.sections = sorted;
    }

    /// <summary>
    /// Puts the sections of these settings in the order the home screen shows them.
    /// </summary>
    /// <param name="settings">The settings, which may be null.</param>
    public static void Sort(Settings? settings) => Sort(settings?.home?.value);

    /// <summary>
    /// Fills in the kind of every section in these settings.
    /// </summary>
    /// <param name="settings">The settings, which may be null.</param>
    public static void Declare(Settings? settings) => Declare(settings?.home?.value);

    /// <summary>
    /// The reasons a home layout cannot be stored, one line each.
    /// </summary>
    /// <param name="home">The home layout, which may be null.</param>
    /// <returns>The problems, in section order. Empty when there are none.</returns>
    public static IReadOnlyList<string> Problems(Home? home)
    {
        var sections = home?.sections;
        if (sections is null)
        {
            return [];
        }

        var problems = new List<string>();

        for (var index = 0; index < sections.Length; index++)
        {
            var section = sections[index];
            if (section is null)
            {
                problems.Add(string.Format(CultureInfo.InvariantCulture, "{0} is empty.", Name(null, index)));
                continue;
            }

            var name = Name(section, index);
            var carried = Carried(section).ToList();

            if (carried.Count > 1)
            {
                problems.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} carries {1} queries, {2}, and a section is filled by one.",
                    name,
                    carried.Count,
                    string.Join(" and ", carried)));
                continue;
            }

            if (carried.Count == 0)
            {
                problems.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} carries no query, so it would draw an empty row. Give it one of {1}.",
                    name,
                    string.Join(", ", Kinds)));
                continue;
            }

            if (section.kind is not null && section.kind.Value != carried[0])
            {
                // No article before the kind: "a items query" is what writing one gives
                // you, and the kinds are spelled the way the payload is rather than in
                // English, so there is no right article to pick.
                problems.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} says it is a {1} section, and the query beside it is {2}.",
                    name,
                    section.kind.Value,
                    carried[0]));
            }
        }

        return problems;
    }

    /// <summary>
    /// The reasons the home layout in these settings cannot be stored.
    /// </summary>
    /// <param name="settings">The settings, which may be null.</param>
    /// <returns>The problems. Empty when there are none.</returns>
    public static IReadOnlyList<string> Problems(Settings? settings) => Problems(settings?.home?.value);

    private static readonly SectionKind[] Kinds = Enum.GetValues<SectionKind>();

    // Named for a message an administrator reads, so the title first when there is one:
    // a position alone is no help in a list of fifteen, and a title alone is no help
    // when two sections share it.
    private static string Name(Section? section, int index)
    {
        var position = string.Format(CultureInfo.InvariantCulture, "Home section {0}", index + 1);

        return string.IsNullOrWhiteSpace(section?.title)
            ? position
            : string.Format(CultureInfo.InvariantCulture, "{0} ({1})", position, section!.title);
    }

    private static IEnumerable<SectionKind> Carried(Section section)
    {
        if (section.items is not null)
        {
            yield return SectionKind.items;
        }

        if (section.nextUp is not null)
        {
            yield return SectionKind.nextUp;
        }

        if (section.latest is not null)
        {
            yield return SectionKind.latest;
        }

        if (section.custom is not null)
        {
            yield return SectionKind.custom;
        }
    }

    private static SectionKind? Implied(Section section)
    {
        var carried = Carried(section).Take(2).ToList();

        return carried.Count == 1 ? carried[0] : null;
    }
}
