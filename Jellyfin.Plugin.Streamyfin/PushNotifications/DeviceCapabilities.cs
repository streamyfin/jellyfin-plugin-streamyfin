using System.Text.Json;
using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace Jellyfin.Plugin.Streamyfin.PushNotifications;

/// <summary>
/// What a device says it can show, so a message only carries what it will display.
/// </summary>
/// <remarks>
/// A message sent to an Android channel the device never created is not shown at all (Expo's
/// documentation), so a channel only goes to a device that created ours. Each is a version
/// rather than a flag, so a later set of channels can be told apart from this one.
/// </remarks>
public sealed class DeviceCapabilities
{
    /// <summary>Gets or sets the version of our Android channels the device created, 0 for none.</summary>
    [JsonPropertyName("channels")]
    [JsonProperty("channels")]
    public int Channels { get; set; }

    /// <summary>Gets or sets the version of our notification buttons the device declared, 0 for none.</summary>
    [JsonPropertyName("categories")]
    [JsonProperty("categories")]
    public int Categories { get; set; }

    /// <summary>Gets a value indicating whether the device created our channels.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public bool HasChannels => Channels >= 1;

    /// <summary>Gets a value indicating whether the device declared our buttons.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    [Newtonsoft.Json.JsonIgnore]
    public bool HasCategories => Categories >= 1;

    /// <summary>Reads what is stored.</summary>
    /// <param name="stored">The stored JSON.</param>
    /// <returns>The capabilities, or <c>null</c> when the device said nothing readable.</returns>
    public static DeviceCapabilities? Read(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<DeviceCapabilities>(stored);
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Writes the capabilities for storage.</summary>
    /// <param name="capabilities">What the device said, which may be nothing.</param>
    /// <returns>The JSON, or <c>null</c> for nothing.</returns>
    public static string? Write(DeviceCapabilities? capabilities) =>
        capabilities is null ? null : System.Text.Json.JsonSerializer.Serialize(capabilities);
}
