using System;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// Marks a setting whose value has to be a whole http or https address.
/// </summary>
/// <remarks>
/// Apart from <see cref="ProbeAttribute"/>, which says which service answers at the
/// other end. Those are two facts: a webhook target or a documentation link is an
/// address with nothing to ask, and a setting should not lose its shape check the day
/// its service becomes unprobeable. <c>[Probe]</c> implies this one, so the three
/// addresses that have both declare it once.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class WebAddressAttribute : Attribute;
