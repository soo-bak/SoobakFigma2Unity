using System;
using System.Collections.Generic;

namespace SoobakFigma2Unity.Editor.Util
{
    public enum LogLevel
    {
        Info,
        Warning,
        Error,
        Success
    }

    public readonly struct LogEntry
    {
        public readonly DateTime Timestamp;
        public readonly LogLevel Level;
        public readonly string Message;

        public LogEntry(LogLevel level, string message)
        {
            Timestamp = DateTime.Now;
            Level = level;
            Message = message;
        }

        public string Prefix => Level switch
        {
            LogLevel.Info => "ℹ",
            LogLevel.Warning => "⚠",
            LogLevel.Error => "✗",
            LogLevel.Success => "✓",
            _ => " "
        };

        public override string ToString() => $"{Prefix} {Message}";
    }

    internal sealed class ImportLogger
    {
        private readonly List<LogEntry> _entries = new List<LogEntry>();
        public IReadOnlyList<LogEntry> Entries => _entries;

        public event Action OnLogUpdated;

        public void Info(string msg) => Add(LogLevel.Info, msg);
        public void Warn(string msg) => Add(LogLevel.Warning, msg);
        public void Error(string msg) => Add(LogLevel.Error, msg);
        public void Success(string msg) => Add(LogLevel.Success, msg);

        public void Clear()
        {
            _entries.Clear();
            OnLogUpdated?.Invoke();
        }

        private void Add(LogLevel level, string message)
        {
            _entries.Add(new LogEntry(level, message));
            OnLogUpdated?.Invoke();
        }
    }
}
