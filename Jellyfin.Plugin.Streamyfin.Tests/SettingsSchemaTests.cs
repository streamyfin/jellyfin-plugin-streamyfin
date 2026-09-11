using System.Linq;
using System.Reflection;
using System.Text.Json;
using Jellyfin.Plugin.Streamyfin.Configuration;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Xunit;
using Settings = Jellyfin.Plugin.Streamyfin.Configuration.Settings.Settings;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// <c>SettingsSchema</c> reads the settings class so that nothing else has to repeat a
/// property list. These tests are what keep the two in step: a setting added to the class
/// and forgotten here, or a credential added without <c>[Secret]</c>, fails the build
/// rather than shipping.
/// </summary>
public class SettingsSchemaTests
{
    /// <summary>
    /// Every public property of the class is described. A setting that escapes the schema
    /// is a setting the secret filter and the resolution engine cannot see.
    /// </summary>
    [Fact]
    public void EverySettingIsDescribed()
    {
        var declared = typeof(Settings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, System.StringComparer.Ordinal)
            .ToArray();

        var described = SettingsSchema.Descriptors
            .Select(d => d.Property.Name)
            .OrderBy(n => n, System.StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(declared, described);
    }

    /// <summary>
    /// The credentials in the settings, all of them. This pins the set: adding another one
    /// has to be a decision rather than an accident, because P1.4 filters exactly what is
    /// listed here out of the response served to a non administrator. A key that reaches
    /// this list late has been served to every account in the meantime.
    /// </summary>
    [Fact]
    public void TheCredentialsAreTheOnesListedHere()
    {
        Assert.Equal(
            new[] { "jellyseerrApiKey", "openSubtitlesApiKey" },
            SettingsSchema.Secrets.Select(s => s.Key).OrderBy(k => k, System.StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    /// The server URL sitting next to the key is not a credential. Marking it would hide a
    /// setting an admin has to be able to read back.
    /// </summary>
    [Fact]
    public void TheServerUrlIsNotSecret()
    {
        Assert.False(SettingsSchema.IsSecret("jellyseerrServerUrl"));
    }

    /// <summary>
    /// A key nothing declares is not a setting, rather than a setting with no marker.
    /// </summary>
    [Fact]
    public void AnUnknownKeyHasNoDescriptor()
    {
        Assert.Null(SettingsSchema.Find("thereIsNoSuchSetting"));
        Assert.False(SettingsSchema.IsSecret("thereIsNoSuchSetting"));
    }

    /// <summary>
    /// The value type is the one inside <c>Lockable</c>, not <c>Lockable</c> itself. Everything
    /// that has to reason about what a setting holds reads this.
    /// </summary>
    [Theory]
    [InlineData("subtitleSize", typeof(int))]
    [InlineData("jellyseerrApiKey", typeof(string))]
    [InlineData("hiddenLibraries", typeof(string[]))]
    [InlineData("defaultVideoOrientation", typeof(OrientationLock))]
    public void TheValueTypeIsUnwrapped(string key, System.Type expected)
    {
        var descriptor = SettingsSchema.Find(key);

        Assert.NotNull(descriptor);
        Assert.True(descriptor!.IsLockable);
        Assert.Equal(expected, descriptor.ValueType);
    }

    /// <summary>
    /// The label and the help text come from the property, so a form does not need a second
    /// copy of them that then drifts.
    /// </summary>
    [Fact]
    public void TheDescriptorCarriesTheLabel()
    {
        var descriptor = SettingsSchema.Find("jellyseerrApiKey");

        Assert.NotNull(descriptor);
        Assert.Equal("Jellyseerr API Key", descriptor!.DisplayName);
        Assert.False(string.IsNullOrWhiteSpace(descriptor.Description));
    }

    /// <summary>
    /// The generated schema says which fields are credentials, so a form can render a
    /// password input. Without it a client has no way to tell the key from the URL next to it.
    /// </summary>
    [Fact]
    public void TheGeneratedSchemaMarksTheSecret()
    {
        using var document = JsonDocument.Parse(SerializationHelper.GetJsonSchema<Config>());
        var settings = document.RootElement
            .GetProperty("definitions")
            .GetProperty("Settings")
            .GetProperty("properties");

        Assert.True(settings.GetProperty("jellyseerrApiKey").GetProperty("x-secret").GetBoolean());
        Assert.False(settings.GetProperty("jellyseerrServerUrl").TryGetProperty("x-secret", out _));
    }

    /// <summary>
    /// The marker goes on the property and not on the shared definition the property points
    /// at. <c>LockableOfString</c> is reached by the Seerr key and by three plain URLs, so
    /// marking it there would turn every URL in the plugin into a secret.
    /// </summary>
    [Fact]
    public void TheSharedLockableDefinitionIsNotMarked()
    {
        using var document = JsonDocument.Parse(SerializationHelper.GetJsonSchema<Config>());

        var lockableOfString = document.RootElement
            .GetProperty("definitions")
            .GetProperty("LockableOfString");

        Assert.False(lockableOfString.TryGetProperty("x-secret", out _));
    }

    /// <summary>
    /// Every setting names the section of the form it belongs to. The form draws one card
    /// per section rather than ninety two settings in a single column, so a setting
    /// without one would have nowhere to go. Failing here is the point: adding a setting
    /// is also deciding where an administrator will look for it.
    /// </summary>
    [Fact]
    public void EverySettingNamesItsSection()
    {
        var uncategorised = SettingsSchema.Descriptors
            .Where(descriptor => string.IsNullOrWhiteSpace(descriptor.Category))
            .Select(descriptor => descriptor.Key)
            .ToArray();

        Assert.True(
            uncategorised.Length == 0,
            $"no category, so the form has nowhere to put them: {string.Join(", ", uncategorised)}");
    }

    /// <summary>
    /// The section reaches the served schema too. The admin pages read it from
    /// <c>v1/settings/form</c>, but the schema is the public description of the
    /// configuration and anything else editing it deserves the same grouping.
    /// </summary>
    [Fact]
    public void TheSchemaCarriesTheSection()
    {
        using var document = JsonDocument.Parse(SerializationHelper.GetJsonSchema<Config>());
        var settings = document.RootElement
            .GetProperty("definitions")
            .GetProperty("Settings")
            .GetProperty("properties");

        foreach (var descriptor in SettingsSchema.Descriptors)
        {
            var property = settings.GetProperty(descriptor.Key);

            Assert.True(
                property.TryGetProperty("x-category", out var category),
                $"{descriptor.Key} reaches the schema without a category");
            Assert.Equal(descriptor.Category, category.GetString());
        }
    }

    /// <summary>
    /// No playback cap is stored as null, which is the app's "Max".
    /// </summary>
    /// <remarks>
    /// The form offers it as a choice whose value is null, and the store has to read that
    /// back as no cap rather than as a missing setting or a zero.
    /// </remarks>
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
