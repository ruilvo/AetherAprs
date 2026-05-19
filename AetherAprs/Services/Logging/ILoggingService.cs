namespace AetherAprs.Services;

public enum LogLevel
{
    Debug,
    Information,
    Warning,
    Error,
    Critical
}

public interface ILoggingService
{
    void Log(LogLevel level, string message);
    void Debug(string message);
    void Info(string message);
    void Warn(string message);
    void Error(string message);
}
