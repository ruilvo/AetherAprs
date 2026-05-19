using System.Text.Json.Serialization;

namespace AetherAprs.Services;

public class AppSettings
{
    public string SomeSetting { get; set; } = string.Empty;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LogLevel LogLevel { get; set; } = LogLevel.Debug;

    public bool WriteToFile { get; set; } = false;
}