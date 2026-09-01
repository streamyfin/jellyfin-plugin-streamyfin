using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// One setting, described rather than hard coded in the places that need it.
/// </summary>
/// <param name="Key">The key as it appears in the YAML and in the JSON payload.</param>
/// <param name="Property">The property on <see cref="Settings"/> that holds it.</param>
/// <param name="ValueType">The value's type, unwrapped from <see cref="Lockable{T}"/>.</param>
/// <param name="IsLockable">Whether an admin can lock this setting against the user.</param>
/// <param name="IsSecret">Whether the value is a credential. See <see cref="SecretAttribute"/>.</param>
/// <param name="DisplayName">Label for a form, when the property carries one.</param>
/// <param name="Description">Help text for a form, when the property carries one.</param>
/// <param name="Category">The section of the form it belongs to. See <see cref="SettingScopeAttribute"/>.</param>
/// <param name="Group">The subdivision within that category, when it has one.</param>
public sealed record SettingDescriptor(
    string Key,
    PropertyInfo Property,
    Type ValueType,
    bool IsLockable,
    bool IsSecret,
    string? DisplayName,
    string? Description,
    string? Category,
    string? Group);

/// <summary>
/// The settings, as data.
/// </summary>
/// <remarks>
/// <see cref="Settings"/> is a C# class, which is the right shape for an admin
/// writing YAML and for the schema the form generates from. It is the wrong shape
/// for anything that has to treat settings uniformly: filtering secrets out of a
/// response, resolving a value across the server, group and user levels, or
/// telling a client which keys exist.
///
/// This reads the class once and hands back a list. Anything that needs to walk
/// the settings walks this rather than repeating a property list that then drifts
/// the first time someone adds a key. <c>SettingsSchemaTests</c> is what keeps the
/// two in step.
/// </remarks>
public static class SettingsSchema
{
    private static readonly List<SettingDescriptor> _descriptors = Describe();

    private static readonly Dictionary<string, SettingDescriptor> _byKey =
        _descriptors.ToDictionary(d => d.Key, StringComparer.Ordinal);

    private static readonly List<SettingDescriptor> _secrets =
        _descriptors.FindAll(d => d.IsSecret);

    /// <summary>
    /// Gets every setting, in the order the class declares them.
    /// </summary>
    public static IReadOnlyList<SettingDescriptor> Descriptors => _descriptors;

    /// <summary>
    /// Gets the settings whose values are credentials.
    /// </summary>
    public static IReadOnlyList<SettingDescriptor> Secrets => _secrets;

    /// <summary>
    /// Finds a setting by the key it carries in the YAML.
    /// </summary>
    /// <param name="key">The setting key.</param>
    /// <returns>The descriptor, or null when no such setting exists.</returns>
    public static SettingDescriptor? Find(string key) =>
        key is not null && _byKey.TryGetValue(key, out var descriptor) ? descriptor : null;

    /// <summary>
    /// Whether a setting key holds a credential.
    /// </summary>
    /// <param name="key">The setting key.</param>
    /// <returns>True when the key is marked secret.</returns>
    public static bool IsSecret(string key) => Find(key)?.IsSecret == true;

    private static List<SettingDescriptor> Describe()
    {
        return typeof(Settings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0)
            .Select(Describe)
            .ToList();
    }

    private static SettingDescriptor Describe(PropertyInfo property)
    {
        var declared = property.PropertyType;
        var underlying = Nullable.GetUnderlyingType(declared) ?? declared;

        var lockable = underlying.IsGenericType
                       && underlying.GetGenericTypeDefinition() == typeof(Lockable<>);

        var valueType = lockable ? underlying.GetGenericArguments()[0] : underlying;
        var display = property.GetCustomAttribute<DisplayAttribute>();
        var scope = property.GetCustomAttribute<SettingScopeAttribute>();

        return new SettingDescriptor(
            Key: property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name,
            Property: property,
            ValueType: valueType,
            IsLockable: lockable,
            IsSecret: property.GetCustomAttribute<SecretAttribute>() is not null,
            DisplayName: display?.Name,
            Description: display?.Description,
            Category: scope?.Category,
            Group: scope?.Group);
    }
}
