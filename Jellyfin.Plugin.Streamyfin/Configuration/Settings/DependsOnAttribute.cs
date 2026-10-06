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

    /// <summary>
    /// Gets or sets the value the other one has to hold, for a part of a shape that
    /// depends on a choice rather than on a switch being on. <c>null</c> means on.
    /// </summary>
    public string? Value { get; set; }
}
