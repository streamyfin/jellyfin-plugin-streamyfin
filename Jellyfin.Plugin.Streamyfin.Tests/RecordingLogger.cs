using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Streamyfin.Tests;

/// <summary>
/// A logger that keeps what it was told, so a test can read a log line.
/// </summary>
/// <typeparam name="T">The category, which nothing here uses.</typeparam>
public sealed class RecordingLogger<T> : ILogger<T>
{
    /// <summary>
    /// Gets the messages, formatted as a real logger would format them.
    /// </summary>
    public List<string> Messages { get; } = [];

    /// <inheritdoc/>
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    /// <inheritdoc/>
    public bool IsEnabled(LogLevel logLevel) => true;

    /// <inheritdoc/>
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        ArgumentNullException.ThrowIfNull(formatter);

        Messages.Add(formatter(state, exception));
    }
}
