namespace WPSupportTools.Core.Logging;

public enum LogLevel
{
    Info,
    Warning,
    Error,
}

public interface IAppLog
{
    string LogDirectory { get; }

    void Write(LogLevel level, string message);
}
