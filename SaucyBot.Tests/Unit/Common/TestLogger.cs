using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace SaucyBot.Tests.Unit.Common;

internal sealed class TestLogger<T> : ILogger<T>
{
    public List<RecordedLog> Entries { get; } = [];
    public List<string> Messages { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = formatter(state, exception);
        Entries.Add(new RecordedLog(logLevel, message));
        Messages.Add(message);
    }
}

internal sealed record RecordedLog(LogLevel Level, string Message);
