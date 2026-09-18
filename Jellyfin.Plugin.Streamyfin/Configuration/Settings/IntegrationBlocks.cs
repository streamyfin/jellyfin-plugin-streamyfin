using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Settings;

/// <summary>
/// Seerr, as a block rather than as three keys spelt jellyseerr.
/// </summary>
/// <remarks>
/// A shape, not a place things are stored. The flat keys stay the one truth: they carry
/// the locks, the targeting levels, the form fields and the validation, and duplicating
/// that machinery would be two truths to keep agreeing. This is read on the way in and
/// written on the way out.
/// </remarks>
public class SeerrSettings
{
    /// <summary>Gets or sets the address, as <c>jellyseerrServerUrl</c> holds it.</summary>
    [Display(Name = "Server URL", Description = "The same setting as jellyseerrServerUrl")]
    public Lockable<string>? serverUrl { get; set; }

    /// <summary>Gets or sets the admin key, as <c>jellyseerrApiKey</c> holds it.</summary>
    [Display(Name = "API key", Description = "The same setting as jellyseerrApiKey")]
    public Lockable<string>? apiKey { get; set; }

    /// <summary>Gets or sets whether to sign in without asking, as <c>autoLoginJellyseerr</c> holds it.</summary>
    [Display(Name = "Sign in automatically", Description = "The same setting as autoLoginJellyseerr")]
    public Lockable<bool>? autoLogin { get; set; }
}

/// <summary>
/// Reads the integration blocks on the way in and writes them on the way out (P6.1).
/// </summary>
/// <remarks>
/// <para>
/// Seerr was renamed from Jellyseerr, and the keys were not: every copy of the app in the
/// field reads <c>jellyseerrServerUrl</c> by name, so a plugin that wrote the other
/// spelling would take Seerr away from everyone who had not updated. #159 made the new
/// spelling readable as an alias, which is a spelling. A block is a shape, and no alias
/// makes an app that reads a flat key find a nested one.
/// </para>
/// <para>
/// So both are served for now. An administrator may write either, the plugin answers with
/// both, the app moves when it is ready, and the flat keys come out the day it has.
/// </para>
/// </remarks>
public static class IntegrationBlocks
{
    private static readonly (string Block, string Flat, Func<SeerrSettings, object?> FromBlock, Func<Settings, object?> FromFlat)[] _seerr =
    [
        ("seerr.serverUrl", "jellyseerrServerUrl", block => block.serverUrl, settings => settings.jellyseerrServerUrl),
        ("seerr.apiKey", "jellyseerrApiKey", block => block.apiKey, settings => settings.jellyseerrApiKey),
        ("seerr.autoLogin", "autoLoginJellyseerr", block => block.autoLogin, settings => settings.autoLoginJellyseerr)
    ];

    /// <summary>
    /// Copies what a block says onto the keys everything else reads.
    /// </summary>
    /// <param name="settings">The settings, changed in place.</param>
    /// <remarks>
    /// Only where the block says something. A block that names one of the three leaves
    /// the other two alone, which is what a level overriding one setting means.
    /// </remarks>
    public static void Fold(Settings? settings)
    {
        if (settings?.seerr is not { } block)
        {
            return;
        }

        settings.jellyseerrServerUrl = block.serverUrl ?? settings.jellyseerrServerUrl;
        settings.jellyseerrApiKey = block.apiKey ?? settings.jellyseerrApiKey;
        settings.autoLoginJellyseerr = block.autoLogin ?? settings.autoLoginJellyseerr;
    }

    /// <summary>
    /// Fills the block from the keys everything else reads.
    /// </summary>
    /// <param name="settings">The settings, changed in place.</param>
    /// <remarks>
    /// A server that says nothing about Seerr serves no block at all, rather than an
    /// empty one, which reads as an opinion about a setting nobody set.
    /// </remarks>
    public static void Project(Settings? settings)
    {
        if (settings is null)
        {
            return;
        }

        if (settings.jellyseerrServerUrl is null
            && settings.jellyseerrApiKey is null
            && settings.autoLoginJellyseerr is null)
        {
            settings.seerr = null;
            return;
        }

        // Copies, not the same objects. The resolver carries each level's own Lockable
        // into what it hands back, and the first level is the plugin's live
        // configuration, so a block sharing those would be a second handle on what the
        // server holds.
        settings.seerr = new SeerrSettings
        {
            serverUrl = Copy(settings.jellyseerrServerUrl),
            apiKey = Copy(settings.jellyseerrApiKey),
            autoLogin = Copy(settings.autoLoginJellyseerr)
        };
    }

    /// <summary>
    /// The reason a document cannot be stored when it writes one setting twice and
    /// disagrees with itself.
    /// </summary>
    /// <param name="settings">The settings as they were written.</param>
    /// <returns>The message to refuse with, or <c>null</c> when there is no argument.</returns>
    /// <remarks>
    /// Refused rather than resolved by precedence: an administrator who wrote both meant
    /// one of them, and a rule about which spelling wins is a rule nobody would remember.
    /// </remarks>
    public static string? Disagreement(Settings? settings)
    {
        if (settings?.seerr is not { } block)
        {
            return null;
        }

        var problems = _seerr
            .Select(one => (one.Block, one.Flat, FromBlock: one.FromBlock(block), FromFlat: one.FromFlat(settings)))
            .Where(one => one.FromBlock is not null && one.FromFlat is not null && !Same(one.FromBlock, one.FromFlat))
            .Select(one => $"{one.Block} and {one.Flat} are the same setting and say different things.")
            .ToList();

        return problems.Count == 0 ? null : string.Join(" ", problems);
    }

    private static Lockable<T>? Copy<T>(Lockable<T>? one) =>
        one is null ? null : new Lockable<T> { value = one.value, locked = one.locked };

    // Two Lockable<T> say the same thing when both the value and the lock match. Compared
    // as values rather than as text: rendering them made null and the empty string the
    // same thing, so a document that set one shape to nothing and the other to "" was
    // accepted and the block then won, quietly.
    private static bool Same(object? left, object? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return Equals(Held(left, "value"), Held(right, "value"))
            && Equals(Held(left, "locked"), Held(right, "locked"));
    }

    private static object? Held(object lockable, string name) =>
        lockable.GetType().GetProperty(name)?.GetValue(lockable);
}
