using System;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// Marks a setting whose value is a credential rather than a preference.
/// </summary>
/// <remarks>
/// Secrecy is a property of the key, not of the value an admin happens to write,
/// so it lives here rather than as a field on <see cref="Lockable{T}"/>. An admin
/// cannot mark the Seerr admin key public, and a marked key costs nothing in the
/// YAML.
///
/// Two things read this. The generated JSON schema carries it as <c>x-secret</c>,
/// so a form renders the field as a password instead of plain text. P1.4 uses it
/// to decide what leaves <c>GET /streamyfin/config</c> for a caller who is not an
/// administrator, which is the finding at the top of
/// <c>docs/rewrite/state-of-the-plugin.md</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
public sealed class SecretAttribute : Attribute
{
}
