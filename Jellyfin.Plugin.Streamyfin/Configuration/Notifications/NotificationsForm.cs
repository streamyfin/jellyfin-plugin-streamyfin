using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Notifications;

/// <summary>
/// The notification events, described rather than drawn.
/// </summary>
/// <remarks>
/// The four events were four properties here and four blocks of markup in the admin
/// page, so adding one meant writing it twice and the page could disagree with the
/// server about what a field was called. They are described the way the settings are
/// described in <see cref="SettingsForm"/>, from the same attributes, and the page draws
/// whatever it is handed. Adding an event is a property and its <see cref="DisplayAttribute"/>.
///
/// <para>
/// This is the first half of P4.4. What it does not do yet is let an event be declared
/// anywhere but on <see cref="Notifications"/>; #29, #34 and #30 each need a decision of
/// their own before that, and the triage says so.
/// </para>
/// </remarks>
public static class NotificationsForm
{
    private static readonly IReadOnlyList<SettingsChoice> _noOptions = [];

    /// <summary>
    /// Every field of every event, in the order they are declared.
    /// </summary>
    /// <param name="libraries">
    /// The libraries this server has, offered as the choices for the event that can be
    /// restricted to some of them. Null when the caller has none to offer.
    /// </param>
    /// <returns>The fields the page draws.</returns>
    public static IReadOnlyList<SettingsFormField> Describe(
        IReadOnlyList<SettingsChoice>? libraries = null)
    {
        var fields = new List<SettingsFormField>();

        foreach (var eventProperty in typeof(Notifications).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var display = eventProperty.GetCustomAttribute<DisplayAttribute>();
            var category = display?.GetName() ?? eventProperty.Name;
            var eventKey = JsonNameOf(eventProperty);
            var blockType = Nullable.GetUnderlyingType(eventProperty.PropertyType) ?? eventProperty.PropertyType;


            foreach (var field in InDeclaredOrder(blockType))
            {
                var control = ControlFor(field.PropertyType);
                if (control == SettingsControl.Unknown)
                {
                    continue;
                }

                var fieldDisplay = field.GetCustomAttribute<DisplayAttribute>();
                var described = fieldDisplay?.GetDescription();

                fields.Add(new SettingsFormField(
                    Key: $"{eventKey}.{JsonNameOf(field)}",
                    Category: category,
                    Group: null,
                    Title: fieldDisplay?.GetName() ?? field.Name,
                    // The switch that turns an event on says what the event is. The
                    // property's own sentence, "if true, the notifications for this
                    // event are enabled", says only what a checkbox is for, and the
                    // card is already named after the event.
                    Description: JsonNameOf(field) == "enabled"
                        ? display?.GetDescription() ?? described
                        : described,
                    Control: control,
                    // An event is on or off for the server, not pinned against a user.
                    // The three states belong to the settings, and saying so here keeps
                    // the page from drawing a control that would write nowhere.
                    Lockable: false,
                    Minimum: control == SettingsControl.Number ? 0 : null,
                    Maximum: null,
                    Step: null,
                    Options: OptionsFor(field, libraries),
                    DependsOn: DependsOn(field, eventKey),
                    Integer: false,
                    Probe: null,
                    Address: false));
            }
        }

        return fields;
    }

    /// <summary>
    /// The fields of an event, the switch that turns it on first.
    /// </summary>
    /// <param name="blockType">The type holding the event's fields.</param>
    /// <returns>The properties, inherited ones before the ones added on top.</returns>
    /// <remarks>
    /// Reflection hands back a derived type's own properties before the ones it
    /// inherits, so the libraries an event may come from came out above the switch that
    /// turns the event on. The order a page draws in is the order the fields are read
    /// in, so it is decided here rather than left to that.
    /// </remarks>
    private static IEnumerable<PropertyInfo> InDeclaredOrder(Type blockType)
    {
        var depths = new Dictionary<Type, int>();
        var depth = 0;
        for (var type = blockType; type is not null && type != typeof(object); type = type.BaseType)
        {
            depths[type] = depth++;
        }

        return blockType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(property => depths.TryGetValue(property.DeclaringType!, out var found) ? found : 0);
    }

    /// <summary>
    /// The name a field carries in the payload, which is the name the page has to send
    /// back and the name the YAML is written in.
    /// </summary>
    /// <param name="property">The property.</param>
    /// <returns>Its JSON name.</returns>
    private static string JsonNameOf(PropertyInfo property) =>
        property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
        ?? char.ToLowerInvariant(property.Name[0]) + property.Name[1..];

    private static SettingsControl ControlFor(Type type)
    {
        var bare = Nullable.GetUnderlyingType(type) ?? type;

        if (bare == typeof(bool))
        {
            return SettingsControl.Toggle;
        }

        if (bare == typeof(double) || bare == typeof(int) || bare == typeof(long))
        {
            return SettingsControl.Number;
        }

        if (bare == typeof(string))
        {
            return SettingsControl.Text;
        }

        if (bare == typeof(string[]))
        {
            return SettingsControl.List;
        }

        return SettingsControl.Unknown;
    }

    // A list the server can enumerate is a set of choices rather than free text: the
    // libraries an event may come from are the one case today, and the page draws the
    // boxes it is given instead of knowing which field means libraries.
    private static IReadOnlyList<SettingsChoice> OptionsFor(
        PropertyInfo field,
        IReadOnlyList<SettingsChoice>? libraries) =>
        JsonNameOf(field) == "enabledLibraries" && libraries is not null ? libraries : _noOptions;

    // Everything in an event only matters while the event is on, which the page shows
    // the same way it shows a setting that depends on another.
    private static string? DependsOn(PropertyInfo field, string eventKey) =>
        JsonNameOf(field) == "enabled" ? null : $"{eventKey}.enabled";
}
