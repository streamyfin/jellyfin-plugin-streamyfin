using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Xunit;
using Settings = Jellyfin.Plugin.Streamyfin.Configuration.Settings.Settings;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// What the server refuses to store.
/// </summary>
/// <remarks>
/// Until this existed the admin form was the only thing that knew a skip time stops at
/// sixty: the Yaml tab wrote whatever was typed, and the targeting routes took a JSON
/// body from anything holding an API key. The bounds come from the same declaration the
/// form draws from, so a setting is bounded once.
/// </remarks>
public class SettingsValidationTests
{
    /// <summary>
    /// Settings within their bounds are stored.
    /// </summary>
    [Fact]
    public void SettingsWithinTheirBoundsAreAccepted()
    {
        var settings = new Settings
        {
            forwardSkipTime = new Lockable<int> { value = 60, locked = false },
            rewindSkipTime = new Lockable<int> { value = 0, locked = false },
            subtitleSize = new Lockable<int> { value = 120, locked = true },
        };

        Assert.Empty(SettingsValidation.Problems(settings));
        Assert.Null(SettingsValidation.Message(settings));
    }

    /// <summary>
    /// A value past its bounds is refused, and the message names the setting the way an
    /// administrator reads it rather than by its key.
    /// </summary>
    [Theory]
    [InlineData(600)]
    [InlineData(-5)]
    public void AValueOutsideItsBoundsIsRefused(int value)
    {
        var settings = new Settings { forwardSkipTime = new Lockable<int> { value = value, locked = false } };

        var problem = Assert.Single(SettingsValidation.Problems(settings));

        Assert.Contains("Forward skip time", problem, System.StringComparison.Ordinal);
        Assert.Contains("0 to 60", problem, System.StringComparison.Ordinal);
        Assert.Contains(value.ToString(System.Globalization.CultureInfo.InvariantCulture), problem, System.StringComparison.Ordinal);
    }

    /// <summary>
    /// Every setting out of bounds is named, not just the first, so one save says
    /// everything that has to change.
    /// </summary>
    [Fact]
    public void EverySettingOutOfBoundsIsNamed()
    {
        var settings = new Settings
        {
            forwardSkipTime = new Lockable<int> { value = 600, locked = false },
            subtitleSize = new Lockable<int> { value = 900, locked = false },
        };

        Assert.Equal(2, SettingsValidation.Problems(settings).Count);
    }

    /// <summary>
    /// A setting a level does not carry is not its business: it falls through to the
    /// level below, and a partial level is the normal shape of a group.
    /// </summary>
    [Fact]
    public void ASettingALevelDoesNotCarryIsNotChecked()
    {
        Assert.Empty(SettingsValidation.Problems(new Settings()));
        Assert.Empty(SettingsValidation.Problems(null));
    }

    /// <summary>
    /// A setting with no declared bounds accepts anything, which is what having no
    /// bounds means.
    /// </summary>
    [Fact]
    public void ASettingWithNoBoundsAcceptsAnything()
    {
        var settings = new Settings { maxAutoPlayEpisodeCount = new Lockable<int> { value = 9999, locked = false } };

        Assert.Empty(SettingsValidation.Problems(settings));
    }

    /// <summary>
    /// A null is not out of bounds. It is the app's own answer for a nullable setting,
    /// the playback quality's "no cap", and refusing it here would refuse Max.
    /// </summary>
    [Fact]
    public void ANullIsNotOutOfBounds()
    {
        var settings = new Settings { defaultBitrate = new Lockable<Configuration.Bitrate?> { value = null, locked = false } };

        Assert.Empty(SettingsValidation.Problems(settings));
    }

    /// <summary>
    /// Every bounded setting is a number, since bounds mean nothing on anything else.
    /// </summary>
    [Fact]
    public void OnlyANumberCarriesBounds()
    {
        var wrong = SettingsSchema.Descriptors
            .Where(d => d.Property.GetCustomAttribute<BoundsAttribute>() is not null)
            .Where(d => SettingsForm.Describe().Single(f => f.Key == d.Key).Control != SettingsControl.Number)
            .Select(d => d.Key)
            .ToArray();

        Assert.Empty(wrong);
    }

    /// <summary>
    /// The bounds the server enforces are the bounds the form draws, because they are
    /// the same declaration. A second list would drift the first time one moved.
    /// </summary>
    [Fact]
    public void TheServerAndTheFormShareTheirBounds()
    {
        foreach (var field in SettingsForm.Describe().Where(f => f.Minimum is not null))
        {
            var bounds = SettingsSchema.Descriptors
                .Single(d => d.Key == field.Key)
                .Property.GetCustomAttribute<BoundsAttribute>();

            Assert.NotNull(bounds);
            Assert.Equal(field.Minimum, bounds!.Minimum);
            Assert.Equal(field.Maximum, bounds.Maximum);
        }
    }
}
