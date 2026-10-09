using System;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.Streamyfin.Configuration;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// A name the schema describes has to be a name the config reader accepts.
///
/// The two are written by different libraries: the schema comes from NJsonSchema over
/// the CLR property names, the config is read by YamlDotNet under the camel case
/// convention. Every settings type happens to agree because its properties are already
/// lower case, except <c>LanguagePreference</c>, whose members are PascalCase so that
/// the app can match them against the SDK's <c>CultureDto</c>. The schema therefore
/// described a name its own reader rejected, and an administrator could not save a
/// default audio or subtitle language at all:
///
/// <code>
/// Property 'ThreeLetterISOLanguageName' not found on type '...LanguagePreference'
/// </code>
///
/// Nothing surfaced it while the settings page was written by hand, because that page
/// never offered the two settings. A generated form offers them.
/// </summary>
public class LanguagePreferenceRoundTripTests
{
    private static string[] SchemaPropertyNames(string definition)
    {
        using var document = JsonDocument.Parse(SerializationHelper.GetJsonSchema<Config>());

        return document.RootElement
            .GetProperty("definitions")
            .GetProperty(definition)
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToArray();
    }

    /// <summary>
    /// Reads a config written with the names the schema describes. This is the whole
    /// point: a form fills in what the schema tells it to, so a name the reader does not
    /// know is a setting that cannot be saved.
    /// </summary>
    [Fact]
    public void AConfigWrittenWithTheSchemasNamesIsRead()
    {
        var names = SchemaPropertyNames("LanguagePreference");

        var isoCode = Assert.Single(names.Where(name =>
            name.Contains("ThreeLetter", StringComparison.OrdinalIgnoreCase)));
        var display = Assert.Single(names.Where(name =>
            name.Contains("DisplayName", StringComparison.OrdinalIgnoreCase)));

        var config = new SerializationHelper().Deserialize<Config>(
            $"""
             settings:
               defaultAudioLanguage:
                 locked: false
                 value:
                   {isoCode}: fre
                   {display}: French
             """);

        var language = config.settings?.defaultAudioLanguage?.value;

        Assert.NotNull(language);
        Assert.Equal("fre", language!.ThreeLetterISOLanguageName);
        Assert.Equal("French", language.DisplayName);
    }

    /// <summary>
    /// The app matches on the SDK's <c>CultureDto</c>, whose member is
    /// <c>ThreeLetterISOLanguageName</c>. Whatever the config is written with, what the
    /// app is served keeps that name, so this is the half that must not move.
    /// </summary>
    [Fact]
    public void TheAppIsStillServedTheCultureDtoNames()
    {
        var json = new SerializationHelper().SerializeToJson(new Config
        {
            settings = new Configuration.Settings.Settings
            {
                defaultAudioLanguage = new Configuration.Settings.Lockable<Configuration.Settings.LanguagePreference>
                {
                    value = new Configuration.Settings.LanguagePreference
                    {
                        ThreeLetterISOLanguageName = "fre",
                        DisplayName = "French",
                    },
                },
            },
        });

        Assert.Contains("\"ThreeLetterISOLanguageName\"", json, StringComparison.Ordinal);
        Assert.Contains("\"DisplayName\"", json, StringComparison.Ordinal);
    }
}
