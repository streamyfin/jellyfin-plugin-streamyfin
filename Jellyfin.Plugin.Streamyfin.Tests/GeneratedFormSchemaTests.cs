using System;
using System.Linq;
using System.Text.Json;
using Jellyfin.Plugin.Streamyfin.Configuration;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The generated admin form (P3.1) is json-editor reading the schema the plugin
/// serves. These tests pin the shape that form needs, because the form itself is JS
/// that only a browser exercises. What json-editor makes of the schema was checked
/// against the real library before each of these was written; the reason each shape
/// matters is the rendering it produces.
/// </summary>
public class GeneratedFormSchemaTests
{
    /// <summary>
    /// Every setting names the section of the form it belongs to. The form renders one
    /// collapsible section per category rather than ninety two settings in a single
    /// column, so a setting without one would have nowhere to go. Failing here is the
    /// point: adding a setting is also deciding where an administrator will look for it.
    /// </summary>
    [Fact]
    public void EverySettingNamesItsSection()
    {
        var uncategorised = Configuration.Settings.SettingsSchema.Descriptors
            .Where(descriptor => string.IsNullOrWhiteSpace(descriptor.Category))
            .Select(descriptor => descriptor.Key)
            .ToArray();

        Assert.True(
            uncategorised.Length == 0,
            $"no category, so the form has nowhere to put them: {string.Join(", ", uncategorised)}");
    }

    /// <summary>
    /// The section reaches the served schema, which is where the page reads it from. The
    /// page groups on this rather than keeping a list of its own that would drift.
    /// </summary>
    [Fact]
    public void TheSchemaCarriesTheSection()
    {
        using var document = JsonDocument.Parse(SerializationHelper.GetJsonSchema<Config>());
        var settings = SettingsProperties(document);

        foreach (var descriptor in Configuration.Settings.SettingsSchema.Descriptors)
        {
            var property = settings.GetProperty(descriptor.Key);

            Assert.True(
                property.TryGetProperty("x-category", out var category),
                $"{descriptor.Key} reaches the form without a category");
            Assert.Equal(descriptor.Category, category.GetString());
        }
    }

    private static JsonElement SettingsProperties(JsonDocument document) =>
        document.RootElement
            .GetProperty("definitions")
            .GetProperty("Settings")
            .GetProperty("properties");

    /// <summary>
    /// NJsonSchema attaches a property's title and secret marker by wrapping its
    /// reference in a single-branch <c>oneOf</c>. json-editor reads a <c>oneOf</c> as a
    /// choice between schemas and renders a type selector next to every setting, a
    /// dropdown with one option that edits nothing. The wrapper is flattened so the
    /// setting renders as itself.
    /// </summary>
    [Fact]
    public void NoSettingIsWrappedInAOneOf()
    {
        using var document = JsonDocument.Parse(SerializationHelper.GetJsonSchema<Config>());

        foreach (var property in SettingsProperties(document).EnumerateObject())
        {
            Assert.False(
                property.Value.TryGetProperty("oneOf", out _),
                $"{property.Name} is still wrapped in a oneOf, which json-editor renders as a redundant type selector");
        }
    }

    /// <summary>
    /// json-editor shows the description carried by the referenced definition, not the one
    /// on the property pointing at it. Every <c>Lockable&lt;T&gt;</c> definition carries the
    /// same generic "Assign a lock to given type value", which would shadow the help text
    /// written on each setting. Blanked, so a setting's own description reaches the form.
    /// </summary>
    [Fact]
    public void TheSharedLockableDefinitionsCarryNoDescription()
    {
        using var document = JsonDocument.Parse(SerializationHelper.GetJsonSchema<Config>());

        foreach (var definition in document.RootElement.GetProperty("definitions").EnumerateObject())
        {
            if (!definition.Name.StartsWith("LockableOf", StringComparison.Ordinal))
            {
                continue;
            }

            if (definition.Value.TryGetProperty("description", out var description))
            {
                Assert.True(
                    string.IsNullOrEmpty(description.GetString()),
                    $"{definition.Name} still carries a description that would shadow each setting's own");
            }
        }
    }

