using System.Text.Json.Serialization;

namespace AetherAprs.Services;

public class AppSettings
{

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public LogLevel LogLevel { get; set; } = LogLevel.Debug;

    public bool WriteToFile { get; set; } = false;
}
