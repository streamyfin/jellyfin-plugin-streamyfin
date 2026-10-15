using System;

namespace Jellyfin.Plugin.Streamyfin.Db;

/// <summary>
/// A group was saved under a name another group has. The database holds one group per name,
/// compared the way it compares them, where "Kids" and "kids" are two names.
/// </summary>
public sealed class GroupNameTakenException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="GroupNameTakenException"/> class.
    /// </summary>
    /// <param name="name">The name another group has.</param>
    /// <param name="inner">What the database said.</param>
    public GroupNameTakenException(string name, Exception inner)
        : base($"A group named \"{name}\" exists already.", inner)
    {
        Name = name;
    }

    /// <summary>
    /// Gets the name another group has.
    /// </summary>
    public string Name { get; }
}