    /// <summary>
    /// A credential renders as a password field, masked. The <c>x-secret</c> marker sits on
    /// the property, but the input is the <c>value</c> inside the Lockable the property points
    /// at, and that Lockable is shared with plain URLs that must stay readable. So a secret
    /// carries its own inlined value marked <c>format: password</c>. The two are the ones
    /// <see cref="Configuration.Settings.SettingsSchema.Secrets"/> lists.
    /// </summary>
    [Theory]
    [InlineData("jellyseerrApiKey")]
    [InlineData("openSubtitlesApiKey")]
    public void ASecretRendersItsValueAsAPasswordField(string key)
    {
        using var document = JsonDocument.Parse(SerializationHelper.GetJsonSchema<Config>());

        var setting = SettingsProperties(document).GetProperty(key);

        Assert.True(
            setting.TryGetProperty("properties", out var properties),
            $"{key} should inline its value so a password field can be marked without touching the shared definition");

        var value = properties.GetProperty("value");

        Assert.Equal("password", value.GetProperty("format").GetString());

        // The type has to be the single string "string", never a list. json-editor drops
        // the format the moment a type is a list, so ["null","string"] renders the
        // credential in a plain text box, in clear. Found on a real dashboard, where the
        // field came out as type=text; the format alone is not enough to assert.
        Assert.Equal(JsonValueKind.String, value.GetProperty("type").ValueKind);
        Assert.Equal("string", value.GetProperty("type").GetString());
    }

    /// <summary>
    /// The playback quality is a nullable bitrate: null is no cap, the app's "Max". NJsonSchema
    /// renders the nullable enum as a null-or-reference <c>oneOf</c>, which json-editor draws as
    /// a type selector, and the enum names carry a leading underscore. Collapsed to one nullable
    /// enum with friendly titles it renders as a single dropdown, Max then 250KB through 8MB.
    /// This is the option list the page's <c>setOptions</c> assembled by hand, moved onto the
    /// schema so the page holds none of it.
    /// </summary>
    [Fact]
    public void ThePlaybackQualityOffersMaxAndCleanLabels()
    {
        using var document = JsonDocument.Parse(SerializationHelper.GetJsonSchema<Config>());

        var value = document.RootElement
            .GetProperty("definitions")
            .GetProperty("LockableOfNullableBitrate")
            .GetProperty("properties")
            .GetProperty("value");

        Assert.False(
            value.TryGetProperty("oneOf", out _),
            "the null-or-reference oneOf should be collapsed so json-editor draws one dropdown, not a type selector");

        var values = value.GetProperty("enum")
            .EnumerateArray().Select(entry => entry.GetString()).ToArray();
        var titles = value.GetProperty("options").GetProperty("enum_titles")
            .EnumerateArray().Select(title => title.GetString()).ToArray();

        // "Max" is the empty string, not null: json-editor labels a null entry "null" whatever
        // its title, but honours an empty string's title. The blank is coerced back to null on
        // save, the way the hand written page always turned a blank field into null.
        Assert.Equal(new[] { "", "_250KB", "_500KB", "_1MB", "_2MB", "_4MB", "_8MB" }, values);
        Assert.Equal(new[] { "Max", "250KB", "500KB", "1MB", "2MB", "4MB", "8MB" }, titles);
    }

    /// <summary>
    /// No cap is stored as null. The form offers "Max" as an empty string, which the page
    /// coerces back to null before saving; a stored null is what the deserializer reads as no
    /// cap. This pins that target: an empty string is not itself a valid bitrate, so a form
    /// that failed to coerce it would be caught by the config failing to load.
    /// </summary>
    [Fact]
    public void NoPlaybackCapIsStoredAsNull()
    {
        var config = new SerializationHelper().Deserialize<Config>(
            """
            settings:
              defaultBitrate:
                locked: false
                value: null
            """);

        Assert.NotNull(config.settings);
        Assert.Null(config.settings!.defaultBitrate!.value);
    }
}
