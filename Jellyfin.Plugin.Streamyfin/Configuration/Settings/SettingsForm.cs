using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// The kind of control a setting needs.
/// </summary>
/// <remarks>
/// Decided here, from the value's type, rather than in each page that draws a
/// setting. P3.1 left that decision to json-editor reading a JSON schema at runtime,
/// which meant the schema had to be reshaped four ways before it would draw the right
/// thing, and that nothing could test what a setting would come out as.
/// </remarks>
public enum SettingsControl
{
    /// <summary>No control maps to this value's type. A test fails on it.</summary>
    Unknown,

    /// <summary>A checkbox.</summary>
    Toggle,

    /// <summary>A number field, with bounds when the setting declares them.</summary>
    Number,

    /// <summary>A single line of text.</summary>
    Text,

    /// <summary>A credential, masked.</summary>
    Secret,

    /// <summary>A choice among declared values.</summary>
    Select,

    /// <summary>Several free values, such as library ids.</summary>
    List,

    /// <summary>A language, which the app matches on its ISO code.</summary>
    Language,

    /// <summary>A shape with fields of its own, edited by a control written for it.</summary>
    Composite
}

/// <summary>
/// One choice in a dropdown.
/// </summary>
/// <param name="Value">What is stored. Null is a real choice, used for "no cap".</param>
/// <param name="Label">What the administrator reads.</param>
public sealed record SettingsChoice(
    [property: JsonPropertyName("value")] string? Value,
    [property: JsonPropertyName("label")] string Label);

/// <summary>
/// One setting, as the admin form needs it.
/// </summary>
/// <param name="Key">The key in the YAML and in the JSON payload.</param>
/// <param name="Category">The section of the form. From <see cref="SettingScopeAttribute"/>.</param>
/// <param name="Group">The subdivision within that section, when it has one.</param>
/// <param name="Title">The label.</param>
/// <param name="Description">The help text.</param>
/// <param name="Control">Which control draws it.</param>
/// <param name="Lockable">Whether an administrator can pin it against the user.</param>
/// <param name="Minimum">Lowest accepted value, when declared.</param>
/// <param name="Maximum">Highest accepted value, when declared.</param>
/// <param name="Step">The increment, when declared.</param>
/// <param name="Options">The choices, for a <see cref="SettingsControl.Select"/>.</param>
public sealed record SettingsFormField(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("category")] string? Category,
    [property: JsonPropertyName("group")] string? Group,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("control")] SettingsControl Control,
    [property: JsonPropertyName("lockable")] bool Lockable,
    [property: JsonPropertyName("minimum")] double? Minimum,
    [property: JsonPropertyName("maximum")] double? Maximum,
    [property: JsonPropertyName("step")] double? Step,
    [property: JsonPropertyName("options")] IReadOnlyList<SettingsChoice> Options);

/// <summary>
/// The admin form, described in C# rather than inferred from a schema in the browser.
/// </summary>
/// <remarks>
/// Built from <see cref="SettingsSchema.Descriptors"/>, so a new setting is still one
/// property and its attributes, and nothing here is edited to add one.
/// </remarks>
public static class SettingsForm
{
    private static readonly IReadOnlyList<SettingsChoice> _noOptions = [];

    /// <summary>
    /// Every setting, in the order they are declared.
    /// </summary>
    /// <returns>The fields the form draws.</returns>
    public static IReadOnlyList<SettingsFormField> Describe() =>
        SettingsSchema.Descriptors.Select(Describe).ToList();

    private static SettingsFormField Describe(SettingDescriptor descriptor)
    {
        var type = descriptor.ValueType;
        var enumType = EnumTypeOf(type);
        var control = ControlFor(descriptor, type, enumType);
        var range = descriptor.Property.GetCustomAttribute<RangeAttribute>();
        var step = descriptor.Property.GetCustomAttribute<StepAttribute>();

        return new SettingsFormField(
            Key: descriptor.Key,
            Category: descriptor.Category,
            Group: descriptor.Group,
            Title: descriptor.DisplayName,
            Description: descriptor.Description,
            Control: control,
            Lockable: descriptor.IsLockable,
            Minimum: AsNumber(range?.Minimum),
            Maximum: AsNumber(range?.Maximum),
            Step: step?.Value,
            Options: enumType is null ? _noOptions : Choices(enumType, AcceptsNull(type)));
    }

