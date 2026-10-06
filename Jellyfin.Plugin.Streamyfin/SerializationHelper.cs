#pragma warning disable CA1869

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
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
            // Reading only. A setting declaring [AlsoKnownAs] answers to that spelling as
            // well, which is how seerrServerUrl stopped being ignored in silence, and the
            // document still comes back written under one name per setting.
            .WithTypeInspector(inner => new AliasingTypeInspector(inner))
            .Build();

        _jsonSerializer = NewtonsoftJsonSerializer.CreateDefault();
    }

    /// <summary>
    /// The options the plugin stores JSON with, and reads it back with.
    /// </summary>
    /// <remarks>
    /// The global configuration, every targeting level and every backup are written with
    /// these, and every build that may run on the same database has to read them, a
    /// rollback included. What the app receives is <see cref="GetAppJsonSerializerOptions"/>.
    /// </remarks>
    public JsonSerializerOptions GetJsonSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonDefaults.Options);

        // Jellyfin's options omit a null when writing, so a setting whose value is null,
        // the playback quality's "no cap", left the document without its value key. Read
        // back, that tripped the required value on Lockable<T> and threw, the tolerant
        // read answered null, and a whole targeting level came back empty: a group set to
        // Max looked saved until the page was reopened and gave its members nothing.
        // An absent value means null here, which is what omitting it meant.
        options.TypeInfoResolver = (options.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver())
            .WithAddedModifier(type =>
            {
                if (type.Type.IsGenericType && type.Type.GetGenericTypeDefinition() == typeof(Lockable<>))
                {
                    foreach (var property in type.Properties)
                    {
                        property.IsRequired = false;
                    }
                }
            });

        // Prioritize these first since other converters & defaults change expected behavior.
        // SubtitlePlaybackMode stays a number here, in storage only: every build from before
        // it went out by name reads nothing else, and one rolled back to would serve an
        // empty configuration. The app gets the name, see GetAppJsonSerializerOptions.
        options.Converters.Insert(0, new JsonNumberEnumConverter<SubtitlePlaybackMode>());
        options.Converters.Insert(0, new JsonNumberEnumConverter<OrientationLock>());
        options.Converters.Insert(0, new JsonNumberEnumConverter<Bitrate>());
        options.Converters.Insert(0, new JsonNumberEnumConverter<InactivityTimeout>());

#if DEBUG
        options.WriteIndented = true;
#endif
        return options;
    }

    /// <summary>
    /// The options every JSON the app receives is written with.
    /// </summary>
    /// <remarks>
    /// The stored form, except for the subtitle mode: the app compares the SDK's strings
    /// for it, so the number never matched, and a mode an administrator locked did
    /// nothing. Public because the parity test compares a declared default against what
    /// the app reads, and it has to compare the written form. Comparing CLR values would
    /// pass for an enum written as a number where the app expects its name.
    /// </remarks>
    public JsonSerializerOptions GetAppJsonSerializerOptions()
    {
        var options = GetJsonSerializerOptions();
        options.Converters.Remove(options.Converters.OfType<JsonNumberEnumConverter<SubtitlePlaybackMode>>().Single());
        return options;
    }

    /// <summary>
    /// Generate schema to json
    /// </summary>
    /// <remarks>
    /// The names are camel cased to match the config, which YamlDotNet reads under the
    /// camel case convention. Every settings type agreed already, its properties being
    /// lower case to begin with, except <c>LanguagePreference</c>: its members are
    /// PascalCase so the app can match them against the SDK's <c>CultureDto</c>, and the
    /// schema described a name its own reader rejected. A form fills in what the schema
    /// tells it to, so that made the two language settings impossible to save. This only
    /// touches how the schema spells a name; what the app is served is written by
    /// <see cref="SerializeToJson{T}"/> and keeps the CLR names.
    /// </remarks>
    public static string GetJsonSchema<T>()
    {
        var settings = new SystemTextJsonSchemaGeneratorSettings
        {
            TypeMappers = HTMLFormTypeMappers(),
            // A derived type as allOf its base and its own properties, each closed with
            // additionalProperties false, refuses the other's properties, so the Yaml
            // editor flagged itemAdded.enabled. One object per type does not.
            FlattenInheritanceHierarchy = true
        };
#if DEBUG
        settings.SerializerOptions.WriteIndented = true;
#endif
        settings.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        settings.SerializerOptions.Converters.Add(new JsonStringEnumConverter());

        var schema = JsonSchemaGenerator.FromType<T>(settings);
        MarkSecrets(schema);
        MarkCategories(schema);
        return schema.ToJson();
    }

    /// <summary>
    /// Carries each setting's section of the form, as <c>x-category</c> and
    /// <c>x-group</c>.
    /// </summary>
    /// <remarks>
    /// The form renders one collapsible section per category rather than ninety two
    /// settings in a single column. The values come from <see cref="SettingsSchema"/>,
    /// so a new setting picks its section with one attribute and nothing here changes.
    /// </remarks>
    private static void MarkCategories(JsonSchema schema)
    {
        foreach (var candidate in SchemasCarryingSettings(schema))
        {
            foreach (var descriptor in SettingsSchema.Descriptors)
            {
                if (descriptor.Category is null
                    || !candidate.Properties.TryGetValue(descriptor.Key, out var property))
                {
                    continue;
                }

                property.ExtensionData ??= new Dictionary<string, object?>();
                property.ExtensionData["x-category"] = descriptor.Category;

                if (descriptor.Group is not null)
                {
                    property.ExtensionData["x-group"] = descriptor.Group;
                }
            }
        }
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
    /// Serialize to the Json the app receives, see <see cref="GetAppJsonSerializerOptions"/>.
    /// </summary>
    public string SerializeForApp<T>(T item) =>
        JsonSerializer.Serialize(item, GetAppJsonSerializerOptions());

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
    /// Stored JSON comes back through the options that wrote it, number converters
    /// included. <see cref="Deserialize{T}"/> reads JSON as well, YAML being a superset of
    /// it, and YamlDotNet even takes these enums as numbers, but what the stored form
    /// means is defined here, not by what a YAML reader happens to accept.
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