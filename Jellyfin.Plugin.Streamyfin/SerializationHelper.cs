#pragma warning disable CA1869

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions.Json;
using Jellyfin.Plugin.Streamyfin.Configuration;
using Newtonsoft.Json;
using NJsonSchema;
using NJsonSchema.Generation;
using NJsonSchema.Generation.TypeMappers;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using JsonSchemaGenerator = NJsonSchema.Generation.JsonSchemaGenerator;
using JsonSerializer = System.Text.Json.JsonSerializer;


namespace Jellyfin.Plugin.Streamyfin;

/// <summary>
/// Serialization settings for json and yaml
/// </summary>
public class SerializationHelper
{
    private readonly IDeserializer _deserializer;
    private readonly ISerializer _yamlSerializer;

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
    }

    private JsonSerializerOptions GetJsonSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonDefaults.Options);
        // Prioritize these first since other converters & defaults change expected behavior
        options.Converters.Insert(0, new JsonNumberEnumConverter<SubtitlePlaybackMode>());
        options.Converters.Insert(0, new JsonNumberEnumConverter<OrientationLock>());
        options.Converters.Insert(0, new JsonNumberEnumConverter<RemuxConcurrentLimit>());

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
        return JsonSchemaGenerator.FromType<T>(settings).ToJson();
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
    /// Deserialize Json/Yaml
    /// </summary>
    public T Deserialize<T>(string value) => _deserializer.Deserialize<T>(value);

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