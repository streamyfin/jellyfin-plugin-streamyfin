using System;
using Jellyfin.Plugin.Streamyfin.Integrations;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// Marks a setting the server can try before it is saved.
/// </summary>
/// <remarks>
/// An address is the one kind of setting that can be wrong in a way nobody notices: it
/// saves, it looks right, and the failure shows up as an empty tab days later. The form
/// draws a button for a setting that declares this, and the server does the reaching.
/// Declaring it also says the value is a whole http or https address, which is what
/// refuses one before it is stored.
/// </remarks>
/// <param name="kind">Which service is at the other end.</param>
[AttributeUsage(AttributeTargets.Property)]
public sealed class ProbeAttribute(IntegrationKind kind) : Attribute
{
    /// <summary>
    /// Gets the service at the other end.
    /// </summary>
    public IntegrationKind Kind { get; } = kind;
}
