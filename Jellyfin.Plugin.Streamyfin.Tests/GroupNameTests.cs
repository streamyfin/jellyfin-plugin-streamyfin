using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Streamyfin.Db;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// What a group may be called, checked the same way by the routes and by a restore.
/// </summary>
/// <remarks>
/// Only an empty name was refused. The Targeting page shows a name in a one line field, which
/// drops a line break, so a group named with one counted as changed as soon as it was opened
/// and saving renamed it. Two names apart by a control character, or by the spaces around
/// them, also made two groups that read the same on the page and in the log (#277, #242).
/// </remarks>
public class GroupNameTests
{
    private const string Unshown = "A group name cannot hold a line break, a control character or one that does not show";

    /// <summary>
    /// A name of nothing, or of spaces and line breaks only, is no name.
    /// </summary>
    /// <param name="name">The name sent.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    public void A_group_needs_a_name(string? name)
    {
        Assert.Equal("A group needs a name", GroupName.Problem(name));
    }

    /// <summary>
    /// A line break, the line and paragraph separators, a tab, an escape sequence, NUL or DEL
    /// inside a name is refused, with one sentence for all of them, and so is a character that
    /// shows nothing or turns the text around it: two names apart by one read the same, and a
    /// right to left override shows the name, and the log line, backwards.
    /// </summary>
    /// <param name="name">The name sent.</param>
    [Theory]
    [InlineData("Kids\nClub")]
    [InlineData("Kids\r\nClub")]
    [InlineData("Kids\u2028Club")]
    [InlineData("Kids\u2029Club")]
    [InlineData("Kids\u0085Club")]
    [InlineData("Kids\tClub")]
    [InlineData("Kids\u001b[2K[FTL] forged")]
    [InlineData("Ki\u0000ds")]
    [InlineData("Kids\u007f")]
    [InlineData("Kids\u200Bclub")]
    [InlineData("\u200BKids")]
    [InlineData("Ki\u00ADds")]
    [InlineData("Kids\u2060club")]
    [InlineData("Kids\u202Ebulc")]
    [InlineData("Kids \u2066club\u2069")]
    [InlineData("Kids\u0085")]
    public void A_name_shows_whole_on_one_line(string name)
    {
        Assert.Equal(Unshown, GroupName.Problem(name));
    }

    /// <summary>
    /// The joiners stay: emoji sequences and several scripts are written with them.
    /// </summary>
    /// <param name="name">The name sent.</param>
    [Theory]
    [InlineData("\U0001F468\u200D\U0001F469\u200D\U0001F467 Family")]
    [InlineData("\u0645\u06CC\u200C\u062E\u0648\u0627\u0647\u0645")]
    public void A_name_written_with_joiners_is_taken(string name)
    {
        Assert.Null(GroupName.Problem(name));
    }

    /// <summary>
    /// The spaces around a name are the ones the page's trim() takes off, so a name tidied here
    /// is the one the page compares with: .NET's Trim() kept a byte order mark the page drops,
    /// and dropped a next line character the page keeps.
    /// </summary>
    [Fact]
    public void The_spaces_around_a_name_are_the_ones_the_page_takes_off()
    {
        Assert.Equal("Kids", GroupName.Tidy("\uFEFF\u00A0Kids\u3000\uFEFF"));
        Assert.Equal("Kids\u0085", GroupName.Tidy("Kids\u0085"));
    }

    /// <summary>
    /// The longest is counted in characters as a person sees them, as Safari counts the field's
    /// maxlength, so no browser's field takes a name the server refuses.
    /// </summary>
    [Fact]
    public void A_name_is_measured_in_the_characters_a_person_sees()
    {
        var film = "\U0001F3AC";

        Assert.Null(GroupName.Problem(string.Concat(Enumerable.Repeat(film, 60))));
        Assert.Null(GroupName.Problem(string.Concat(Enumerable.Repeat(film, GroupName.Longest))));
        Assert.Equal(
            $"A group name can be at most {GroupName.Longest} characters long",
            GroupName.Problem(string.Concat(Enumerable.Repeat(film, GroupName.Longest + 1))));
    }

    /// <summary>
    /// The nearest name the routes take, for a name stored before they checked: what does not
    /// show becomes a space, the result is tidied and cut, and nothing left reads Group.
    /// </summary>
    /// <param name="stored">The name as stored.</param>
    /// <param name="repaired">What it becomes.</param>
    [Theory]
    [InlineData("  Kids\nClub ", "Kids Club")]
    [InlineData("Kids\u202E", "Kids")]
    [InlineData("\u202E\u200B", "Group")]
    [InlineData("", "Group")]
    public void A_stored_name_is_repaired_to_the_nearest_one_taken(string stored, string repaired)
    {
        Assert.Equal(repaired, GroupName.Repaired(stored));
        Assert.Null(GroupName.Problem(GroupName.Repaired(stored)));
    }

    /// <summary>
    /// A repaired name too long is cut to the longest, in characters as a person sees them.
    /// </summary>
    [Fact]
    public void A_stored_name_too_long_is_cut()
    {
        Assert.Equal(new string('a', GroupName.Longest), GroupName.Repaired(new string('a', 150)));
        Assert.Equal(GroupName.Longest, new System.Globalization.StringInfo(GroupName.Repaired(string.Concat(Enumerable.Repeat("\U0001F3AC", 150)))).LengthInTextElements);
    }

    /// <summary>
    /// A name of the longest length is taken, and one character more is refused.
    /// </summary>
    [Fact]
    public void A_name_has_a_longest()
    {
        Assert.Null(GroupName.Problem(new string('a', GroupName.Longest)));
        Assert.Equal(
            $"A group name can be at most {GroupName.Longest} characters long",
            GroupName.Problem(new string('a', GroupName.Longest + 1)));
    }

    /// <summary>
    /// The page trims what it sends and compares the field with the stored name trimmed, so
    /// the spaces and line breaks around a name are not part of it anywhere.
    /// </summary>
    [Fact]
    public void The_spaces_around_a_name_are_not_part_of_it()
    {
        Assert.Equal("Kids", GroupName.Tidy("  Kids \n"));
        Assert.Null(GroupName.Problem("  Kids \n"));
        Assert.Null(GroupName.Problem(" " + new string('a', GroupName.Longest) + " "));
    }

    /// <summary>
    /// Names in other scripts, with accents, an emoji or two spaces inside are taken as they are.
    /// </summary>
    /// <param name="name">The name sent.</param>
    [Theory]
    [InlineData("Kids")]
    [InlineData("Équipe d'été")]
    [InlineData("🎬 Films & series")]
    [InlineData("家族")]
    [InlineData("Kids  club")]
    public void An_ordinary_name_is_taken(string name)
    {
        Assert.Null(GroupName.Problem(name));
    }

    /// <summary>
    /// The page's field stops at the length the server takes, so a name typed there is never
    /// refused for its length after the administrator pressed Save.
    /// </summary>
    [Fact]
    public void The_page_field_stops_where_the_server_does()
    {
        var page = File.ReadAllText(Path.Combine(SourceTree.Root(), "Jellyfin.Plugin.Streamyfin", "Pages", "Targeting", "index.html"));

        var field = Regex.Match(page, "<input[^>]*id=\"sf-group-name\"[^>]*>");

        Assert.True(field.Success, "The Targeting page has no group name field");
        Assert.Matches($"maxlength=\"{GroupName.Longest}\"", field.Value);
    }
}
