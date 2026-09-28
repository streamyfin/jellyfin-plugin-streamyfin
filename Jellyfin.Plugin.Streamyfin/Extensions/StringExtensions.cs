using System;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Streamyfin.Extensions;

public static class StringExtensions
{
    public static string Escape(this string? input) => 
        input?.Replace("\"", "\\\"", StringComparison.Ordinal) ?? string.Empty;

    public static bool IsNullOrNonWord(this string? value) =>
        string.IsNullOrWhiteSpace(value) || Regex.Count(value, "\\w+") == 0;

    private static readonly Regex LineBreaks = new("[\\r\\n\\v\\f\\u0085\\u2028\\u2029]+", RegexOptions.Compiled);

    /// <summary>
    /// A value from outside the server, on one line so it cannot start a log entry of
    /// its own that reads as the server's.
    /// </summary>
    public static string? ForLog(this string? input) =>
        input is null ? null : LineBreaks.Replace(input, " ");
}