using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// Flattens the targeting levels into the one set of settings a caller receives.
/// </summary>
/// <remarks>
/// Three levels, least specific first: what the server declares for everyone, then
/// the groups the caller belongs to, then anything targeted at the caller alone.
/// Each level is a <see cref="Settings"/> with only the keys it means to say
/// something about filled in, which works because every property is nullable.
///
/// <para>
/// The most specific level that sets a key wins, and that includes the lock. It is
/// not "the most restrictive lock wins": the shape the maintainers proposed in
/// issue #29 has an override setting <c>lock: false</c> to hand a setting back to
/// named users, and a resolver that could only tighten would make that impossible.
/// </para>
///
/// <para>
/// A level sets a key or it does not. There is no way to override the lock while
/// leaving the value alone, because <see cref="Lockable{T}"/> carries both and an
/// absent value is indistinguishable from <c>false</c> or <c>0</c>. That matches
/// the proposal, where every override states both.
/// </para>
/// </remarks>
public static class SettingsResolver
{
    /// <summary>
    /// Resolves the levels into one set of settings.
    /// </summary>
    /// <param name="levels">The levels, least specific first. Nulls are skipped.</param>
    /// <returns>
    /// A new <see cref="Settings"/> holding, for each key, the value from the most
    /// specific level that set it. A key no level sets stays null, which the client
    /// reads as "the server has no opinion".
    /// </returns>
    public static Settings Resolve(params Settings?[] levels)
    {
        var resolved = new Settings();

        if (levels is null)
        {
            return resolved;
        }

        var present = levels.Where(level => level is not null).ToList();

        foreach (var descriptor in SettingsSchema.Descriptors)
        {
            // Walk from the most specific level back, and stop at the first one that
            // has something to say about this key.
            for (var i = present.Count - 1; i >= 0; i--)
            {
                var value = descriptor.Property.GetValue(present[i]);
                if (value is null)
                {
                    continue;
                }

                descriptor.Property.SetValue(resolved, value);
                break;
            }
        }

        return resolved;
    }

    /// <summary>
    /// Removes the settings that hold a credential.
    /// </summary>
    /// <param name="settings">The settings to redact. Not modified.</param>
    /// <returns>A copy with every key marked <see cref="SecretAttribute"/> left unset.</returns>
    /// <remarks>
    /// A redacted key is absent rather than blanked, so a client cannot tell an
    /// administrator who cleared the field from a user who is not allowed to see it,
    /// and neither can it push an empty string back as if it were the real value.
    /// </remarks>
    public static Settings Redact(Settings? settings)
    {
        var redacted = Resolve(settings);

        foreach (var secret in SettingsSchema.Secrets)
        {
            secret.Property.SetValue(redacted, null);
        }

        return redacted;
    }

    /// <summary>
    /// Which of a caller's groups apply, in the order they should be layered.
    /// </summary>
    /// <param name="groups">The groups the caller belongs to.</param>
    /// <returns>The same groups, least specific first.</returns>
    /// <remarks>
    /// A higher priority wins, so it is layered later. Two groups on the same
    /// priority are ordered by id, which is arbitrary but stable: the same caller in
    /// the same groups always resolves to the same answer, rather than to whatever
    /// the database happened to return first.
    /// </remarks>
    public static IReadOnlyList<T> InLayerOrder<T>(IEnumerable<T> groups)
        where T : Db.SettingsGroup =>
        groups is null
            ? []
            : groups.OrderBy(g => g.Priority).ThenBy(g => g.Id).ToList();
}
