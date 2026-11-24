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

        // The French words belong to Crowdin, so the expectation reads them rather than
        // repeating them: what is checked is that fr-FR reaches the French file.
        var french = CultureInfo.GetCultureInfo("fr");
        var sentence = Resources().GetString("UserWatching", french)!;
        Assert.Equal(
            expected: string.Format(french, sentence, "Test"),
            actual: _helper.GetFormatted(
                key: "UserWatching",
                cultureInfo: CultureInfo.CreateSpecificCulture("fr-FR"),
                args: "Test"
            )
        );
    }

    /// <summary>
    /// A translation that cannot be formatted gives way to the English sentence.
    /// </summary>
    /// <remarks>
    /// Translations come from Crowdin, where a stray brace can get through. Throwing would
    /// drop the notification for every device in the batch, whatever their language.
    /// </remarks>
    [Fact]
    public void ATranslationThatCannotBeFormattedFallsBackToEnglish()
    {
        var broken = new BrokenResources("{0} ล้มเหลว: {1}}");
        var helper = new LocalizationHelper(null, null, () => null, broken);

        Assert.Equal(
            "Scan failed: Boom",
            helper.GetFormatted("TaskFailedWithReason", CultureInfo.GetCultureInfo("th"), "Scan", "Boom"));
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
    /// Every file is named after a culture a bare language tag reaches.
    /// </summary>
    /// <remarks>
    /// Devices and the app often send a language without a region, "zh" or "pt", and the
    /// resource manager only walks up from a culture to its parents. A file named after a
    /// region, as Crowdin names a language crowdin.yml does not map yet (id-ID for a new
    /// Indonesian), is reached by nobody who says "id". Brazilian Portuguese is the one
    /// file that has to name its region, next to the European Portuguese of "pt".
    /// </remarks>
    /// <param name="locale">The language.</param>
    [Theory]
    [MemberData(nameof(TranslatedCultures))]
    public void EveryFileIsNamedSoABareTagReachesIt(string locale)
    {
        var culture = CultureInfo.GetCultureInfo(locale);

        Assert.True(
            culture.IsNeutralCulture || locale == "pt-BR",
            $"Strings.{locale}.resx is named after a region. Map the language in crowdin.yml to its neutral culture.");
    }

    /// <summary>
    /// A language as Jellyfin names it reaches the file Crowdin writes for it.
    /// </summary>
    /// <remarks>
    /// The server's language goes through <see cref="CultureInfo.CreateSpecificCulture"/>
    /// and the resource manager then walks up the parents. crowdin.yml names each file
    /// after the culture that walk meets: zh for Simplified Chinese and zh-Hant for
    /// Traditional, so Hong Kong finds Traditional; es rather than es-ES, so Mexico finds
    /// Spanish.
    /// </remarks>
    /// <param name="jellyfin">The language as the server's settings carry it.</param>
    /// <param name="file">The culture of the file it should be answered from.</param>
    [Theory]
    [InlineData("zh-CN", "zh")]
    [InlineData("zh-TW", "zh-Hant")]
    [InlineData("zh-HK", "zh-Hant")]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("pt-PT", "pt")]
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
    /// The app sends the language picked in its settings or the phone's bare language code
    /// ("zh", "pt", "no"), and Android spells a region with an underscore. They go through
    /// <see cref="PushNotifications.DeviceLanguage.CultureOf"/>, as at send time. The app
    /// reads "zh" as Simplified Chinese and "pt" as European Portuguese, which is where
    /// crowdin.yml puts them.
    /// </remarks>
    /// <param name="device">The language tag as a phone or a TV sends it.</param>
    /// <param name="file">The culture of the file it should be answered from.</param>
    [Theory]
    [InlineData("zh", "zh")]
    [InlineData("zh-CN", "zh")]
    [InlineData("zh-Hans-CN", "zh")]
    [InlineData("zh-TW", "zh-Hant")]
    [InlineData("zh_TW", "zh-Hant")]
    [InlineData("zh-Hant-TW", "zh-Hant")]
    [InlineData("pt", "pt")]
    [InlineData("pt_PT", "pt")]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("no", "nb")]
    [InlineData("nb-NO", "nb")]
    [InlineData("es-419", "es")]
    [InlineData("fr_CA", "fr")]
    public void ADeviceLanguageReachesItsTranslation(string device, string file) =>
        Assert.Equal(file, FileFor(PushNotifications.DeviceLanguage.CultureOf(device)!));

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
    /// Every translation formats with the arguments its English sentence is given.
    /// </summary>
    /// <remarks>
    /// The sentence is formatted with as many arguments as the English asks for, so a stray
    /// brace or an index the English does not pass fails here rather than at send time. A
    /// translation may leave a placeholder out, and one whose sentence changed in English
    /// keeps its old wording until Crowdin's next sync: neither is checked, so a pull
    /// request that only edits Strings.resx, as the README asks, does not have to touch the
    /// translated files.
    /// </remarks>
    /// <param name="locale">The language.</param>
    [Theory]
    [MemberData(nameof(TranslatedCultures))]
    public void EveryTranslationFormatsWithTheEnglishArguments(string locale)
    {
        var culture = CultureInfo.GetCultureInfo(locale);
        var english = ValuesOf(CultureInfo.InvariantCulture);
        var failing = new List<string>();

        foreach (var (key, value) in ValuesOf(culture).Where(pair => english.ContainsKey(pair.Key)))
        {
            var arguments = Enumerable.Range(0, ArgumentCount(english[key])).Select(index => (object)$"<{index}>").ToArray();
            try
            {
                _ = string.Format(culture, value, arguments);
            }
            catch (System.FormatException)
            {
                failing.Add(key);
            }
        }

        Assert.Empty(failing);
    }

    /// <summary>
    /// How many arguments a format string is given: one more than its highest index.
    /// </summary>
    private static int ArgumentCount(string value) =>
        Regex.Matches(value, @"\{(\d+)[^}]*\}")
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) + 1)
            .DefaultIfEmpty(0)
            .Max();

    /// <summary>
    /// The plugin's sentences, with every translation replaced by <paramref name="broken"/>.
    /// </summary>
    private sealed class BrokenResources(string broken)
        : ResourceManager("Jellyfin.Plugin.Streamyfin.Resources.Strings", typeof(LocalizationHelper).Assembly)
    {
        public override string? GetString(string name, CultureInfo? culture) =>
            culture is null || culture.Equals(CultureInfo.InvariantCulture) ? base.GetString(name, culture) : broken;
    }

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