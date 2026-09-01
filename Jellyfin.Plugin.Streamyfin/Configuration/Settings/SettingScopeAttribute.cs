using System;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// Where a setting belongs in the generated admin form.
/// </summary>
/// <remarks>
/// The categories are the app's own, read from the settings pages a user navigates
/// rather than invented here, so an administrator deciding what to lock sees the same
/// arrangement as the person who will live with it. A form that groups settings its own
/// way makes the administrator translate between two vocabularies for no gain.
///
/// <para>
/// <see cref="Group"/> subdivides the larger categories. The app puts twenty six
/// settings on its playback page, which reads as a wall in a form that shows every
/// platform at once rather than only the device in your hand.
/// </para>
///
/// <para>
/// One deliberate departure from the app's arrangement: it shows <c>videoPlayer</c> on
/// the playback page and the two native player toggles on the TV screen, because a user
/// only ever sees their own device. An administrator configures every platform in one
/// sitting, so the three settings that decide which player runs are kept together.
/// </para>
///
/// <para>
/// Which platforms each setting actually reaches is a separate question, and a real one:
/// locking a phone-only setting on a TV-only fleet changes nothing, and the form cannot
/// say so yet. It is not recorded here because the derivation is only sound for the
/// settings the app exposes on a settings screen, and guessing the rest would put a
/// wrong claim in front of an administrator. It gets its own pass.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property)]
public sealed class SettingScopeAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SettingScopeAttribute"/> class.
    /// </summary>
    /// <param name="category">The section of the form, matching the app's own.</param>
    public SettingScopeAttribute(string category)
    {
        Category = category;
    }

    /// <summary>
    /// Gets the section of the form this setting belongs to.
    /// </summary>
    public string Category { get; }

    /// <summary>
    /// Gets or sets the subdivision within the category, for the categories large enough
    /// to need one. Null leaves the setting directly under its category.
    /// </summary>
    public string? Group { get; set; }
}
