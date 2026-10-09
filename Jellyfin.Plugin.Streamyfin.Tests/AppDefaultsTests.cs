using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// What the form puts a setting back to when the plugin declares no default for it.
/// </summary>
/// <remarks>
/// Reset was offered on 53 settings of 86, the ones the plugin declares a default for. The
/// others have a default too, the app's own, which the manifest generated from the app's
/// source already holds.
/// </remarks>
public class AppDefaultsTests
{
    private static SettingsFormField Field(string key) => SettingsForm.Describe().Single(f => f.Key == key);

    /// <summary>
    /// A setting the plugin declares nothing for still has the app's own value.
    /// </summary>
    /// <param name="key">The setting.</param>
    /// <param name="expected">The app's default, as JSON.</param>
    [Theory]
    [InlineData("enableHorizontalSwipeSkip", "true")]
    [InlineData("skipIntro", "\"ask\"")]
    [InlineData("maxAutoPlayEpisodeCount", "3")]
    [InlineData("defaultPlaybackSpeed", "1")]
    public void ASettingThePluginDeclaresNothingForHasTheApps(string key, string expected)
    {
        Assert.Equal(expected, Field(key).AppDefault?.GetRawText());
    }

    /// <summary>
    /// A value the app reshapes on the way in is given in the plugin's form: the subtitle
    /// size is a percentage here and a scale in the app.
    /// </summary>
    [Fact]
    public void AReshapedValueIsGivenInThePluginsForm()
    {
        Assert.Equal("100", Field("subtitleSize").AppDefault?.GetRawText());
    }

    /// <summary>
    /// A choice the app keeps as the number of its own enum is given by the name the form
    /// offers: the inactivity timeout is 0 in the app and Disabled in the menu.
    /// </summary>
    [Fact]
    public void AChoiceTheAppNumbersIsGivenByTheNameTheMenuOffers()
    {
        Assert.Equal("\"Disabled\"", Field("inactivityTimeout").AppDefault?.GetRawText());
    }

    /// <summary>
    /// The mpv buffers default to one number on a phone and another on Android TV, so there
    /// is none to put back, and the languages default to nothing chosen.
    /// </summary>
    [Fact]
    public void ADefaultTheAppPicksPerPlatformOrNotAtAllIsNotOne()
    {
        Assert.Null(Field("mpvDemuxerMaxBytes").AppDefault);
        Assert.Null(Field("defaultAudioLanguage").AppDefault);
    }

    /// <summary>
    /// Every app default is a value the control that shows it can hold: a box, a number
    /// within its bounds, one of the choices offered. One the form cannot show would put back
    /// something nobody can see, and the manifest is regenerated from the app every week.
    /// </summary>
    [Fact]
    public void EveryAppDefaultFitsItsControl()
    {
        var misfits = SettingsForm.Describe()
            .Where(field => field.AppDefault is { } value && !Fits(field, value))
            .Select(field => $"{field.Key} ({field.Control}): {field.AppDefault?.GetRawText()}")
            .ToList();

        Assert.True(misfits.Count == 0, "App defaults the form cannot show:\n  " + string.Join("\n  ", misfits));
    }

    private static bool Fits(SettingsFormField field, JsonElement value) => field.Control switch
    {
        SettingsControl.Toggle => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        SettingsControl.Number => value.ValueKind == JsonValueKind.Number
            && (field.Minimum is null || value.GetDouble() >= field.Minimum)
            && (field.Maximum is null || value.GetDouble() <= field.Maximum),
        SettingsControl.Select => field.Options.Any(option => option.Value == (value.ValueKind == JsonValueKind.Null ? null : value.ToString())),
        SettingsControl.List => value.ValueKind == JsonValueKind.Array
            && (field.Options.Count == 0 || value.EnumerateArray().All(one => field.Options.Any(option => option.Value == one.ToString()))),
        SettingsControl.Text or SettingsControl.Secret => value.ValueKind is JsonValueKind.String or JsonValueKind.Null,
        SettingsControl.Fields => value.ValueKind == JsonValueKind.Object,
        _ => true,
    };
}
