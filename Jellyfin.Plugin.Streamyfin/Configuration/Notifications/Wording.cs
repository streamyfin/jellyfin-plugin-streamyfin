using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Jellyfin.Plugin.Streamyfin.Configuration.Notifications;

/// <summary>
/// One sentence written differently from the way the plugin writes it.
/// </summary>
/// <remarks>
/// Issue #34 asks to change the title and the body of a notification, and was answered
/// with a real constraint: the webhook plugin waits for an item's metadata before it
/// fires, while this one waits for the opposite reason, to say "eight episodes" rather
/// than eight times "one episode", and therefore has far less to interpolate. So this is
/// not templating. It is the sentence, with the placeholders the sentence already has,
/// and nothing new to fill them with.
/// </remarks>
public class WordingOverride
{
    /// <summary>
    /// Gets or sets which sentence this replaces, by the key it is stored under.
    /// </summary>
    [Required]
    [Display(Name = "Sentence", Description = "Which sentence to write differently.")]
    [JsonPropertyName(name: "key")]
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the language this is for, as a tag such as <c>fr</c> or
    /// <c>fr-CA</c>, or nothing for every language.
    /// </summary>
    [Display(Name = "Language", Description = "The language this wording is for, such as fr or fr-CA. Leave it out for every language.")]
    [JsonPropertyName(name: "locale")]
    public string? Locale { get; set; }

    /// <summary>
    /// Gets or sets what to say instead.
    /// </summary>
    [Required]
    [Display(Name = "Wording", Description = "What to say instead, keeping the placeholders the sentence has.")]
    [JsonPropertyName(name: "text")]
    public string Text { get; set; } = string.Empty;
}

/// <summary>
/// What the plugin says, and what an administrator says instead.
/// </summary>
public static partial class Wording
{
    /// <summary>
    /// One sentence the plugin can write, as the admin page lists it.
    /// </summary>
    /// <param name="Key">The key it is stored under.</param>
    /// <param name="Text">What it says today, in English.</param>
    /// <param name="Placeholders">How many things it names, as <c>{0}</c> and so on.</param>
    /// <param name="Describes">What each of those is, when the resource says.</param>
    public sealed record Sentence(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("placeholders")] int Placeholders,
        [property: JsonPropertyName("describes")] string? Describes);

    private static readonly Lazy<IReadOnlyList<Sentence>> _sentences = new(Read);

    /// <summary>
    /// Every sentence the plugin can write, in the order the resources declare them.
    /// </summary>
    /// <returns>The sentences, for a page that lists them.</returns>
    public static IReadOnlyList<Sentence> Sentences() => _sentences.Value;

    /// <summary>
    /// What to say instead of a sentence, if an administrator said anything.
    /// </summary>
    /// <param name="key">The sentence, by its key.</param>
    /// <param name="culture">Who it is being written for.</param>
    /// <param name="said">What the administrator said, which may be nothing.</param>
    /// <returns>The wording to use, or <c>null</c> to leave the plugin's own standing.</returns>
    /// <remarks>
    /// The most specific language wins, so a server can answer French one way, Canadian
    /// French another, and everybody else a third, which is the shape the rest of this
    /// plugin resolves in. Where two rows say the same thing about the same language, the
    /// first is the one that counts, and the page keeps only that one.
    /// </remarks>
    public static string? For(string key, CultureInfo? culture, IEnumerable<WordingOverride>? said)
    {
        if (said is null)
        {
            return null;
        }

        var mine = said
            .Where(one => one is { Text: not null }
                && !string.IsNullOrWhiteSpace(one.Text)
                && string.Equals(one.Key, key, StringComparison.Ordinal))
            .ToList();

        if (mine.Count == 0)
        {
            return null;
        }

        var tag = culture?.Name ?? string.Empty;
        var language = tag.Split('-')[0];

        return Matching(mine, tag)
            ?? (language.Length > 0 ? Matching(mine, language) : null)
            ?? mine.Find(one => string.IsNullOrWhiteSpace(one.Locale))?.Text;
    }

    /// <summary>
    /// Whether a sentence exists, and how many things it names.
    /// </summary>
    /// <param name="key">The sentence, by its key.</param>
    /// <returns>The sentence, or <c>null</c> when this server has no such thing.</returns>
    public static Sentence? Known(string key) =>
        Sentences().FirstOrDefault(one => string.Equals(one.Key, key, StringComparison.Ordinal));

    /// <summary>
    /// The highest placeholder a wording asks for, counted from one.
    /// </summary>
    /// <param name="text">The wording.</param>
    /// <returns>How many things it needs to be given.</returns>
    /// <remarks>
    /// String.Format throws on a placeholder nothing was passed for, inside an event
    /// handler the server is waiting on, so this is what the page and the save both check
    /// rather than finding out when the event fires.
    /// </remarks>
    public static int Asks(string? text) =>
        string.IsNullOrEmpty(text)
            ? 0
            : Placeholder()
                .Matches(text)
                // A number too big to be an index is not one, and parsing it threw out of
                // the save rather than refusing the wording. String.Format says the same:
                // it reads {99999999999} as text and not as a placeholder.
                .Select(match => int.TryParse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture, out var index)
                    ? index + 1
                    : 0)
                .DefaultIfEmpty(0)
                .Max();

    /// <summary>
    /// Whether a wording carries a number that cannot be a placeholder at all.
    /// </summary>
    /// <param name="text">The wording.</param>
    /// <returns>True when one of its numbers is too big to be an index.</returns>
    /// <remarks>
    /// String.Format refuses <c>{99999999999}</c> the same way it refuses <c>{2}</c> with
    /// two things to say: both throw, so both are wordings that would never work. Parsing
    /// it threw out of the save instead, which is a 500 where a sentence was meant.
    /// </remarks>
    public static bool AsksForWhatCannotBeAPlaceholder(string? text) =>
        !string.IsNullOrEmpty(text)
        && Placeholder()
            .Matches(text)
            .Any(match => !int.TryParse(match.Groups[1].ValueSpan, CultureInfo.InvariantCulture, out _));

    // A language is typed by hand into a box, so fr_CA and FR-ca are the same language as
    // fr-CA. The rest of the plugin normalises device languages the same way.
    private static string? Matching(List<WordingOverride> said, string tag) =>
        said.Find(one => !string.IsNullOrWhiteSpace(one.Locale)
            && string.Equals(one.Locale!.Replace('_', '-'), tag, StringComparison.OrdinalIgnoreCase))?.Text;

    // Read from the English resources rather than from a list written here, so a sentence
    // added to the plugin appears on the page without being written down twice. The
    // comment beside each one says what its placeholders are, which is exactly what
    // somebody rewriting the sentence needs and what the ResourceManager does not carry.
    private static IReadOnlyList<Sentence> Read()
    {
        using var stream = typeof(Wording).Assembly
            .GetManifestResourceStream("Jellyfin.Plugin.Streamyfin.Resources.Sentences.xml");

        if (stream is null)
        {
            return [];
        }

        using var reader = new StreamReader(stream);
        var document = XDocument.Load(reader);

        return
        [
            .. document
                .Descendants("data")
                .Select(data => new Sentence(
                    Key: data.Attribute("name")?.Value ?? string.Empty,
                    Text: data.Element("value")?.Value ?? string.Empty,
                    Placeholders: Asks(data.Element("value")?.Value),
                    Describes: data.Nodes().OfType<XComment>().FirstOrDefault()?.Value.Trim()))
                .Where(sentence => sentence.Key.Length > 0)
        ];
    }

    [GeneratedRegex(@"\{(\d+)[^}]*\}")]
    private static partial Regex Placeholder();
}
