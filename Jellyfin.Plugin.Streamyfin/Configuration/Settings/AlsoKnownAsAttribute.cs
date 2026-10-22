using System;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// Another spelling a setting answers to when it is read.
/// </summary>
/// <remarks>
/// Jellyseerr was renamed Seerr, and administrators write the new name. Issue #95 is
/// exactly that: <c>seerrServerUrl</c> was ignored in silence, the app never saw a
/// server, and nothing said why.
///
/// <para>
/// Read only, and deliberately. The canonical name is what the plugin writes, so the
/// document an administrator gets back keeps one spelling per setting and every copy of
/// the app in the field keeps reading the key it knows. The day the app reads the new
/// name, the canonical one moves and the old spelling becomes the alias, without a
/// second migration for anyone who typed either.
/// </para>
/// </remarks>
/// <param name="name">The other spelling, in the casing the document uses.</param>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true)]
public sealed class AlsoKnownAsAttribute(string name) : Attribute
{
    /// <summary>
    /// Gets the other spelling this setting answers to.
    /// </summary>
    public string Name { get; } = name;
}
