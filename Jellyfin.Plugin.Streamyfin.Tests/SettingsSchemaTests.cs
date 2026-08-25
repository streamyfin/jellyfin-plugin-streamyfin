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
    /// The Seerr admin key is the only credential in the settings today. This pins the set:
    /// adding another one has to be a decision rather than an accident, because P1.4 filters
    /// exactly what is listed here out of the response served to a non administrator.
    /// </summary>
    [Fact]
    public void TheSeerrAdminKeyIsTheOnlySecret()
    {
        Assert.Equal(
            new[] { "jellyseerrApiKey" },
            SettingsSchema.Secrets.Select(s => s.Key).ToArray());
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
}