    private static SettingsControl ControlFor(SettingDescriptor descriptor, Type type, Type? enumType)
    {
        if (enumType is not null)
        {
            return SettingsControl.Select;
        }

        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(bool))
        {
            return SettingsControl.Toggle;
        }

        if (underlying == typeof(int) || underlying == typeof(long)
            || underlying == typeof(double) || underlying == typeof(float)
            || underlying == typeof(decimal))
        {
            return SettingsControl.Number;
        }

        if (underlying == typeof(string))
        {
            return descriptor.IsSecret ? SettingsControl.Secret : SettingsControl.Text;
        }

        if (underlying == typeof(LanguagePreference))
        {
            return SettingsControl.Language;
        }

        // An array or a list of anything is several values the administrator types or
        // picks, whatever the element is.
        if (underlying.IsArray || (underlying.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(underlying)))
        {
            return SettingsControl.List;
        }

        // A shape with fields of its own: the home layout, a device profile, a library's
        // display options. Each gets a control written for it rather than a generic one
        // that would describe none of them well.
        return underlying.IsClass ? SettingsControl.Composite : SettingsControl.Unknown;
    }

    private static Type? EnumTypeOf(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying.IsEnum ? underlying : null;
    }

    private static bool AcceptsNull(Type type) => Nullable.GetUnderlyingType(type) is not null;

    /// <summary>
    /// The choices for one enum, labelled for a person.
    /// </summary>
    /// <remarks>
    /// A nullable enum keeps null as its first choice. Only the playback quality is one
    /// today, where null is the app's "Max", meaning no cap. P3.1 could not express that:
    /// json-editor labels a null entry "null" however its title is set, so the schema had
    /// to encode it as an empty string and the page had to turn the empty string back into
    /// null on the way out. Drawing the control ourselves, null is simply a choice.
    /// </remarks>
    private static List<SettingsChoice> Choices(Type enumType, bool acceptsNull)
    {
        var choices = new List<SettingsChoice>();

        if (acceptsNull)
        {
            choices.Add(new SettingsChoice(null, "Max"));
        }

        foreach (var member in enumType.GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var display = member.GetCustomAttribute<DisplayAttribute>()?.Name;
            choices.Add(new SettingsChoice(member.Name, display ?? Humanize(member.Name)));
        }

        return choices;
    }

    private static double? AsNumber(object? value) =>
        value is null ? null : Convert.ToDouble(value, CultureInfo.InvariantCulture);

    /// <summary>
    /// Turns a member name into something an administrator can read.
    /// </summary>
    /// <remarks>
    /// Nothing in <c>Enums.cs</c> carries a display name, so every dropdown showed CLR
    /// identifiers: <c>_250KB</c> for a playback quality, <c>OnlyForced</c> for a subtitle
    /// mode. Deriving covers those, and a <c>Display</c> attribute on the member overrides
    /// it where deriving would be wrong, so the exceptions cost one attribute rather than
    /// a table of ninety.
    ///
    /// <para>
    /// A run of capitals stays together, because it is an acronym and not a set of words:
    /// <c>_250KB</c> reads "250 KB" and not "250 K B".
    /// </para>
    /// </remarks>
    private static string Humanize(string name)
    {
        var trimmed = name.TrimStart('_');
        if (trimmed.Length == 0)
        {
            return name;
        }

        var words = new List<string>();
        var word = new StringBuilder();

        foreach (var (character, index) in trimmed.Select((c, i) => (c, i)))
        {
            var previous = index > 0 ? trimmed[index - 1] : '\0';
            var breaks = index > 0
                && ((char.IsUpper(character) && !char.IsUpper(previous))
                    || (char.IsDigit(character) != char.IsDigit(previous))
                    || (char.IsUpper(character) && char.IsUpper(previous)
                        && index + 1 < trimmed.Length && char.IsLower(trimmed[index + 1])));

            if (breaks && word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }

            word.Append(character);
        }

        if (word.Length > 0)
        {
            words.Add(word.ToString());
        }

        // Sentence case: the first word carries the capital, the rest go lower unless
        // they are acronyms, which would be wrong to lowercase.
        for (var i = 1; i < words.Count; i++)
        {
            var current = words[i];
            var acronym = current.Length > 1 && current.All(c => char.IsUpper(c) || char.IsDigit(c));

            if (!acronym)
            {
                words[i] = current.ToLowerInvariant();
            }
        }

        return string.Join(' ', words);
    }
}
