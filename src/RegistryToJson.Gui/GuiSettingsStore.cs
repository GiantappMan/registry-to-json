using System.IO;
using System.Text.Json;

namespace RegistryToJson.Gui;

public sealed class GuiSettings
{
    public string RegistryPath { get; init; } = string.Empty;

    public string OutputPath { get; init; } = string.Empty;

    public string IntervalText { get; init; } = "2";

    public bool WatchEnabled { get; init; }
}

public static class GuiSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly string SettingsFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RegistryToJson",
        "gui-settings.json");

    public static GuiSettings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new GuiSettings();
            }

            var json = File.ReadAllText(SettingsFilePath);
            return JsonSerializer.Deserialize<GuiSettings>(json, JsonOptions) ?? new GuiSettings();
        }
        catch
        {
            return new GuiSettings();
        }
    }

    public static void Save(GuiSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsFilePath, json);
    }
}
