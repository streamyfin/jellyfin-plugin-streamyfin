using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Jellyfin.Plugin.Streamyfin.Configuration;
using Jellyfin.Plugin.Streamyfin.Configuration.Settings;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// The plugin declares what the app reads, and nothing else.
///
/// A key the plugin does not declare resolves <c>locked</c> to <c>undefined</c> in the
/// app, so the lock never fires and no value is ever pushed. A key the plugin declares
/// under a name the app does not read is worse, because it looks like it works. Both
/// have shipped, which is what <c>docs/rewrite/settings-parity.md</c> records.
/// </summary>
public class SettingsParityTests
{
    /// <summary>
    /// Keys the app reads that the plugin deliberately does not declare, and why.
    /// </summary>
    /// <remarks>
    /// Deleting an entry from here is how a setting comes under administrator control.
    /// A key in neither this list nor <see cref="Settings"/> fails the first test, so
    /// no key can arrive without someone deciding about it.
    /// </remarks>
    private static readonly Dictionary<string, string> NotDeclared = new(StringComparer.Ordinal)
    {
        ["downloadQuality"] =
            "Declared once the app can read it back. The app types it as DownloadOption, "
            + "which is { label, value }, and the generic fallback in normalizePluginValue "
            + "only rebuilds { key, value }, so a value declared today would arrive in a "
            + "shape the app cannot use. Either the plugin sends the scalar and the app "
            + "gains a normalizer case, or DownloadOption gains a key.",
        ["playbackSpeedPerMedia"] =
            "Not a setting. A map the player writes by itself, keyed by item id, so "
            + "there is nothing an administrator could put in it.",
        ["playbackSpeedPerShow"] =
            "Not a setting. A map the player writes by itself, keyed by series id.",
        ["autoPlayEpisodeCount"] =
            "Not a setting. A counter the player keeps by itself, of the episodes it has "
            + "played in a row against maxAutoPlayEpisodeCount; a locked value would stop "
            + "auto play for good or never.",
        ["videoPlayer"] =
            "Picks the engine and the controls for every platform at once: ExoPlayer, which "
            + "Android TV needs for HDR, also moves every iPhone off the native controls. A "
            + "value set for one platform changes the others, so each device keeps its own, "
            + "and the plugin declares the two switches that each name one platform, "
            + "nativeVideoPlayerTV and nativeVideoPlayerAndroidTV.",
        ["deviceProfile"] =
            "Read nowhere. The app builds the device profile from the active player since "
            + "ac9bcbcb (2024-10-15), so a value set here did nothing on any platform.",
        ["mediaListCollectionIds"] =
            "Read nowhere. Its last reader, the large carousel, was unmounted in cc2e6341 "
            + "(2025-09-29, #1098) and deleted in a36a0643 (2026-08-18, #1984).",
        ["usePopularPlugin"] =
            "Read nowhere. Only the large carousel read it, unmounted in cc2e6341 "
            + "(2025-09-29, #1098) and deleted in a36a0643 (2026-08-18, #1984).",
        ["showHomeTitles"] =
            "Read nowhere. It entered the app's Settings type in e173d51d (2024-09-04), and "
            + "no screen has read it since.",
    };

    /// <summary>
    /// Declared defaults that knowingly differ from the app's, and why.
    /// </summary>
    /// <remarks>
    /// This list should stay empty or nearly so. An entry is a promise that someone
    /// weighed the difference, not a place to put a default that turned out to be
    /// inconvenient to fix.
    /// </remarks>
    private static readonly Dictionary<string, string> KnownDisagreements = new(StringComparer.Ordinal);

    /// <summary>
    /// Keys the plugin declares that the app's published branch does not read yet, and
    /// why that is deliberate rather than the mistake of pull request #109.
    /// </summary>
    /// <remarks>
    /// An entry here is a bet that the app change lands. It is safe only while the
    /// plugin ships no default for the key, or ships one the app agrees with once it
    /// catches up, since an unlocked default is applied whether the app understands the
    /// key or not.
    /// </remarks>
    private static readonly Dictionary<string, string> DeclaredAheadOfTheApp = new(StringComparer.Ordinal);

    /// <summary>
    /// Keys the plugin still declares for the copies of the app in the field, which the
    /// app's own branch no longer reads, and why.
    /// </summary>
    /// <remarks>
    /// The other side of <see cref="DeclaredAheadOfTheApp"/>. The jellyseerr keys are in
    /// the manifest only while the app keeps its fallback for plugins older than the
    /// seerr block, and apps older than the block read nothing else. Those are two events:
    /// the day the app drops its fallback, the plugin still owes the keys to the apps
    /// people have installed, and the answer to the failing test is an entry here rather
    /// than deleting them. An entry dies when the plugin stops declaring the key, or when
    /// the app reads it again.
    /// </remarks>
    private static readonly Dictionary<string, string> KeptForAppsInTheField = new(StringComparer.Ordinal);

