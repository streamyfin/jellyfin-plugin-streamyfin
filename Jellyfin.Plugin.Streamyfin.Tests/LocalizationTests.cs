using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Resources;
using System.Text.RegularExpressions;
using Xunit;

namespace Jellyfin.Plugin.Streamyfin.Tests;


/// <summary>
/// Ensure resource file is accessed correctly
/// </summary>
public class LocalizationTests
{
    private LocalizationHelper _helper = new(null, null);
    
    /// <summary>
    /// Test to make sure fallback is english resource
    /// </summary>
    [Fact]
    public void TestFallbackResource()
    {
        Assert.Equal(
            expected: "Playback started",
            actual: _helper.GetString("PlaybackStartTitle", CultureInfo.CreateSpecificCulture("ab-AX"))
        );
    }

    /// <summary>
    /// Strings that don't exist should return the key we used
    /// </summary>
    [Fact]
    public void TestKeyThatDoesNotExist()
    {
        Assert.Equal(
            expected: "ThisStringDoesNotExist",
            actual: _helper.GetString("ThisStringDoesNotExist")
        );
    }

    /// <summary>
    /// Every sentence the plugin names exists in the English resources.
    /// </summary>
    /// <remarks>
    /// A missing key does not throw: the helper answers with the key itself, so the
    /// notification goes out reading "SeriesEpisode". That is how an episode with a
    /// number and no season was announced, the resource being named "Series Episode".
    ///
    /// <para>
    /// A key reaches the helper either written in the call or through a variable set a
    /// few lines up, as the Seerr and admin events do. Both are a string literal in a
    /// file that calls the helper, so every such literal shaped like a key is checked:
    /// the literal in the call, and every PascalCase literal of two words or more.
    /// </para>
    /// </remarks>
    [Fact]
    public void EveryKeyTheCodeNamesExists()
    {
        var known = KeysOf(CultureInfo.InvariantCulture).ToHashSet(System.StringComparer.Ordinal);
        var sources = Path.Combine(SourceTree.Root(), "Jellyfin.Plugin.Streamyfin");

        var named = SourceTree.CSharpFiles(sources)
            .Select(File.ReadAllText)
            .Where(text => LocalizationCall.IsMatch(text))
            .SelectMany(text => KeyInCall.Matches(text).Concat(KeyShaped.Matches(text)))
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToArray();

        // The Seerr titles, written only in a switch, prove the second pattern still reaches them.
        Assert.Contains("SeerrRequestDeclinedTitle", named);
        Assert.Empty(named.Where(key => !known.Contains(key)).OrderBy(key => key, System.StringComparer.Ordinal));
    }

    private static readonly Regex LocalizationCall = new(@"\b_?localization\.(?:GetString|GetFormatted)\(");

    private static readonly Regex KeyInCall = new(@"\b_?localization\.(?:GetString|GetFormatted)\(\s*(?:key:\s*)?""([^""]+)""");

    private static readonly Regex KeyShaped = new(@"""([A-Z][a-z0-9]+(?:[A-Z][a-z0-9]*)+)""");
    
    /// <summary>
    /// Test string formats
    /// </summary>
    [Fact]
    public void TestStringFormatLocalization()
    {
        // No culture given: the helper resolves to the invariant culture, which
        // serves the neutral English resource. Before, it deferred to
        // CultureInfo.CurrentUICulture and this assertion failed on any machine
        // whose locale had a translated resx, French for instance.
        Assert.Equal(
            expected: "Test watching",
            actual: _helper.GetFormatted("UserWatching", args: "Test")
        );

        var french = CultureInfo.GetCultureInfo("fr");
        Assert.Equal(
            expected: string.Format(french, ValuesOf(french)["UserWatching"], "Test"),
            actual: _helper.GetFormatted(
                key: "UserWatching",
                cultureInfo: CultureInfo.CreateSpecificCulture("fr-FR"),
                args: "Test"
            )
        );
    }

    /// <summary>
    /// Every culture the plugin ships a translation for, found from the satellite
    /// assemblies rather than listed here, so a language Crowdin adds is checked as well.
    /// </summary>
    public static TheoryData<string> TranslatedCultures()
    {
        var resources = Resources();
        var cultures = new TheoryData<string>();

        foreach (var culture in CultureInfo.GetCultures(CultureTypes.AllCultures)
                     .Where(culture => !culture.Equals(CultureInfo.InvariantCulture))
                     .Where(culture => resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false) is not null)
                     .OrderBy(culture => culture.Name, System.StringComparer.Ordinal))
        {
            cultures.Add(culture.Name);
        }

