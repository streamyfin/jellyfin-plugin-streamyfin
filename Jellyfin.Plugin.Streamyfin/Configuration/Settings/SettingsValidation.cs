using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// What the server refuses to store.
/// </summary>
/// <remarks>
/// The admin form already refuses a value outside a setting's bounds, and says which
/// setting and why. It is the only thing that does: the Yaml tab writes whatever is
/// typed, and the targeting routes take a JSON body from anything holding an API key.
/// A skip time of 600 was accepted by all three, reached the app, and made a button
/// jump ten minutes.
///
/// <para>
/// An address is checked for being one at all. The Yaml tab accepted
/// <c>http://:5055</c>, which parses as YAML and is not an address, and the app was
/// handed it. Whether anything answers there is a different question, and one only a
/// probe can ask.
/// </para>
///
/// <para>
/// The home layout is checked here too, by <see cref="Sections"/>: a section carrying
/// two queries used whichever the app tested first, and a section carrying none drew
/// an empty row under a title.
/// </para>
///
/// <para>
/// The bounds come from <see cref="BoundsAttribute"/>, the same declaration the form
/// draws from, so a setting is bounded once and both ends agree. Nothing here holds a
/// list of keys.
/// </para>
/// </remarks>
public static class SettingsValidation
{
    /// <summary>
    /// The reasons these settings cannot be stored, one line each.
    /// </summary>
    /// <param name="settings">The settings, or the part of them a level carries.</param>
    /// <returns>The problems, in declaration order. Empty when there are none.</returns>
    public static IReadOnlyList<string> Problems(Settings? settings)
    {
        if (settings is null)
        {
            return [];
        }

        var problems = new List<string>(Sections.Problems(settings));

        foreach (var descriptor in SettingsSchema.Descriptors)
        {
            var bounds = descriptor.Bounds;
            if (bounds is null && !descriptor.IsWebAddress)
            {
                continue;
            }

            // A level carries a setting or it does not. The absent ones are not this
            // method's business: they fall through to the level below.
            var value = Read(descriptor, settings);
            if (value is null && descriptor.Bounds is not null)
            {
                continue;
            }

            if (descriptor.IsWebAddress && value is string address && !string.IsNullOrWhiteSpace(address)
                && !WebAddress.Parses(address, out _))
            {
                problems.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} needs a whole http or https address, and this is \"{1}\".",
                    descriptor.DisplayName ?? descriptor.Key,
                    address));
            }

            if (bounds is null || value is null || !IsNumber(value))
            {
                continue;
            }

            var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (number < bounds.Minimum || number > bounds.Maximum)
            {
                problems.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} accepts {1} to {2}, and this is {3}.",
                    descriptor.DisplayName ?? descriptor.Key,
                    Number(bounds.Minimum),
                    Number(bounds.Maximum),
                    Number(number)));
            }
        }

        return problems;
    }

    /// <summary>
    /// Tidies these settings and says what still cannot be stored.
    /// </summary>
    /// <param name="settings">The settings, which are trimmed in place.</param>
    /// <returns>The message to refuse with, or <c>null</c> when they can be stored.</returns>
    /// <remarks>
    /// One call, because the two halves have to happen in this order and a write path
    /// that did only the second stored an address with the space the first removes.
    /// </remarks>
    public static string? Check(Settings? settings)
    {
        Tidy(settings);

        return Message(settings);
    }

    /// <summary>
    /// Trims the settings that are addresses, in place.
    /// </summary>
    /// <param name="settings">The settings, which may be null.</param>
    /// <remarks>
    /// The check trims before parsing, so an address pasted with a space passes it and
    /// was then stored with the space. The Yaml tab and the targeting routes have no
    /// form to trim it for them.
    /// </remarks>
    public static void Tidy(Settings? settings)
    {
        if (settings is null)
        {
            return;
        }

        foreach (var descriptor in SettingsSchema.Descriptors)
        {
            if (!descriptor.IsWebAddress)
            {
                continue;
            }

            if (Read(descriptor, settings) is not string address)
            {
                continue;
            }

            var trimmed = address.Trim();
            if (string.Equals(trimmed, address, StringComparison.Ordinal))
            {
                continue;
            }

            if (descriptor.Lockable)
            {
                descriptor.Value!.SetValue(descriptor.Property.GetValue(settings), trimmed);
            }
            else
            {
                descriptor.Property.SetValue(settings, trimmed);
            }
        }
    }

    /// <summary>
    /// The problems as one message, or null when there are none.
    /// </summary>
    /// <param name="settings">The settings to check.</param>
    /// <returns>The message, or <c>null</c>.</returns>
    public static string? Message(Settings? settings)
    {
        var problems = Problems(settings);

        return problems.Count == 0 ? null : string.Join(" ", problems);
    }

    // A setting is usually a Lockable, which keeps its value one level down. One that
    // is not is read directly, or [WebAddress] on a plain property would be a rule the
    // form applies and the server does not.
    private static object? Read(SettingDescriptor descriptor, Settings settings)
    {
        var held = descriptor.Property.GetValue(settings);

        return descriptor.Lockable ? (held is null ? null : descriptor.Value?.GetValue(held)) : held;
    }

    private static bool IsNumber(object value) =>
        value is byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal;

    // 60 rather than 60.0, since every bound declared today is a whole number and an
    // administrator reading the message is not thinking in doubles. The comparison is a
    // difference rather than an equality, which is how a double says "whole".
    private static string Number(double value) =>
        Math.Abs(value - Math.Truncate(value)) < 1e-9 && Math.Abs(value) < 1e15
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString(CultureInfo.InvariantCulture);
}
