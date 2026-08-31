#pragma warning disable CA1869

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Data.Enums;
using Jellyfin.Extensions.Json;
using Jellyfin.Plugin.Streamyfin.Configuration;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
        return ShapeForGeneratedForm(schema.ToJson());
    }

    /// <summary>
    /// Reshapes the generated schema into what the admin form's json-editor needs. The
    /// schema is otherwise served as generated; this is the one place the form's reading
    /// of it is accommodated, so the page stays a generic consumer.
    /// </summary>
    /// <remarks>
    /// NJsonSchema attaches a property's own keywords, its title and its <c>x-secret</c>
    /// marker, by wrapping the reference to its type in a single-branch <c>oneOf</c>,
    /// because it drops any keyword sitting next to a bare <c>$ref</c>. json-editor reads
    /// a <c>oneOf</c> as a choice between schemas and draws a type selector beside every
    /// setting, a dropdown with one option that edits nothing. Flattening the wrapper to
    /// a plain <c>$ref</c>, which json-editor merges with the keywords next to it, makes
    /// the setting render as itself. Newtonsoft parses and reprints so the rest of the
    /// document keeps the formatting <c>ToJson</c> gave it.
    /// </remarks>
    private static string ShapeForGeneratedForm(string json)
    {
        var root = JToken.Parse(json);
        FlattenSingleBranchReferences(root);
        BlankSharedLockableDescriptions(root);
        InlineSecretsAsPasswords(root);
        CollapseNullableBitrate(root);
        return root.ToString(Formatting.Indented);
    }

    /// <summary>
    /// Turns the nullable playback quality into one dropdown. A <c>Bitrate?</c> is null for no
    /// cap, the app's "Max", so NJsonSchema renders the value as a null-or-reference
    /// <c>oneOf</c>, which json-editor draws as a type selector, over enum names that each
    /// carry a leading underscore. Collapsed to a single string enum with friendly titles it
    /// renders as one dropdown, Max then 250KB through 8MB, the list the page built by hand.
    /// </summary>
    /// <remarks>
    /// "Max" is the empty string rather than json <c>null</c>: json-editor labels a null enum
    /// entry "null" however its title is set, but honours the title of an empty string. The
    /// empty string is coerced back to null before the config is saved, the same way the hand
    /// written page always turned a blank field into null, so a saved "Max" is stored as no cap.
    /// </remarks>
    private static void CollapseNullableBitrate(JToken root)
    {
        if (root["definitions"] is not JObject definitions
            || definitions["Bitrate"] is not JObject bitrate
            || bitrate["enum"] is not JArray names)
        {
            return;
        }

        var values = new JArray { string.Empty };
        var titles = new JArray { "Max" };
        foreach (var name in names.OfType<JValue>())
        {
            var text = (string?)name.Value ?? string.Empty;
            values.Add(text);
            titles.Add(text.TrimStart('_'));
        }

        foreach (var definition in definitions.Properties())
        {
            if (definition.Value is JObject body
                && body["properties"]?["value"] is JObject value
                && value["oneOf"] is JArray branches
                && branches.OfType<JObject>().Any(branch => (branch["$ref"] as JValue)?.Value as string == "#/definitions/Bitrate"))
            {
                value.Remove("oneOf");
                value["type"] = "string";
                value["enum"] = values.DeepClone();
                value["options"] = new JObject { ["enum_titles"] = titles.DeepClone() };
            }
        }
    }

    /// <summary>
    /// Gives each credential its own inlined value, marked <c>format: password</c>, so
    /// json-editor masks it. The <c>x-secret</c> marker is on the property, but the input is
    /// the <c>value</c> inside the shared <c>LockableOfString</c> the property points at, and
    /// three plain URLs point at the same definition and must stay readable. Inlining is the
    /// only place a per-setting override lands without turning those URLs into passwords too.
    /// </summary>
    private static void InlineSecretsAsPasswords(JToken root)
    {
        if (root["definitions"] is not JObject definitions
            || definitions["Settings"] is not JObject settings
            || settings["properties"] is not JObject properties)
        {
            return;
        }

        foreach (var property in properties.Properties())
        {
            if (property.Value is not JObject setting
                || setting["x-secret"] is not JValue marker
                || marker.Value is not true
                || setting["$ref"] is not JValue reference
                || reference.Value is not string definitionPath)
            {
                continue;
            }

            var definitionName = definitionPath.Split('/').Last();
            var locked = (definitions[definitionName] as JObject)?["properties"]?["locked"]?.DeepClone();

            setting.Remove("$ref");
            setting["type"] = "object";
            setting["additionalProperties"] = false;
            setting["properties"] = new JObject
            {
                ["locked"] = locked,
                ["value"] = new JObject
                {
                    ["type"] = new JArray("null", "string"),
                    ["format"] = "password",
                    ["options"] = new JObject
                    {
                        ["inputAttributes"] = new JObject { ["class"] = "emby-input" }
                    }
                }
            };
        }
    }

    /// <summary>
    /// Empties the description on every <c>Lockable&lt;T&gt;</c> definition. They all carry
    /// the same "Assign a lock to given type value", and json-editor shows the referenced
    /// definition's description rather than the property's, so left in place it shadows the
    /// help text written on each setting. Emptied, the setting's own description shows.
    /// </summary>
    private static void BlankSharedLockableDescriptions(JToken root)
    {
        if (root["definitions"] is not JObject definitions)
        {
            return;
        }

        foreach (var definition in definitions.Properties())
        {
            if (definition.Name.StartsWith("LockableOf", System.StringComparison.Ordinal)
                && definition.Value is JObject body
                && body["description"] is not null)
            {
                body["description"] = string.Empty;
            }
        }
    }

    private static void FlattenSingleBranchReferences(JToken node)
    {
        switch (node)
        {
            case JObject obj:
                if (obj["oneOf"] is JArray branches
                    && branches.Count == 1
                    && branches[0] is JObject only
                    && only["$ref"] is JValue reference)
                {
                    obj.Remove("oneOf");
                    obj["$ref"] = (string?)reference.Value;
                }

                foreach (var property in obj.Properties().ToList())
                {
                    FlattenSingleBranchReferences(property.Value);
                }

                break;

            case JArray array:
                foreach (var item in array.ToList())
                {
                    FlattenSingleBranchReferences(item);
                }

                break;
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