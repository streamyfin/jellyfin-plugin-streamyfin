using System;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// Names the toggle a setting only matters under.
/// </summary>
/// <remarks>
/// The admin form greys a dependent setting when its toggle is locked off, and says so
/// in its place, rather than hiding it the way the app hides it from its own users. A
/// dependency is declared only where the app's code was read to confirm it: the tests
/// name each pair and where it was found.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class DependsOnAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DependsOnAttribute"/> class.
    /// </summary>
    /// <param name="key">The key of the toggle this setting depends on.</param>
    public DependsOnAttribute(string key)
    {
        Key = key;
    }

    /// <summary>
    /// Gets the key of the toggle this setting depends on.
    /// </summary>
    public string Key { get; }
}
