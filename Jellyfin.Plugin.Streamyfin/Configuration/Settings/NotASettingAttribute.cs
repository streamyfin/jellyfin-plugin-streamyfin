using System;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// Marks a property of <see cref="Settings"/> that is a shape rather than a setting.
/// </summary>
/// <remarks>
/// The schema is every public property of <see cref="Settings"/>, and everything in it is
/// drawn as a field, resolved level by level and validated on the way in. An integration
/// block is none of those: it mirrors keys that are already all three, and treating it as
/// a setting of its own would give one value two places to be resolved from.
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class NotASettingAttribute : Attribute;
