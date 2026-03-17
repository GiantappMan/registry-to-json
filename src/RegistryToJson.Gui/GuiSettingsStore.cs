using System.IO;
using System.Text.Json;

namespace RegistryToJson.Gui;

public sealed class GuiSettings
{
    public List<WatchConfigurationSettings> Configurations { get; init; } = [];

    public Guid? SelectedConfigurationId { get; init; }

    public static GuiSettings CreateDefault()
    {
        var configuration = WatchConfigurationSettings.CreateDefault();
        return new GuiSettings
        {
            Configurations = [configuration],
            SelectedConfigurationId = configuration.Id,
        };
    }
}

public sealed class WatchConfigurationSettings
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Name { get; init; } = "监控项 1";

    public string RegistryPath { get; init; } = @"HKEY_CURRENT_USER\SOFTWARE";

    public string OutputPath { get; init; } = string.Empty;

    public string IntervalText { get; init; } = "2";

    public bool WatchEnabled { get; init; }

    public static WatchConfigurationSettings CreateDefault(int index = 1)
    {
        return new WatchConfigurationSettings
        {
            Name = $"监控项 {index}",
        };
    }
}

internal sealed class LegacyGuiSettings
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
                return GuiSettings.CreateDefault();
            }

            var json = File.ReadAllText(SettingsFilePath);
            var settings = JsonSerializer.Deserialize<GuiSettings>(json, JsonOptions);
            if (settings is not null && settings.Configurations.Count > 0)
            {
                return Normalize(settings);
            }

            var legacy = JsonSerializer.Deserialize<LegacyGuiSettings>(json, JsonOptions);
            return legacy is null ? GuiSettings.CreateDefault() : MigrateLegacy(legacy);
        }
        catch
        {
            return GuiSettings.CreateDefault();
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

    private static GuiSettings Normalize(GuiSettings settings)
    {
        var configurations = settings.Configurations
            .Where(static config => config is not null)
            .Select(static config => new WatchConfigurationSettings
            {
                Id = config.Id == Guid.Empty ? Guid.NewGuid() : config.Id,
                Name = string.IsNullOrWhiteSpace(config.Name) ? "监控项" : config.Name,
                RegistryPath = string.IsNullOrWhiteSpace(config.RegistryPath) ? @"HKEY_CURRENT_USER\SOFTWARE" : config.RegistryPath,
                OutputPath = config.OutputPath ?? string.Empty,
                IntervalText = string.IsNullOrWhiteSpace(config.IntervalText) ? "2" : config.IntervalText,
                WatchEnabled = config.WatchEnabled,
            })
            .ToList();

        if (configurations.Count == 0)
        {
            return GuiSettings.CreateDefault();
        }

        var selectedId = settings.SelectedConfigurationId;
        if (selectedId is null || configurations.All(config => config.Id != selectedId.Value))
        {
            selectedId = configurations[0].Id;
        }

        return new GuiSettings
        {
            Configurations = configurations,
            SelectedConfigurationId = selectedId,
        };
    }

    private static GuiSettings MigrateLegacy(LegacyGuiSettings legacy)
    {
        var configuration = new WatchConfigurationSettings
        {
            Name = "监控项 1",
            RegistryPath = string.IsNullOrWhiteSpace(legacy.RegistryPath) ? @"HKEY_CURRENT_USER\SOFTWARE" : legacy.RegistryPath,
            OutputPath = legacy.OutputPath ?? string.Empty,
            IntervalText = string.IsNullOrWhiteSpace(legacy.IntervalText) ? "2" : legacy.IntervalText,
            WatchEnabled = legacy.WatchEnabled,
        };

        return new GuiSettings
        {
            Configurations = [configuration],
            SelectedConfigurationId = configuration.Id,
        };
    }
}
