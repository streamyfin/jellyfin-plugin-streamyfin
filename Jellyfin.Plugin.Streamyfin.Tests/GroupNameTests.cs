using System.IO;
using System.Text.RegularExpressions;
using Jellyfin.Plugin.Streamyfin.Api;
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
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\t")]
    public void A_group_needs_a_name(string? name)
    {
        Assert.Equal("A group needs a name", GroupName.Problem(name));
    }

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
    public void A_name_fits_on_one_line(string name)
    {
        Assert.Equal("A group name cannot hold a line break or another control character", GroupName.Problem(name));
    }

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