        return cultures;
    }

    /// <summary>
    /// No translation carries a sentence the English no longer has.
    /// </summary>
    /// <remarks>
    /// The reverse is expected: Crowdin leaves a sentence nobody has translated yet out of
    /// that language's file, and the resource manager answers it in English. A key only
    /// a translation knows is one the English renamed or dropped, which Crowdin removes on
    /// its next sync, so one left here means a file was edited by hand.
    /// </remarks>
    /// <param name="locale">The language.</param>
    [Theory]
    [MemberData(nameof(TranslatedCultures))]
    public void NoLocaleCarriesAKeyTheEnglishLacks(string locale)
    {
        var stale = KeysOf(CultureInfo.GetCultureInfo(locale))
            .Except(KeysOf(CultureInfo.InvariantCulture))
            .OrderBy(key => key, System.StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(stale);
    }

    /// <summary>
    /// A language as Jellyfin names it reaches the file Crowdin writes for it.
    /// </summary>
    /// <remarks>
    /// The server's language goes through <see cref="CultureInfo.CreateSpecificCulture"/>
    /// and the resource manager then walks up the parents. crowdin.yml names each file
    /// after the culture that walk meets: zh-Hans and zh-Hant rather than zh-CN and zh-TW,
    /// so Hong Kong finds Traditional Chinese; es rather than es-ES, so Mexico finds Spanish.
    /// </remarks>
    /// <param name="jellyfin">The language as the server's settings carry it.</param>
    /// <param name="file">The culture of the file it should be answered from.</param>
    [Theory]
    [InlineData("zh-CN", "zh-Hans")]
    [InlineData("zh-TW", "zh-Hant")]
    [InlineData("zh-HK", "zh-Hant")]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("pt-PT", "pt-PT")]
    [InlineData("nb", "nb")]
    [InlineData("es", "es")]
    [InlineData("es-MX", "es")]
    [InlineData("fr-CA", "fr")]
    [InlineData("sv", "sv")]
    public void ALanguageReachesItsTranslation(string jellyfin, string file) =>
        Assert.Equal(file, FileFor(CultureInfo.CreateSpecificCulture(jellyfin)));

    /// <summary>
    /// A device's language, as the app reports it, reaches the file Crowdin writes for it.
    /// </summary>
    /// <remarks>
    /// Notifications are written in each device's language when it gave one, which
    /// <see cref="PushNotifications.DeviceLanguage"/> reads with <see cref="CultureInfo.GetCultureInfo(string)"/>,
    /// so the tags phones send, with a script or a region, have to land on a file as well.
    /// </remarks>
    /// <param name="device">The language tag as a phone or a TV sends it.</param>
    /// <param name="file">The culture of the file it should be answered from.</param>
    [Theory]
    [InlineData("zh-Hans-CN", "zh-Hans")]
    [InlineData("zh-Hant-TW", "zh-Hant")]
    [InlineData("nb-NO", "nb")]
    [InlineData("es-419", "es")]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("fr-CA", "fr")]
    public void ADeviceLanguageReachesItsTranslation(string device, string file) =>
        Assert.Equal(file, FileFor(CultureInfo.GetCultureInfo(device)));

    /// <summary>
    /// The culture whose file answers <paramref name="culture"/>: the resource manager's own
    /// walk up the parents, stopping at the first culture that has a satellite.
    /// </summary>
    private static string FileFor(CultureInfo culture)
    {
        var resources = Resources();

        while (!culture.Equals(CultureInfo.InvariantCulture)
               && resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false) is null)
        {
            culture = culture.Parent;
        }

        return culture.Name;
    }

    /// <summary>
    /// A translation uses the same placeholders as the English it translates.
    /// </summary>
    /// <remarks>
    /// A string is formatted with the arguments its English version asks for. A translation
    /// that drops one shows the others in the wrong places, and one that invents an index
    /// throws at the moment the notification is built, which is inside an event handler.
    /// </remarks>
    /// <param name="locale">The language.</param>
    [Theory]
    [MemberData(nameof(TranslatedCultures))]
    public void EveryLocaleUsesTheSamePlaceholders(string locale)
    {
        var english = ValuesOf(CultureInfo.InvariantCulture);
        var translated = ValuesOf(CultureInfo.GetCultureInfo(locale));

        var differing = english
            .Where(pair => translated.ContainsKey(pair.Key))
            .Where(pair => !Placeholders(pair.Value).SetEquals(Placeholders(translated[pair.Key])))
            .Select(pair => pair.Key)
            .OrderBy(key => key, System.StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(differing);
    }

    /// <summary>
    /// The indexes a format string asks for, such as 0 and 1 in "{0} failed: {1}".
    /// </summary>
    private static HashSet<string> Placeholders(string value) =>
        System.Text.RegularExpressions.Regex.Matches(value, @"\{(\d+)[^}]*\}")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(System.StringComparer.Ordinal);

    /// <summary>
    /// The plugin's sentences, as the helper reads them.
    /// </summary>
    private static ResourceManager Resources() =>
        new(baseName: "Jellyfin.Plugin.Streamyfin.Resources.Strings", assembly: typeof(LocalizationHelper).Assembly);

    /// <summary>
    /// What one resource file declares, as key and value.
    /// </summary>
    private static Dictionary<string, string> ValuesOf(CultureInfo culture)
    {
        var set = Resources().GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        Assert.NotNull(set);

        return set.Cast<DictionaryEntry>()
            .Where(entry => entry.Value is string)
            .ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!, System.StringComparer.Ordinal);
    }

    /// <summary>
    /// What one resource file declares, without what it inherits from its parents.
    /// </summary>
    private static IEnumerable<string> KeysOf(CultureInfo culture)
    {
        // tryParents false, or every culture inherits English and this can never fail.
        var set = Resources().GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        Assert.NotNull(set);

        return set.Cast<DictionaryEntry>().Select(entry => (string)entry.Key);
    }
}