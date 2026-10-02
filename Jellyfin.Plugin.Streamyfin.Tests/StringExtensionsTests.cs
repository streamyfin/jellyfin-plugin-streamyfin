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