    /// <summary>
    /// One setting the app reads, as <c>scripts/app-settings-manifest.js</c> writes it.
    /// </summary>
    /// <remarks>
    /// <c>WireNames</c> are the names other than its own that the app reads the setting
    /// under: the flat key it had before a rename, and the field of a block, such as
    /// <c>seerr.serverUrl</c>. The script finds them by running the app's own
    /// <c>readIntegrationBlocks</c>. <c>Options</c> are the values the app offers for a
    /// setting it picks from a list of its own, such as <c>APP_LANGUAGES</c> for the app
    /// language.
    /// </remarks>
    private sealed record ManifestEntry(
        string Key,
        string Type,
        JsonElement Default,
        bool HasDefault,
        string? NoDefaultReason,
        JsonElement WireDefault,
        string? WireNote,
        IReadOnlyList<string>? WireNames = null,
        IReadOnlyList<ManifestChoice>? Options = null);

    /// <summary>
    /// One value the app offers for a setting it picks from a list, under the label its
    /// own picker shows.
    /// </summary>
    private sealed record ManifestChoice(string Value, string Label);

    /// <summary>
    /// What the excuses are checked against.
    /// </summary>
    /// <param name="AppKeys">The app's own keys, which the excuse lists are keyed by.</param>
    /// <param name="KnownNames">Every name the app reads, old names and blocks included.</param>
    /// <param name="Declared">The plugin's properties.</param>
    /// <param name="Served">The app's keys the plugin serves, under any of their names.</param>
    private sealed record Facts(
        IReadOnlySet<string> AppKeys,
        IReadOnlySet<string> KnownNames,
        IReadOnlySet<string> Declared,
        IReadOnlySet<string> Served);

