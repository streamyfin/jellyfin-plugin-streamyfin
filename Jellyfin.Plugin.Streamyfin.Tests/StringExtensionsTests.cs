using Jellyfin.Plugin.Streamyfin.Extensions;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// A value from outside the server, written into its log.
/// </summary>
/// <remarks>
/// A Seerr webhook decides its own notification type and user name. Written as sent, a
/// line break in either starts an entry of its own in the log file, one that reads as
/// the server's.
/// </remarks>
public class StringExtensionsTests
{
    [Theory]
    [InlineData("MEDIA_PENDING\n[ERR] forged", "MEDIA_PENDING [ERR] forged")]
    [InlineData("MEDIA_PENDING\r\n[ERR] forged", "MEDIA_PENDING [ERR] forged")]
    [InlineData("a\rb", "a b")]
    [InlineData("a\u000Bb", "a b")]
    [InlineData("a\u000Cb", "a b")]
    [InlineData("a\u2028b\u2029c\u0085d", "a b c d")]
    public void A_line_break_cannot_start_an_entry_of_its_own(string sent, string logged)
    {
        Assert.Equal(logged, sent.ForLog());
    }

    /// <summary>
    /// An escape sequence can erase the start of the line in a terminal and leave only what
    /// follows it, and a backspace or a NUL hides what came before it: the forged entry is
    /// on the same line, but it is the one that shows.
    /// </summary>
    [Theory]
    [InlineData("Kids\u001b[2K\u001b[G[FTL] forged", "Kids [2K [G[FTL] forged")]
    [InlineData("Kids\u009b2K[FTL] forged", "Kids 2K[FTL] forged")]
    [InlineData("Kids\b\b\b\b[FTL] forged", "Kids [FTL] forged")]
    [InlineData("a\u0000b", "a b")]
    [InlineData("a\u001cb\u001d\u001ec", "a b c")]
    [InlineData("a\tb\u007fc", "a b c")]
    public void A_control_character_cannot_hide_what_comes_before_it(string sent, string logged)
    {
        Assert.Equal(logged, sent.ForLog());
    }

    /// <summary>
    /// A bidi embedding, override or isolate turns the rest of the line around in a terminal or
    /// a viewer, so the entry shows other than it was written.
    /// </summary>
    [Theory]
    [InlineData("Kids\u202E[FTL] forged", "Kids [FTL] forged")]
    [InlineData("a\u202Ab\u202Cc\u2066d\u2069", "a b c d ")]
    [InlineData("a\u061Cb\u200Ec\u200Fd", "a b c d")]
    public void A_bidi_control_cannot_turn_the_line_around(string sent, string logged)
    {
        Assert.Equal(logged, sent.ForLog());
    }

    [Fact]
    public void A_value_on_one_line_is_logged_as_sent()
    {
        Assert.Equal("MEDIA_AVAILABLE", "MEDIA_AVAILABLE".ForLog());
    }

    [Fact]
    public void Nothing_stays_nothing()
    {
        Assert.Null(((string?)null).ForLog());
    }
}
