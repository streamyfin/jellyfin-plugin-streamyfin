#pragma warning disable CA1869

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions.Json;
using Jellyfin.Plugin.Streamyfin.Configuration;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Newtonsoft.Json;
using NJsonSchema;
using NJsonSchema.Generation;
using NJsonSchema.Generation.TypeMappers;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using JsonSchemaGenerator = NJsonSchema.Generation.JsonSchemaGenerator;
using JsonSerializer = System.Text.Json.JsonSerializer;
using NewtonsoftJsonSerializer = Newtonsoft.Json.JsonSerializer;


namespace Jellyfin.Plugin.Streamyfin;

/// <summary>
/// Serialization settings for json and yaml
/// </summary>
public class SerializationHelper
{
    private readonly IDeserializer _deserializer;
    private readonly ISerializer _yamlSerializer;
    private readonly NewtonsoftJsonSerializer _jsonSerializer;

    public SerializationHelper()
    {
        _yamlSerializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            // We cannot use OmitDefaults since SubtitlePlaybackMode.Default gets removed. Create comb. of flags
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull | DefaultValuesHandling.OmitEmptyCollections)
            .Build();
        
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        _jsonSerializer = NewtonsoftJsonSerializer.CreateDefault();
    }

    /// <summary>
    /// The options every JSON the app receives is written with.
    /// </summary>
    /// <remarks>
    /// Public because the parity test compares a declared default against what the app
    /// reads, and it has to compare the written form. Comparing CLR values would pass
    /// for an enum written as a number where the app expects its name.
    /// </remarks>
    public JsonSerializerOptions GetJsonSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonDefaults.Options);
        // Prioritize these first since other converters & defaults change expected behavior
        options.Converters.Insert(0, new JsonNumberEnumConverter<SubtitlePlaybackMode>());
        options.Converters.Insert(0, new JsonNumberEnumConverter<OrientationLock>());
        options.Converters.Insert(0, new JsonNumberEnumConverter<Bitrate>());
        options.Converters.Insert(0, new JsonNumberEnumConverter<VideoPlayer>());
        options.Converters.Insert(0, new JsonNumberEnumConverter<InactivityTimeout>());

#if DEBUG
        options.WriteIndented = true;