    private static IReadOnlyList<ManifestEntry> Manifest()
    {
        var assembly = typeof(SettingsParityTests).Assembly;
        var resource = assembly
            .GetManifestResourceNames()
            .Single(name => name.EndsWith("AppSettingsManifest.json", StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var reader = new StreamReader(stream);

        return JsonSerializer.Deserialize<List<ManifestEntry>>(
            reader.ReadToEnd(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    /// <summary>
    /// The names of every property <see cref="Settings"/> declares.
    /// </summary>
    private static HashSet<string> DeclaredKeys() =>
        typeof(Settings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

    // Built once: Serves is asked about every name of every setting.
    private static readonly HashSet<string> Declared = DeclaredKeys();

    private static readonly Lazy<JsonSerializerOptions> WireOptions =
        new(() => new SerializationHelper().GetAppJsonSerializerOptions());

    // Every flat setting given a value, then the blocks written out the way the plugin
    // answers, so a block shows which of its fields the plugin actually fills.
    private static readonly Lazy<Settings> Projected = new(() =>
    {
        var settings = new Settings();
        foreach (var property in typeof(Settings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.PropertyType.IsGenericType
                && property.PropertyType.GetGenericTypeDefinition() == typeof(Lockable<>))
            {
                property.SetValue(settings, Activator.CreateInstance(property.PropertyType));
            }
        }

        IntegrationBlocks.Project(settings);
        return settings;
    });

    /// <summary>
    /// A set of names, for the made-up cases the excuse tests run on.
    /// </summary>
    private static HashSet<string> Set(params string[] names) => new(names, StringComparer.Ordinal);

    /// <summary>
    /// The names the app reads one setting under: its own, then its wire names.
    /// </summary>
    private static IEnumerable<string> NamesOf(ManifestEntry entry) =>
        entry.WireNames is null ? [entry.Key] : [entry.Key, .. entry.WireNames];

    /// <summary>
    /// Every name the app reads a setting under, a block counting by its own name.
    /// </summary>
    /// <remarks>
    /// An old name is in here only while the app still reads it. The day the app stops,
    /// the regenerated manifest drops it, and a plugin still declaring it fails
    /// <see cref="EveryKeyThePluginDeclaresIsOneTheAppReads"/> unless it is kept for the
    /// apps in the field.
    /// </remarks>
    private static HashSet<string> KnownNames(IEnumerable<ManifestEntry> manifest) =>
        manifest
            .SelectMany(NamesOf)
            .Select(name => name.Split('.')[0])
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// The app's keys the plugin serves under any of their names.
    /// </summary>
    private static HashSet<string> ServedKeys(IEnumerable<ManifestEntry> manifest) =>
        manifest
            .Where(entry => NamesOf(entry).Any(Serves))
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Whether the plugin serves something under one name the app reads.
    /// </summary>
    /// <remarks>
    /// A flat name is a property of <see cref="Settings"/>. A dotted one is a field of a
    /// block, <c>seerr.serverUrl</c> being <see cref="SeerrSettings.serverUrl"/> on the
    /// <c>seerr</c> property, and it only counts if the block the plugin writes out
    /// carries it: a field on the type that <see cref="IntegrationBlocks.Project"/> never
    /// fills reaches the app as nothing.
    /// </remarks>
    private static bool Serves(string name) =>
        name.Contains('.', StringComparison.Ordinal)
            ? DeclaredAt(Projected.Value, name) is { Exists: true, Value: not null }
            : Declared.Contains(name);

    /// <summary>
    /// What the plugin holds under one name the app reads.
    /// </summary>
    /// <param name="settings">The settings to look in.</param>
    /// <param name="name">A flat name, or a block and one of its fields.</param>
    /// <returns>
    /// Whether the plugin has a property under that name, and what it holds there, which
    /// may be nothing.
    /// </returns>
    private static (bool Exists, object? Value) DeclaredAt(Settings settings, string name)
    {
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        var flat = typeof(Settings).GetProperty(dot < 0 ? name : name[..dot], BindingFlags.Public | BindingFlags.Instance);
        if (flat is null)
        {
            return (false, null);
        }

        if (dot < 0)
        {
            return (true, flat.GetValue(settings));
        }

        var field = flat.PropertyType.GetProperty(name[(dot + 1)..], BindingFlags.Public | BindingFlags.Instance);
        if (field is null)
        {
            return (false, null);
        }

        var block = flat.GetValue(settings);
        return (true, block is null ? null : field.GetValue(block));
    }

    /// <summary>
    /// Every setting the app reads has been decided about: declared, or listed as
    /// deliberately not declared with the reason written down.
    /// </summary>
    /// <remarks>
    /// Declared under any name the app reads it under. The three Seerr settings are
    /// <c>seerrServerUrl</c> and the rest in the app, and the plugin still serves them as
    /// the jellyseerr keys every earlier copy of the app reads, plus the seerr block.
    /// </remarks>
    [Fact]
    public void EverySettingTheAppReadsHasBeenDecidedAbout()
    {
        var undecided = Manifest()
            .Where(entry => !NamesOf(entry).Any(Serves) && !NotDeclared.ContainsKey(entry.Key))
            .Select(entry => entry.Key)
            .ToArray();

        Assert.True(
            undecided.Length == 0,
            "Neither declared nor excused:\n  " + string.Join("\n  ", undecided));
    }

    /// <summary>
    /// Every key the plugin declares is one the app actually reads.
    /// </summary>
    /// <remarks>
    /// This is the one that catches the mistake in pull request #109, where the plugin
    /// declared <c>autoSubtitlesOnMute</c> while the app read <c>subtitlesOnMute</c>. It
    /// shipped two properties nothing reads, and the lock they existed to enable still
    /// did nothing.
    /// </remarks>
    [Fact]
    public void EveryKeyThePluginDeclaresIsOneTheAppReads()
    {
        var known = KnownNames(Manifest());

        var unknown = Declared
            .Where(key => !known.Contains(key)
                && !DeclaredAheadOfTheApp.ContainsKey(key)
                && !KeptForAppsInTheField.ContainsKey(key))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(
            unknown.Length == 0,
            "Declared, but the app reads no such key. One the apps in the field still read "
            + "belongs in KeptForAppsInTheField:\n  " + string.Join("\n  ", unknown));
    }

    /// <summary>
    /// A block the plugin serves carries nothing the app does not read from it.
    /// </summary>
    /// <remarks>
    /// The mistake of #109 one level down. The test above knows a block by its name
    /// only, so a field added to <see cref="SeerrSettings"/> under a name the app does not
    /// read would ship a lock nothing reads.
    /// </remarks>
    [Fact]
    public void ABlockThePluginServesCarriesNothingTheAppDoesNotRead()
    {
        var names = Manifest().SelectMany(NamesOf).ToHashSet(StringComparer.Ordinal);
        var blocks = names
            .Where(name => name.Contains('.', StringComparison.Ordinal))
            .Select(name => name.Split('.')[0])
            .ToHashSet(StringComparer.Ordinal);

        var unread = typeof(Settings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(block => blocks.Contains(block.Name))
            .SelectMany(block => block.PropertyType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(field => $"{block.Name}.{field.Name}"))
            .Where(name => !names.Contains(name))
            .ToArray();

        Assert.True(
            unread.Length == 0,
            "In a block the plugin serves, but the app reads no such field:\n  " + string.Join("\n  ", unread));
    }

    /// <summary>
    /// A block the plugin serves carries every field the app reads from it.
    /// </summary>
    /// <remarks>
    /// The app reads the block first and falls back to the flat keys, which leave the
    /// plugin in its breaking release. A field missing from the block is lost that day.
    /// </remarks>
    [Fact]
    public void ABlockThePluginServesHasEveryFieldTheAppReads()
    {
        var missing = Manifest()
            .SelectMany(NamesOf)
            .Where(name => name.Contains('.', StringComparison.Ordinal))
            .Where(name => Declared.Contains(name.Split('.')[0]) && !Serves(name))
            .ToArray();

        Assert.True(
            missing.Length == 0,
            "Read from a block the plugin serves, but missing from what it writes:\n  " + string.Join("\n  ", missing));
    }

    /// <summary>
    /// A declared default equals the app's own.
    /// </summary>
    /// <remarks>
    /// An unlocked plugin value is applied exactly once as a default, so a disagreement
    /// here does not sit there harmlessly: it silently flips the setting for every user
    /// who has not already chosen one.
    /// </remarks>
    [Fact]
    public void ADeclaredDefaultEqualsTheAppsOwn()
    {
        var settings = PluginConfiguration.DefaultSettings();

        var disagreements = Manifest()
            .Where(entry => !KnownDisagreements.ContainsKey(entry.Key))
            .Select(entry => Disagreement(entry, settings))
            .OfType<string>()
            .ToArray();

        Assert.True(
            disagreements.Length == 0,
            "Defaults that disagree with the app:\n  " + string.Join("\n  ", disagreements));
    }

    /// <summary>
    /// An excused disagreement still disagrees.
    /// </summary>
    /// <remarks>
    /// The excuse skips the comparison for its key, so one left behind after the app
    /// caught up would disable that check quietly and for good. This is the half of
    /// "an excuse must not outlive its reason" that checking the key alone misses: the
    /// key is still there, it is the reason that expired.
    /// </remarks>
    [Fact]
    public void AnExcusedDisagreementStillDisagrees()
    {
        var settings = PluginConfiguration.DefaultSettings();
        var manifest = Manifest();

        var settled = KnownDisagreements.Keys
            .Where(key => manifest.Any(entry => entry.Key == key))
            .Where(key => Disagreement(manifest.Single(entry => entry.Key == key), settings) is null)
            .ToArray();

        Assert.True(
            settled.Length == 0,
            "Excused, but the app and the plugin now agree. Delete the entry:\n  "
            + string.Join("\n  ", settled));
    }

    /// <summary>
    /// How one declared default differs from the app's, or <c>null</c> when it does not.
    /// </summary>
    /// <remarks>
    /// Under every name the plugin declares the setting under, flat or as the field of a
    /// block. Looked up by the app's key alone, a renamed setting finds no property, and
    /// finding none reads as agreement: the three Seerr settings would have passed with
    /// any default at all.
    /// </remarks>
    private static string? Disagreement(ManifestEntry entry, Settings settings) =>
        NamesOf(entry)
            .Select(name => (Name: name, Found: DeclaredAt(settings, name)))
            .Where(one => one.Found.Exists)
            .Select(one => Disagreement(entry, one.Name, one.Found.Value))
            .FirstOrDefault(found => found is not null);

    /// <summary>
    /// How the default declared under one name differs from the app's, or <c>null</c>.
    /// </summary>
    private static string? Disagreement(ManifestEntry entry, string name, object? declared)
    {
        if (declared is null)
        {
            // Declaring no default is always allowed. It means the plugin proposes
            // nothing and the app keeps its own, which is the quiet option.
            return null;
        }

        if (entry.NoDefaultReason == "platform")
        {
            // The app has more than one default for this, chosen by platform. One number
            // here would be pushed to every device and flatten a difference the app
            // makes deliberately.
            return $"{entry.Key}: the app's default varies by platform, so the plugin must declare none";
        }

        if (!entry.HasDefault)
        {
            // The app ships nothing, so there is nothing to disagree with and an
            // administrator's starting value is welcome.
            return null;
        }

        var value = declared.GetType().GetProperty("value")!.GetValue(declared);
        var written = JsonSerializer.Serialize(value, WireOptions.Value);

        // normalizePluginValue reshapes a few keys on the way into the app, so for those
        // the plugin has to send the wire form rather than the stored one.
        var expected = entry.WireNote is null ? entry.Default : entry.WireDefault;

        if (Equivalent(written, expected))
        {
            return null;
        }

        var because = entry.WireNote is null ? string.Empty : $" ({entry.WireNote})";
        var subject = name == entry.Key ? entry.Key : $"{entry.Key}, declared as {name}";
        return $"{subject}: app {Json(expected)}, plugin {written}{because}";
    }

    /// <summary>
    /// A value as the JSON it is, so null, false and a string read the way they travel.
    /// </summary>
    private static string Json(JsonElement element) =>
        element.ValueKind == JsonValueKind.Undefined ? "nothing" : element.GetRawText();

    /// <summary>
    /// A disagreement is only excused while the key it names still exists.
    /// </summary>
    /// <remarks>
    /// Without this, an excuse outlives the property it was written for and the next
    /// person reads a reason for something that is no longer there.
    /// </remarks>
    [Fact]
    public void EveryExcusedDisagreementNamesASettingThatExists()
    {
        var manifest = Manifest();

        var stale = StaleExcuses(
            new Facts(
                manifest.Select(entry => entry.Key).ToHashSet(StringComparer.Ordinal),
                KnownNames(manifest),
                Declared,
                ServedKeys(manifest)),
            NotDeclared.Keys,
            KnownDisagreements.Keys,
            DeclaredAheadOfTheApp.Keys,
            KeptForAppsInTheField.Keys);

        Assert.True(
            stale.Length == 0,
            "Excused, but no such setting exists:\n  " + string.Join("\n  ", stale));
    }

    /// <summary>
    /// The excuses that no longer name a setting in the state they were written for.
    /// </summary>
    /// <remarks>
    /// Taken apart from the lists it reads so the cases it exists to catch can be shown
    /// on made-up sets. On the real ones every case is, by construction, absent.
    /// </remarks>
    private static string[] StaleExcuses(
        Facts facts,
        IEnumerable<string> notDeclared,
        IEnumerable<string> knownDisagreements,
        IEnumerable<string> declaredAheadOfTheApp,
        IEnumerable<string> keptForAppsInTheField) =>
        // Both directions for a not-declared key: the app dropped it, or the plugin
        // serves it after all, under any of its names, and the reason has been acted on.
        notDeclared.Where(key => !facts.AppKeys.Contains(key) || facts.Served.Contains(key))
            // The rules look an excuse up by the app's own key, so one written under an
            // old name or a block excuses nothing, and is reported here instead.
            .Concat(knownDisagreements.Where(key => !facts.AppKeys.Contains(key)))
            // Both directions for an ahead-of-the-app key: the plugin dropped it, or the
            // app caught up and it is no longer ahead of anything.
            .Concat(declaredAheadOfTheApp.Where(key => !facts.Declared.Contains(key) || facts.KnownNames.Contains(key)))
            // And for a key kept for the apps in the field: the plugin dropped it, or the
            // app reads it again.
            .Concat(keptForAppsInTheField.Where(key => !facts.Declared.Contains(key) || facts.KnownNames.Contains(key)))
            .ToArray();

    /// <summary>
    /// A setting that gets declared retires the entry saying it would not be.
    /// </summary>
    /// <remarks>
    /// Checking only that the key still exists misses this: <c>downloadQuality</c> is
    /// excused today because the app cannot read it back. The day the app moves and the
    /// property is written, the reason is spent, and an entry left behind covers the key
    /// again the moment the declaration is removed.
    /// </remarks>
    [Fact]
    public void ANotDeclaredEntryDiesWhenItsSettingGetsDeclared()
    {
        var known = Set("downloadQuality");
        var notDeclared = new[] { "downloadQuality" };

        Assert.Empty(StaleExcuses(new Facts(known, known, Set(), Set()), notDeclared, [], [], []));

        Assert.Equal(
            new[] { "downloadQuality" },
            StaleExcuses(new Facts(known, known, Set("downloadQuality"), Set("downloadQuality")), notDeclared, [], [], []));
    }

    /// <summary>
    /// A setting counts as served when the plugin serves it under another name.
    /// </summary>
    /// <remarks>
    /// <c>seerrApiKey</c> is a key the plugin has no property for, and serves all the same
    /// as <c>jellyseerrApiKey</c> and <c>seerr.apiKey</c>. Checked on the app's key alone, a
    /// not-declared entry for it would never retire.
    /// </remarks>
    [Fact]
    public void ASettingServedUnderAnotherNameCountsAsServed()
    {
        Assert.DoesNotContain("seerrApiKey", Declared);
        Assert.Contains("seerrApiKey", ServedKeys(Manifest()));
    }

    /// <summary>
    /// An excuse for a key the app stopped reading is stale whichever list it sits in.
    /// </summary>
    [Fact]
    public void AnExcuseForAKeyTheAppDroppedIsStale()
    {
        var gone = new[] { "settingTheAppRemoved" };
        var nothing = new Facts(Set(), Set(), Set(), Set());

        Assert.Equal(gone, StaleExcuses(nothing, gone, [], [], []));
        Assert.Equal(gone, StaleExcuses(nothing, [], gone, [], []));
    }

    /// <summary>
    /// An excuse written under one of the app's other names excuses nothing, and says so.
    /// </summary>
    /// <remarks>
    /// The comparison names <c>autoLoginSeerr, declared as autoLoginJellyseerr</c>, so the
    /// second name is an easy one to write an excuse under. The rules look excuses up by
    /// the app's key, so it would suppress nothing and never be reported either.
    /// </remarks>
    [Fact]
    public void AnExcuseUnderAnOldNameIsStale()
    {
        var facts = new Facts(
            Set("autoLoginSeerr"),
            Set("autoLoginSeerr", "autoLoginJellyseerr", "seerr"),
            Set("autoLoginJellyseerr", "seerr"),
            Set("autoLoginSeerr"));
        var oldName = new[] { "autoLoginJellyseerr" };

        Assert.Equal(oldName, StaleExcuses(facts, oldName, [], [], []));
        Assert.Equal(oldName, StaleExcuses(facts, [], oldName, [], []));
    }

    /// <summary>
    /// A key declared ahead of the app is stale from both sides.
    /// </summary>
    /// <remarks>
    /// This is what retired <c>subtitlesOnMuteAllowRestart</c>: the plugin still declared
    /// it, and the app caught up, so it was no longer ahead of anything.
    /// </remarks>
    [Fact]
    public void AKeyDeclaredAheadOfTheAppDiesFromEitherSide()
    {
        var ahead = new[] { "subtitlesOnMuteAllowRestart" };
        var declared = Set("subtitlesOnMuteAllowRestart");

        // Still ahead: declared here, unknown to the app.
        Assert.Empty(StaleExcuses(new Facts(Set(), Set(), declared, Set()), [], [], ahead, []));

        // The app caught up.
        Assert.Equal(ahead, StaleExcuses(new Facts(declared, declared, declared, Set()), [], [], ahead, []));

        // The plugin dropped the property.
        Assert.Equal(ahead, StaleExcuses(new Facts(Set(), Set(), Set(), Set()), [], [], ahead, []));
    }

    /// <summary>
    /// A key kept for the apps in the field is stale from both sides.
    /// </summary>
    [Fact]
    public void AKeyKeptForTheAppsInTheFieldDiesFromEitherSide()
    {
        var kept = new[] { "jellyseerrServerUrl" };
        var declared = Set("jellyseerrServerUrl");

        // Still owed to the field: declared here, no longer read by the app's branch.
        Assert.Empty(StaleExcuses(new Facts(Set(), Set(), declared, Set()), [], [], [], kept));

        // The app reads it again.
        Assert.Equal(kept, StaleExcuses(new Facts(Set(), declared, declared, Set()), [], [], [], kept));

        // The plugin dropped the property.
        Assert.Equal(kept, StaleExcuses(new Facts(Set(), Set(), Set(), Set()), [], [], [], kept));
    }

    /// <summary>
    /// A setting the app renamed still has its default compared, through the name the
    /// plugin declares it under.
    /// </summary>
    [Fact]
    public void ARenamedSettingStillHasItsDefaultCompared()
    {
        using var app = JsonDocument.Parse("false");
        var renamed = new ManifestEntry(
            "autoLoginSeerr",
            "boolean",
            app.RootElement.Clone(),
            HasDefault: true,
            NoDefaultReason: null,
            WireDefault: default,
            WireNote: null,
            WireNames: ["autoLoginJellyseerr", "seerr.autoLogin"]);

        Assert.Equal(
            "autoLoginSeerr, declared as autoLoginJellyseerr: app false, plugin true",
            Disagreement(renamed, PluginConfiguration.DefaultSettings()));
    }

    /// <summary>
    /// A setting declared only as the field of a block still has its default compared.
    /// </summary>
    /// <remarks>
    /// The state the plugin is headed for once the jellyseerr keys go: Seerr served as the
    /// block alone. Looked up among the flat properties only, its default would be
    /// skipped.
    /// </remarks>
    [Fact]
    public void ADefaultDeclaredInABlockIsCompared()
    {
        using var app = JsonDocument.Parse("true");
        var autoLogin = new ManifestEntry(
            "autoLoginSeerr",
            "boolean",
            app.RootElement.Clone(),
            HasDefault: true,
            NoDefaultReason: null,
            WireDefault: default,
            WireNote: null,
            WireNames: ["autoLoginJellyseerr", "seerr.autoLogin"]);
        var blockOnly = new Settings { seerr = new SeerrSettings { autoLogin = new() { value = false } } };

        Assert.Equal(
            "autoLoginSeerr, declared as seerr.autoLogin: app true, plugin false",
            Disagreement(autoLogin, blockOnly));
    }

    /// <summary>
    /// An old name counts as one the app reads only while the manifest says so.
    /// </summary>
    /// <remarks>
    /// The manifest is regenerated from the app, so the day the app stops reading the
    /// jellyseerr keys they leave the manifest, and a plugin still declaring them fails
    /// unless they are kept for the apps in the field.
    /// </remarks>
    [Fact]
    public void AnOldNameCountsOnlyWhileTheAppStillReadsIt()
    {
        var renamed = new ManifestEntry(
            "seerrServerUrl",
            "string",
            default,
            HasDefault: false,
            NoDefaultReason: "none",
            WireDefault: default,
            WireNote: null,
            WireNames: ["jellyseerrServerUrl", "seerr.serverUrl"]);

        Assert.Contains("jellyseerrServerUrl", KnownNames([renamed]));
        Assert.Contains("seerr", KnownNames([renamed]));

        var dropped = renamed with { WireNames = null };

        Assert.DoesNotContain("jellyseerrServerUrl", KnownNames([dropped]));
        Assert.DoesNotContain("seerr", KnownNames([dropped]));
    }

    /// <summary>
    /// A dotted name is a field of a block the plugin fills.
    /// </summary>
    [Fact]
    public void ADottedNameIsAFieldOfABlockThePluginFills()
    {
        Assert.True(Serves("seerr.serverUrl"));
        Assert.False(Serves("seerr.notAField"));
        Assert.False(Serves("notABlock.serverUrl"));
        Assert.True(Serves("jellyseerrServerUrl"));

        // A field the block has but holds nothing in reads as nothing.
        Assert.Equal((true, null), DeclaredAt(new Settings { seerr = new SeerrSettings() }, "seerr.serverUrl"));
    }

    /// <summary>
    /// A setting the app picks from a list of its own is offered from the same list here:
    /// the same values, under the names the app's picker gives them.
    /// </summary>
    /// <remarks>
    /// The dropdown for the app language holds a copy of the app's list, and a copy drifts.
    /// A language the app adds could not be picked, and one it drops would still be offered
    /// and then ignored by every device. The manifest records the app's list, so the copy
    /// fails here instead.
    /// </remarks>
    [Fact]
    public void ASettingTheAppPicksFromAListOffersTheAppsList()
    {
        var drift = Manifest()
            .Where(entry => entry.Options is not null)
            .SelectMany(entry => OfferedFor(entry) is { } offered
                ? ListDrift(entry.Key, entry.Options!, offered)
                : [$"{entry.Key}: the app offers {entry.Options!.Count} values, and the plugin takes free text"])
            .ToArray();

        Assert.True(
            drift.Length == 0,
            "Offered differently from the app:\n  " + string.Join("\n  ", drift));
    }

    /// <summary>
    /// Every list the plugin offers is one the app offers, so no dropdown holds values the
    /// plugin made up.
    /// </summary>
    [Fact]
    public void EveryListThePluginOffersIsTheApps()
    {
        var listed = Manifest()
            .Where(entry => entry.Options is not null)
            .SelectMany(NamesOf)
            .ToHashSet(StringComparer.Ordinal);

        var invented = typeof(Settings)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.GetCustomAttribute<ChoicesAttribute>() is not null)
            .Select(property => property.Name)
            .Where(name => !listed.Contains(name))
            .ToArray();

        Assert.True(
            invented.Length == 0,
            "Offered from a list the manifest does not record from the app:\n  " + string.Join("\n  ", invented));
    }

    /// <summary>
    /// A list that drifted says which value moved and which way, whatever the order.
    /// </summary>
    /// <remarks>
    /// On made-up lists, since on the real ones every case is, by construction, absent. The
    /// choice that stores nothing is the device's own language, which the app offers by
    /// storing nothing too, so it is no value to compare.
    /// </remarks>
    [Fact]
    public void ADriftingListSaysWhichValueMoved()
    {
        var app = new[] { new ManifestChoice("de", "Deutsch"), new ManifestChoice("fr", "Français") };

        Assert.Equal(
            new[]
            {
                "preferedLanguage: the app offers de (Deutsch), the plugin does not",
                "preferedLanguage: the plugin offers en (English), the app does not",
                "preferedLanguage: the app names fr Français, the plugin Francais",
                "preferedLanguage: the plugin offers en twice",
            },
            ListDrift(
                "preferedLanguage",
                app,
                [AppLanguages.DeviceLanguage, new("fr", "Francais"), new("en", "English"), new("en", "English")]));

        Assert.Empty(ListDrift(
            "preferedLanguage",
            app,
            [new("fr", "Français"), AppLanguages.DeviceLanguage, new("de", "Deutsch")]));
    }

    /// <summary>
    /// The list the plugin offers for a setting, under any name it declares the setting
    /// under, or <c>null</c> when it takes free text.
    /// </summary>
    private static IReadOnlyList<SettingsChoice>? OfferedFor(ManifestEntry entry) =>
        NamesOf(entry)
            .Select(name => typeof(Settings).GetProperty(name, BindingFlags.Public | BindingFlags.Instance))
            .Select(property => property?.GetCustomAttribute<ChoicesAttribute>())
            .FirstOrDefault(listed => listed is not null)
            ?.Choices;

    /// <summary>
    /// How the values one setting offers here differ from the app's: a value on one side
    /// only, a value named differently, a value offered twice.
    /// </summary>
    private static string[] ListDrift(string key, IEnumerable<ManifestChoice> app, IEnumerable<SettingsChoice> offered)
    {
        var theirs = app.ToDictionary(choice => choice.Value, choice => choice.Label, StringComparer.Ordinal);
        var ours = new Dictionary<string, string>(StringComparer.Ordinal);
        var twice = new List<string>();

        foreach (var choice in offered.Where(choice => choice.Value is not null))
        {
            if (!ours.TryAdd(choice.Value!, choice.Label))
            {
                twice.Add($"{key}: the plugin offers {choice.Value} twice");
            }
        }

        return theirs.Keys.Except(ours.Keys).Order(StringComparer.Ordinal)
            .Select(value => $"{key}: the app offers {value} ({theirs[value]}), the plugin does not")
            .Concat(ours.Keys.Except(theirs.Keys).Order(StringComparer.Ordinal)
                .Select(value => $"{key}: the plugin offers {value} ({ours[value]}), the app does not"))
            .Concat(ours.Keys.Intersect(theirs.Keys).Order(StringComparer.Ordinal)
                .Where(value => !string.Equals(ours[value], theirs[value], StringComparison.Ordinal))
                .Select(value => $"{key}: the app names {value} {theirs[value]}, the plugin {ours[value]}"))
            .Concat(twice)
            .ToArray();
    }

    /// <summary>
    /// An enum reaches the app under the string the app compares against.
    /// </summary>
    /// <remarks>
    /// Three of these cannot be spelled as a C# member name: <c>5.1</c> and
    /// <c>gpu-next</c> are not identifiers, and <c>default</c> is a keyword. Renaming a
    /// member to something legal without saying so on the wire is silent: the build
    /// passes, the app receives a string it has no case for, and the setting does
    /// nothing.
    /// </remarks>
    [Theory]
    [InlineData(AudioTranscodeMode.Auto, "\"auto\"")]
    [InlineData(AudioTranscodeMode.ForceStereo, "\"stereo\"")]
    [InlineData(AudioTranscodeMode.Allow51, "\"5.1\"")]
    [InlineData(AudioTranscodeMode.AllowAll, "\"passthrough\"")]
    [InlineData(MpvVoDriver.GpuNext, "\"gpu-next\"")]
    [InlineData(MpvVoDriver.Gpu, "\"gpu\"")]
    [InlineData(MpvCacheMode.Auto, "\"auto\"")]
    [InlineData(MpvCacheMode.Yes, "\"yes\"")]
    [InlineData(MpvCacheMode.No, "\"no\"")]
    [InlineData(TVTypographyScale.Small, "\"small\"")]
    [InlineData(TVTypographyScale.Default, "\"default\"")]
    [InlineData(TVTypographyScale.Large, "\"large\"")]
    [InlineData(TVTypographyScale.ExtraLarge, "\"extraLarge\"")]
    [InlineData(DownloadQuality.Original, "\"original\"")]
    [InlineData(DownloadQuality.High, "\"high\"")]
    [InlineData(DownloadQuality.Low, "\"low\"")]
    [InlineData(SubtitleAlignX.Left, "\"left\"")]
    [InlineData(SubtitleAlignX.Center, "\"center\"")]
    [InlineData(SubtitleAlignY.Bottom, "\"bottom\"")]
    [InlineData(SubtitleAlignY.Top, "\"top\"")]
    // The app compares the SDK's strings for this one. It was written as a number until
    // the regenerated manifest showed it, and a locked mode did nothing.
    [InlineData(SubtitlePlaybackMode.Default, "\"Default\"")]
    [InlineData(SubtitlePlaybackMode.Always, "\"Always\"")]
    [InlineData(SubtitlePlaybackMode.OnlyForced, "\"OnlyForced\"")]
    [InlineData(SubtitlePlaybackMode.None, "\"None\"")]
    [InlineData(SubtitlePlaybackMode.Smart, "\"Smart\"")]
    public void AnEnumReachesTheAppUnderTheStringTheAppCompares(object member, string expected)
    {
        var written = JsonSerializer.Serialize(
            member,
            member.GetType(),
            new SerializationHelper().GetAppJsonSerializerOptions());

        Assert.Equal(expected, written);
    }

    /// <summary>
    /// The inactivity timeout, which the app compares as a number, is written as one.
    /// </summary>
    /// <remarks>
    /// Same reason <c>OrientationLock</c> and <c>Bitrate</c> already have a number converter
    /// registered. The default is the member name, and a name where the app switches on a
    /// number matches nothing.
    /// </remarks>
    [Theory]
    [InlineData(InactivityTimeout.Disabled, "0")]
    [InlineData(InactivityTimeout.OneMinute, "60000")]
    [InlineData(InactivityTimeout.FiveMinutes, "300000")]
    [InlineData(InactivityTimeout.TwentyFourHours, "86400000")]
    public void AnEnumTheAppComparesAsANumberIsWrittenAsANumber(object member, string expected)
    {
        var written = JsonSerializer.Serialize(
            member,
            member.GetType(),
            new SerializationHelper().GetAppJsonSerializerOptions());

        Assert.Equal(expected, written);
    }

    // Through the serializer both sides use. Comparing CLR values would pass for an
    // enum written as a number where the app expects its name.
    private static bool Equivalent(string written, JsonElement expected)
    {
        using var document = JsonDocument.Parse(written);
        var actual = document.RootElement;

        // 2 and 2.0 are the same JSON number and the app parses JSON, so comparing the
        // text would fail on a C# double that happens to serialize without a fraction.
        if (actual.ValueKind == JsonValueKind.Number && expected.ValueKind == JsonValueKind.Number)
        {
            return actual.GetDouble() == expected.GetDouble();
        }

        return JsonSerializer.Serialize(actual) == JsonSerializer.Serialize(expected);
    }
}
