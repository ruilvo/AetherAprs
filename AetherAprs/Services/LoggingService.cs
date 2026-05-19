using System;
using System.Diagnostics;
using System.IO;

namespace AetherAprs.Services;

public class LoggingService : ILoggingService
{
    private readonly IConfigurationService _configurationService;
    private readonly LogLevel _minLogLevel;
    private readonly bool _writeToFile;
    private readonly string _logFilePath;

    public LoggingService(IConfigurationService configurationService)
    {
        _configurationService = configurationService;

        var settings = _configurationService.Settings;

        _minLogLevel = settings.LogLevel;

        _writeToFile = settings.WriteToFile;

        if (_writeToFile)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _logFilePath = Path.Combine(appData, "AetherAprs", "Logs", "app.log");

            var directory = Path.GetDirectoryName(_logFilePath);
            if (directory != null && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }
        else
        {
            _logFilePath = string.Empty;
        }
    }

    public void Log(LogLevel level, string message)
    {
        if (level < _minLogLevel)
        {
            return;
        }

        var formattedMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";

        // Always write to Debug output in VS
        System.Diagnostics.Debug.WriteLine(formattedMessage);

        if (_writeToFile && !string.IsNullOrEmpty(_logFilePath))
        {
            try
            {
                File.AppendAllText(_logFilePath, formattedMessage + Environment.NewLine);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write to log file: {ex.Message}");
            }
        }
    }

    public void Debug(string message) => Log(LogLevel.Debug, message);
    public void Info(string message) => Log(LogLevel.Information, message);
    public void Warn(string message) => Log(LogLevel.Warning, message);
    public void Error(string message) => Log(LogLevel.Error, message);
}
