namespace AetherAprs.Services;

public interface IConfigurationService
{
    AppSettings Settings { get; }
    void SaveSettings(AppSettings newSettings);
}