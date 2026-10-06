using System;
using System.Collections.Generic;
using System.Reflection;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// Offers a setting stored as text as a choice among the values the app takes for it.
/// </summary>
/// <remarks>
/// An enum carries its choices by itself. A string does not, and free text is a poor way
/// to ask for a value the app picks from a list of its own: the app language took any code
/// at all and named none of them. The list lives on the type this names, as a static
/// <c>Choices</c> property, and the stored value is still the string the Yaml tab can
/// write, so the form keeps showing a value the list does not offer rather than drop it.
/// </remarks>
/// <param name="source">The type whose static <c>Choices</c> property holds the list.</param>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ChoicesAttribute(Type source) : Attribute
{
    /// <summary>
    /// Gets the type that holds the list.
    /// </summary>
    public Type Source { get; } = source;

    /// <summary>
    /// Gets the choices, in the order the form offers them.
    /// </summary>
    /// <exception cref="InvalidOperationException">The type holds no such list.</exception>
    public IReadOnlyList<SettingsChoice> Choices =>
        Source.GetProperty("Choices", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as IReadOnlyList<SettingsChoice>
        ?? throw new InvalidOperationException($"{Source.Name} has no public static Choices list to offer.");
}
