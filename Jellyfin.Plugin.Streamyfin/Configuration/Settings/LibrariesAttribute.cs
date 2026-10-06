using System;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// Offers a list of library ids as the server's libraries, by name.
/// </summary>
/// <remarks>
/// The app compares ids, and nobody knows one by heart: the field asked an administrator
/// to find each library's id in its address. The libraries belong to the server rather
/// than to the plugin, so they are sent with the form when it is asked for, the way the
/// notifications page is sent them, and an id no longer among them is kept and shown as
/// it is.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class LibrariesAttribute : Attribute
{
}
