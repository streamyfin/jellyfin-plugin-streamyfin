using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using YamlDotNet.Serialization;

namespace Jellyfin.Plugin.Streamyfin;

/// <summary>
/// Lets a setting be read under another name, without being written under it.
/// </summary>
/// <remarks>
/// <see cref="ITypeInspector.GetProperties"/> is what the serializer enumerates when it
/// writes, and it is left alone: a document comes back with one spelling per setting.
/// <see cref="ITypeInspector.GetProperty"/> is what the deserializer asks when it meets
/// a key, and that is where an alias answers.
/// </remarks>
/// <param name="inner">The inspector this decorates.</param>
public sealed class AliasingTypeInspector(ITypeInspector inner) : ITypeInspector
{
    private static readonly Dictionary<Type, Dictionary<string, string>> _aliases = [];

    private readonly ITypeInspector _inner = inner;

    /// <inheritdoc/>
    public IEnumerable<IPropertyDescriptor> GetProperties(Type type, object? container) =>
        _inner.GetProperties(type, container);

    /// <inheritdoc/>
    public IPropertyDescriptor GetProperty(
        Type type,
        object? container,
        string name,
        bool ignoreUnmatched,
        bool caseInsensitivePropertyMatching)
    {
        var canonical = CanonicalName(type, name, caseInsensitivePropertyMatching);

        // Asked without the exception first: an alias should not depend on the real name
        // failing loudly, and a document carrying both spellings must not be refused.
        if (canonical is not null)
        {
            var found = _inner.GetProperty(type, container, canonical, true, caseInsensitivePropertyMatching);
            if (found is not null)
            {
                return found;
            }
        }

        return _inner.GetProperty(type, container, name, ignoreUnmatched, caseInsensitivePropertyMatching);
    }

    /// <inheritdoc/>
    public string GetEnumName(Type enumType, string name) => _inner.GetEnumName(enumType, name);

    /// <inheritdoc/>
    public string GetEnumValue(object enumValue) => _inner.GetEnumValue(enumValue);

    private static string? CanonicalName(Type type, string name, bool caseInsensitive)
    {
        Dictionary<string, string> known;

        lock (_aliases)
        {
            if (!_aliases.TryGetValue(type, out var cached))
            {
                cached = Build(type);
                _aliases[type] = cached;
            }

            known = cached;
        }

        if (known.Count == 0)
        {
            return null;
        }

        if (known.TryGetValue(name, out var canonical))
        {
            return canonical;
        }

        if (!caseInsensitive)
        {
            return null;
        }

        return known
            .Where(entry => string.Equals(entry.Key, name, StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Value)
            .FirstOrDefault();
    }

    private static Dictionary<string, string> Build(Type type)
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            foreach (var alias in property.GetCustomAttributes<AlsoKnownAsAttribute>())
            {
                aliases[alias.Name] = property.Name;
            }
        }

        return aliases;
    }
}
