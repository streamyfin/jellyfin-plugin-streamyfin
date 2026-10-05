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

        Assert.Equal(
            expected: "Test empezaron a mirar",
            actual: _helper.GetFormatted(
                key: "UserWatching",
                cultureInfo: CultureInfo.CreateSpecificCulture("es-MX"),
                args: "Test"
            )
        );
    }

    /// <summary>
    /// Every translated resource carries every key the English one does.
    /// </summary>
    /// <remarks>
    /// A missing key is not an error at runtime: the resource manager falls back to
    /// English and the notification goes out in the wrong language, which nobody
    /// reports. The plugin has no translation platform, unlike the app, so the only
    /// thing that can notice is this.
    ///
    /// <para>
    /// Adding a key to Strings.resx and to no other file is what fails here. That is
    /// deliberate: it makes the translation part of the change rather than a follow up
    /// nobody does.
    /// </para>
    /// </remarks>
    [Theory]
    [InlineData("fr")]
    [InlineData("nl")]
    [InlineData("es-MX")]
    public void EveryLocaleCarriesEveryKey(string locale)
    {
        var missing = KeysOf(CultureInfo.InvariantCulture)
            .Except(KeysOf(CultureInfo.GetCultureInfo(locale)))
            .OrderBy(key => key, System.StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missing);
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
    [InlineData("fr")]
    [InlineData("nl")]
    [InlineData("es-MX")]
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
    /// What one resource file declares, as key and value.
    /// </summary>
    private static Dictionary<string, string> ValuesOf(CultureInfo culture)
    {
        var resources = new ResourceManager(
            baseName: "Jellyfin.Plugin.Streamyfin.Resources.Strings",
            assembly: typeof(LocalizationHelper).Assembly);

        var set = resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
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
        var resources = new ResourceManager(
            baseName: "Jellyfin.Plugin.Streamyfin.Resources.Strings",
            assembly: typeof(LocalizationHelper).Assembly);

        // tryParents false, or every culture inherits English and this can never fail.
        var set = resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false);
        Assert.NotNull(set);

        return set.Cast<DictionaryEntry>().Select(entry => (string)entry.Key);
    }
}