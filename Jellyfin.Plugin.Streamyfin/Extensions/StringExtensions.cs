using System;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Streamyfin.Extensions;

public static class StringExtensions
{
    public static string Escape(this string? input) => 
        input?.Replace("\"", "\\\"", StringComparison.Ordinal) ?? string.Empty;

    public static bool IsNullOrNonWord(this string? value) =>
        string.IsNullOrWhiteSpace(value) || Regex.Count(value, "\\w+") == 0;

    // Every control character, the line breaks among them, the line and paragraph separators,
    // which end a line in some readers without being control characters, and the bidi
    // embeddings, overrides and isolates, which turn the rest of a line around.
    private static readonly Regex NotOnOneLine = new("[\\p{Cc}\\u2028\\u2029\\u202A-\\u202E\\u2066-\\u2069]+", RegexOptions.Compiled);

    /// <summary>
    /// A value from outside the server, on one line so it cannot start a log entry of
    /// its own that reads as the server's. The other control characters go as well: an
    /// escape sequence can erase the start of the line in a terminal, and a backspace or
    /// a NUL can hide it, which leaves the forged part as the entry that shows.
    /// </summary>
    /// <remarks>
    /// It stays a <see cref="Regex.Replace(string, string)"/>, which CodeQL's log forging
    /// query counts as cleaning the value.
    /// </remarks>
    public static string? ForLog(this string? input) =>
        input is null ? null : NotOnOneLine.Replace(input, " ");
}