#endif
        return options;
    }

    /// <summary>
    /// Generate schema to json
    /// </summary>
    public static string GetJsonSchema<T>()
    {
        var settings = new SystemTextJsonSchemaGeneratorSettings
        {
            TypeMappers = HTMLFormTypeMappers()
        };
#if DEBUG
        settings.SerializerOptions.WriteIndented = true;
#endif
        settings.SerializerOptions.Converters.Add(new JsonStringEnumConverter());

        var schema = JsonSchemaGenerator.FromType<T>(settings);
        MarkSecrets(schema);
        return schema.ToJson();
    }

    /// <summary>
    /// Flags the settings that hold a credential, as <c>x-secret</c>.
    /// </summary>
    /// <remarks>
    /// A generated form has no other way to know that a field is a password rather
    /// than a string. It goes on the property rather than on the shared
    /// <c>LockableOfString</c> definition, which several plain URLs also point at.
    /// The set comes from <see cref="SettingsSchema"/>, so marking a new key is one
    /// attribute and nothing here changes.
    /// </remarks>
    private static void MarkSecrets(JsonSchema schema)
    {
        foreach (var candidate in SchemasCarryingSettings(schema))
        {
            foreach (var secret in SettingsSchema.Secrets)
            {
                if (!candidate.Properties.TryGetValue(secret.Key, out var property))
                {
                    continue;
                }

                property.ExtensionData ??= new Dictionary<string, object?>();
                property.ExtensionData["x-secret"] = true;
            }
        }
    }

    private static IEnumerable<JsonSchema> SchemasCarryingSettings(JsonSchema schema)
    {
        // The root when the schema was generated from Settings itself, and the
        // definition when it was generated from Config, which is the live case.
        yield return schema;

        if (schema.Definitions.TryGetValue(typeof(Configuration.Settings.Settings).Name, out var settings))
        {
            yield return settings;
        }
    }

    /// <summary>
    /// Serialize to Yaml with Streamyfin expected options
    /// </summary>
    public string SerializeToYaml<T>(T item) => _yamlSerializer.Serialize(item);
    
    /// <summary>
    /// Serialize to Json with Streamyfin expected using copied options
    /// </summary>
    public string SerializeToJson<T>(T item) => 
        JsonSerializer.Serialize(item, GetJsonSerializerOptions());

    /// <summary>
    /// Serialize to Json with Streamyfin expected using copied options
    /// </summary>
    public string ToJson<T>(T item)
    {
        var output = new StringWriter();
        _jsonSerializer.Serialize(output, item);
        var outputAsString = output.ToString();
        output.Dispose();
        return outputAsString;
    }

    /// <summary>
    /// Deserialize Json/Yaml
    /// </summary>
    public T Deserialize<T>(string value) => _deserializer.Deserialize<T>(value);

    /// <summary>
    /// Deserialize Json, with the same options <see cref="SerializeToJson{T}"/> writes it.
    /// </summary>
    /// <remarks>
    /// <see cref="Deserialize{T}"/> goes through YamlDotNet, and YAML is a superset of
    /// JSON, so it reads most of it. It does not read all of it: the converters
    /// registered here write <c>OrientationLock</c>, <c>Bitrate</c>,
    /// <c>SubtitlePlaybackMode</c>, <c>VideoPlayer</c> and <c>InactivityTimeout</c> as
    /// numbers, and YamlDotNet expects the member name.
    /// Anything stored with <see cref="SerializeToJson{T}"/> has to come back through
    /// this, or those five settings do not survive the round trip.
    /// </remarks>
    /// <typeparam name="T">What to read it as.</typeparam>
    /// <param name="value">The JSON.</param>
    /// <returns>The value, or <c>null</c> for a JSON null.</returns>
    public T? DeserializeJson<T>(string value) =>
        JsonSerializer.Deserialize<T>(value, GetJsonSerializerOptions());

    public static ICollection<ITypeMapper> HTMLFormTypeMappers() => new Collection<ITypeMapper>(new List<ITypeMapper>
        {
            new PrimitiveTypeMapper(
                mappedType: typeof(bool),
                (s) =>
                {
                    s.Type = JsonObjectType.Boolean;
                    s.Format = "checkbox";
                    s.ExtensionData = new Dictionary<string, object?>
                    {
                        {
                            "options",
                            new Options(
                                inputAttrs: null,
                                containerAttrs: new Dictionary<string, object?>
                                {
                                    { "class", "checkboxContainer emby-checkbox-label" },
                                    { "style", "text-align: center" },
                                }
                            )
                        }
                    };
                }
            ),
            new PrimitiveTypeMapper(
                mappedType: typeof(string),
                (s) =>
                {
                    s.Type = JsonObjectType.String;
                    s.ExtensionData = new Dictionary<string, object?>
                    {
                        {
                            "options",
                            new Options(
                                inputAttrs: new Dictionary<string, object?>
                                {
                                    { "class", "emby-input" },
                                },
                                containerAttrs: new Dictionary<string, object?>
                                {
                                    { "class", "inputContainer" },
                                }
                            )
                        }
                    };
                }
            ),
            new PrimitiveTypeMapper(
                mappedType: typeof(int),
                (s) =>
                {
                    s.Type = JsonObjectType.Integer;
                    s.Format = "number";
                    s.ExtensionData = new Dictionary<string, object?>
                    {
                        {
                            "options",
                            new Options(
                                inputAttrs: new Dictionary<string, object?>
                                {
                                    { "class", "emby-input" },
                                },
                                containerAttrs: new Dictionary<string, object?>
                                {
                                    { "class", "inputContainer" },
                                }
                            )
                        }
                    };
                }
            )
        }
    );

    public class Options
    {
        [JsonProperty("inputAttributes", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Dictionary<string, object?>? InputAttrs { get; set; }

        [JsonProperty("containerAttributes", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Dictionary<string, object?>? ContainerAttrs { get; set; }

        public Options(
            Dictionary<string, object?>? inputAttrs = null,
            Dictionary<string, object?>? containerAttrs = null
        )
        {
            if (inputAttrs is null && containerAttrs is null)
                return;

            InputAttrs = inputAttrs;
            ContainerAttrs = containerAttrs;
        }
    }
}