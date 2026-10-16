using Jellyfin.Plugin.Streamyfin.Extensions;

namespace Jellyfin.Plugin.Streamyfin.Api;

/// <summary>
/// What a group may be called. The routes that create and rename a group check a name the
/// same way a restore does, so a file cannot store a name the Targeting page could not save.
/// </summary>
public static class GroupName
{
    /// <summary>
    /// The longest name a group may have, counted the way the page's field counts it, in
    /// UTF-16 code units. A name is read in a list, and nothing longer than this reads there.
    /// </summary>
    public const int Longest = 100;

    /// <summary>
    /// The name as it is stored, without the spaces and line breaks around it. The page trims
    /// what it sends and compares its field with the stored name trimmed, so with them a group
    /// counted as changed as soon as it was opened, and was another group than the same name
    /// without them.
    /// </summary>
    /// <param name="name">The name sent.</param>
    /// <returns>The name to store.</returns>
    public static string Tidy(string? name) => name?.Trim() ?? string.Empty;

    /// <summary>
    /// Why a group cannot be called <paramref name="name"/>, judged on the name as it would be
    /// stored.
    /// </summary>
    /// <param name="name">The name sent.</param>
    /// <returns>A sentence for the page, or <c>null</c> when the name is taken as it is.</returns>
    public static string? Problem(string? name)
    {
        var tidied = Tidy(name);

        if (tidied.Length == 0)
        {
            return "A group needs a name";
        }

        // The page's field holds one line, so it dropped a line break and saving renamed the
        // group, and two names apart by a control character read the same on the page.
        if (!tidied.FitsOnOneLine())
        {
            return "A group name cannot hold a line break or another control character";
        }

        return tidied.Length > Longest ? $"A group name can be at most {Longest} characters long" : null;
    }
}
