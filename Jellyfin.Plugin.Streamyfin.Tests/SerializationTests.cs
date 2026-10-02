using System;
using System.Collections.Generic;
using Jellyfin.Data.Enums;
using Jellyfin.Plugin.Streamyfin.Configuration;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Jellyfin.Plugin.Streamyfin.PushNotifications.models;
using MediaBrowser.Controller.Entities.Movies;
using Xunit;
using Xunit.Abstractions;
using Assert = ICU4N.Impl.Assert;
using Settings = Jellyfin.Plugin.Streamyfin.Configuration.Settings.Settings;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// Ensure special types are properly serialized/deserialized when converting between Object - Json - Yaml
/// </summary>
public class SerializationTests(ITestOutputHelper output)
{
    private readonly SerializationHelper _serializationHelper = new();

    /// <summary>
    /// Ensure Json Schema forces enum names as values imported from external namespaces
    /// </summary>
    [Fact]
    public void EnumJsonSchemaTest()
    {
        var schema = SerializationHelper.GetJsonSchema<Config>();
        output.WriteLine(schema);

        Assert.Assrt(
            msg: "SubtitlePlaybackMode enumNames are string values",
            val: schema.Contains(
                """
                    "SubtitlePlaybackMode": {
                      "type": "string",
                      "description": "",
                      "x-enumNames": [
                        "Default",
                        "Always",
                        "OnlyForced",
                        "None",
                        "Smart"
                      ],
                      "enum": [
                        "Default",
                        "Always",
                        "OnlyForced",
                        "None",
                        "Smart"
                      ]
                    }
                """
                , StringComparison.Ordinal)
            );
    }

    /// <summary>
    /// Ensures all types of enums are deserialized correctly
    /// </summary>
    [Fact]
    public void EnumConfigJsonDeserializationTest()
    {
        DeserializeConfig(
            """
            {
                "settings": {
                    "subtitleMode": {
                        "locked": true,
                        "value": "Default"
                    },
                    "defaultVideoOrientation": {
                        "locked": true,
                        "value": "LandscapeLeft"
                    },
                    "defaultBitrate": {
                        "locked": true,
                        "value": 250000
                    }
                }
            }
            """
        );
    }

    /// <summary>
    /// Ensures all types of enums are deserialized correctly
    /// </summary>
    [Fact]
    public void EnumConfigYamlDeserializationTest()
    {
        DeserializeConfig(
            """
            settings:
                subtitleMode:
                    locked: true
                    value: Default
                defaultVideoOrientation:
                    locked: true
                    value: LandscapeLeft
                defaultBitrate:
                    locked: true
                    value: _250KB
            """
        );
    }

    /// <summary>
    /// Ensures all types of enums are json serialized correctly
    /// </summary>
    [Fact]
    public void ConfigJsonSerializationTest()
    {
        SerializeConfig(
            value: _serializationHelper.SerializeToJson(GetTestConfig()),
            expected:
            """
            {
              "settings": {
                "subtitleMode": {
                  "locked": false,
                  "value": 0
                },
                "defaultVideoOrientation": {
                  "locked": false,
                  "value": 6
                },
                "defaultBitrate": {
                  "locked": false,
                  "value": 250000
                }
              }
            }
            """
        );
    }
    
    /// <summary>
    /// What the app receives carries the subtitle mode by name, the other enums as before.
    /// </summary>
    /// <remarks>
    /// The app compares the SDK's strings for the subtitle mode, so the number never
    /// matched and a mode an administrator locked did nothing.
    /// </remarks>
    [Fact]
    public void ConfigJsonForTheAppTest()
    {
        SerializeConfig(
            value: _serializationHelper.SerializeForApp(GetTestConfig()),
            expected:
            """
            {
              "settings": {
                "subtitleMode": {
                  "locked": false,
                  "value": "Default"
                },
                "defaultVideoOrientation": {
                  "locked": false,
                  "value": 6
                },
                "defaultBitrate": {
                  "locked": false,
                  "value": 250000
                }
              }
            }
            """
        );
    }

