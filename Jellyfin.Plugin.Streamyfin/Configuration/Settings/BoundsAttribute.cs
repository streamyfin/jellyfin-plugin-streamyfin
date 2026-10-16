using System;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// The lowest and highest value a numeric setting accepts.
/// </summary>
/// <remarks>
/// Not <c>System.ComponentModel.DataAnnotations.Range</c>, which the form used first.
/// ASP.NET validates every DataAnnotations attribute on a body it binds, and the
/// targeting routes bind a partial <see cref="Settings"/>. A <c>Range</c> on a
/// <c>Lockable&lt;int&gt;</c> property is then asked whether the <c>Lockable</c> itself
/// lies between the bounds; it cannot be converted to a number, so the answer is no,
/// and every group or user override that carried a skip time was refused with a
/// message naming the bounds as the reason. Seen on the beta: 400 for a forward skip
/// time of 45.
///
/// <para>
/// This attribute is the plugin's own, so nothing acts on it but the form and, one
/// day, a validator that knows what a <c>Lockable</c> is.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class BoundsAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BoundsAttribute"/> class.
    /// </summary>
    /// <param name="minimum">The lowest accepted value.</param>
    /// <param name="maximum">The highest accepted value.</param>
    public BoundsAttribute(double minimum, double maximum)
    {
        Minimum = minimum;
        Maximum = maximum;
    }

    /// <summary>
    /// Gets the lowest accepted value.
    /// </summary>
    public double Minimum { get; }

    /// <summary>
    /// Gets the highest accepted value.
    /// </summary>
    public double Maximum { get; }
}
