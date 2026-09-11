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
                problems.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} says it is a {1} section and carries a {2} query.",
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