    /// <summary>
    /// The subtitle mode is still stored as the number, which every earlier build reads.
    /// </summary>
    /// <remarks>
    /// An earlier build reads the stored subtitle mode with a number converter only, and
    /// throws on a name. Stored by name, a rollback would serve an empty configuration to
    /// everyone and refuse every backup taken since. The app is the only one that gets the
    /// name.
    /// </remarks>
    [Fact]
    public void TheSubtitleModeIsStoredAsTheNumberEarlierBuildsRead()
    {
        var stored = _serializationHelper.SerializeToJson(new Settings
        {
            subtitleMode = new Lockable<SubtitlePlaybackMode> { locked = true, value = SubtitlePlaybackMode.Smart }
        });

        using var document = System.Text.Json.JsonDocument.Parse(stored);
        var value = document.RootElement.GetProperty("subtitleMode").GetProperty("value");

        Assert.Assrt(
            $"SubtitlePlaybackMode stored as {value.GetRawText()}",
            value.ValueKind == System.Text.Json.JsonValueKind.Number && value.GetInt32() == (int)SubtitlePlaybackMode.Smart);
    }

    /// <summary>
    /// Ensures all types of enums are yaml serialized correctly
    /// </summary>
    [Fact]
    public void ConfigYamlSerializationTest()
    {
        SerializeConfig(
            value: _serializationHelper.SerializeToYaml(GetTestConfig()),
            expected:
            """
            settings:
              subtitleMode:
                locked: false
                value: Default
              defaultVideoOrientation:
                locked: false
                value: LandscapeLeft
              defaultBitrate:
                locked: false
                value: _250KB
            """
        );
    }
    
    /// <summary>
    /// Ensures array of notifications are deserialized correctly
    /// </summary>
    [Fact]
    public void DeserializeNotification()
    {
        var notification = _serializationHelper.Deserialize<List<Notification>>(
            """
            [
                {
                    "title": "Test Title",
                    "body": "Test Body",
                    "userId": "2c585c0706ac46779a2c38ca896b556f"
                }
            ]
            """
        )[0];
        
        Assert.Assrt(
            msg: "title deserialized",
            notification.Title == "Test Title"
        );
        
        Assert.Assrt(
            msg: "body deserialized",
            notification.Body == "Test Body"
        );

        Assert.Assrt(
            msg: "guid deserialized",
            notification.UserId?.ToString("N") == "2c585c0706ac46779a2c38ca896b556f"
        );
    }

    private static Config GetTestConfig()
    {
        return new Config
        {
            settings = new Settings
            {
                subtitleMode = new Lockable<SubtitlePlaybackMode>
                {
                    value = SubtitlePlaybackMode.Default
                },
                defaultVideoOrientation = new Lockable<OrientationLock>
                {
                    value = OrientationLock.LandscapeLeft
                },
                defaultBitrate = new Lockable<Bitrate?>
                {
                    value = Bitrate._250KB
                }
            }
        };
    }

    private void SerializeConfig(string value, string expected)
    {
        output.WriteLine($"Serialized:\n {value}");
        output.WriteLine($"Expected:\n {expected}");
        
        // Try to detect if this is JSON by checking if it starts with { or [
        var trimmedValue = value.Trim();
        var trimmedExpected = expected.Trim();
        
        if (trimmedValue.StartsWith('{') || trimmedValue.StartsWith('['))
        {
            // JSON format - normalize by parsing and re-serializing both
            var valueObj = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(value);
            var expectedObj = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(expected);
            var normalizedValue = System.Text.Json.JsonSerializer.Serialize(valueObj);
            var normalizedExpected = System.Text.Json.JsonSerializer.Serialize(expectedObj);
            
            Assert.Assrt("Config serialized matches expected", normalizedValue == normalizedExpected);
        }
        else
        {
            // YAML format - do simple string comparison after trimming
            Assert.Assrt("Config serialized matches expected", trimmedValue == trimmedExpected);
        }
    }

    private void DeserializeConfig(string value)
    {
        output.WriteLine($"Deserializing config from:\n {value}");
        Config config = _serializationHelper.Deserialize<Config>(value);

        Assert.Assrt(
            $"SubtitlePlaybackMode matches: {SubtitlePlaybackMode.Default} == {config.settings?.subtitleMode?.value}",
            SubtitlePlaybackMode.Default == config.settings?.subtitleMode?.value
        );
        Assert.Assrt(
            $"OrientationLock matches: {OrientationLock.LandscapeLeft} == {config.settings?.defaultVideoOrientation?.value}",
            OrientationLock.LandscapeLeft == config.settings?.defaultVideoOrientation?.value
        );
        Assert.Assrt(
            $"Bitrate matches: {Bitrate._250KB} == {config.settings?.defaultBitrate?.value}",
            Bitrate._250KB == config.settings?.defaultBitrate?.value
        );
    }
}