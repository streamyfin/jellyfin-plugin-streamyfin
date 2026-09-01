using System;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// The increment a numeric setting moves by in the admin form.
/// </summary>
/// <remarks>
/// Beside <c>Range</c> rather than folded into it, because the two answer different
/// questions: <c>Range</c> says which values are legal, which a server side validator
/// can act on, and this says how a form should offer them. A skip time accepts any
/// second between zero and sixty and is still more usable stepping by five.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class StepAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="StepAttribute"/> class.
    /// </summary>
    /// <param name="value">The increment.</param>
    public StepAttribute(double value)
    {
        Value = value;
    }

    /// <summary>
    /// Gets the increment.
    /// </summary>
    public double Value { get; }
}
