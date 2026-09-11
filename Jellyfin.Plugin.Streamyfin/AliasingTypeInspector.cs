using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Reflection;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using YamlDotNet.Core;
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

        // Asked without the exception first, so an alias does not depend on the real
        // name failing loudly.
        if (canonical is not null)
        {
            var aliased = _inner.GetProperty(type, container, canonical, true, caseInsensitivePropertyMatching);
            if (aliased is not null)
            {
                return new WrittenOnce(aliased, name);
            }
        }

        var found = _inner.GetProperty(type, container, name, ignoreUnmatched, caseInsensitivePropertyMatching);

        // The canonical name is guarded too, or the same collision would pass unnoticed
        // whenever the alias happened to come first in the document.
        return found is not null && HasAliases(type, found.Name)
            ? new WrittenOnce(found, name)
            : found!;
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

    private static bool HasAliases(Type type, string propertyName)
    {
        lock (_aliases)
        {
            if (!_aliases.TryGetValue(type, out var cached))
            {
                cached = Build(type);
                _aliases[type] = cached;
            }

            return cached.ContainsValue(propertyName);
        }
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

/// <summary>
/// A setting one document may fill in once, whichever of its names it uses.
/// </summary>
/// <remarks>
/// A document carrying both <c>jellyseerrApiKey</c> and <c>seerrApiKey</c> has the two
/// spellings of one setting saying different things. YamlDotNet's answer is that the
/// later key wins, silently, and the whole stored pair goes with it, <c>locked</c>
/// included. Silence is what made #95 worth reporting in the first place, so the
/// collision is refused and names both spellings.
/// </remarks>
/// <param name="inner">The property this stands in for.</param>
/// <param name="spelling">The name the document used, for the message.</param>
internal sealed class WrittenOnce(IPropertyDescriptor inner, string spelling) : IPropertyDescriptor
{
    private readonly IPropertyDescriptor _inner = inner;

    /// <inheritdoc/>
    public string Name => _inner.Name;

    /// <inheritdoc/>
    public bool AllowNulls => _inner.AllowNulls;

    /// <inheritdoc/>
    public bool CanWrite => _inner.CanWrite;

    /// <inheritdoc/>
    public Type Type => _inner.Type;

    /// <inheritdoc/>
    public Type? TypeOverride
    {
        get => _inner.TypeOverride;
        set => _inner.TypeOverride = value!;
    }

    /// <inheritdoc/>
    public int Order
    {
        get => _inner.Order;
        set => _inner.Order = value;
    }

    /// <inheritdoc/>
    public ScalarStyle ScalarStyle
    {
        get => _inner.ScalarStyle;
        set => _inner.ScalarStyle = value;
    }

    /// <inheritdoc/>
    public bool Required => _inner.Required;

    /// <inheritdoc/>
    public Type ConverterType => _inner.ConverterType!;

    /// <inheritdoc/>
    public T GetCustomAttribute<T>()
        where T : Attribute => _inner.GetCustomAttribute<T>()!;

    /// <inheritdoc/>
    public IObjectDescriptor Read(object target) => _inner.Read(target);

    /// <inheritdoc/>
    public void Write(object target, object? value)
    {
        // A setting starts null on a document that has not mentioned it, so anything
        // already there was written by this same document under its other name.
        if (_inner.Read(target)?.Value is not null)
        {
            throw new InvalidOperationException(
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} is set twice, once as {1}. A setting is written under one name.",
                    _inner.Name,
                    spelling));
        }

        _inner.Write(target, value);
    }
}
