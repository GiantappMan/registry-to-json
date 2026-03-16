using Microsoft.Win32;
using System.Text.Json;

namespace RegistryToJson.Core;

public sealed class RegistrySnapshotService
{
    public RegistrySnapshot Capture(string registryPath)
    {
        if (string.IsNullOrWhiteSpace(registryPath))
        {
            throw new ArgumentException("Registry path is required.", nameof(registryPath));
        }

        using var registryKey = OpenRegistryKey(registryPath, out var normalizedPath);
        if (registryKey is null)
        {
            throw new InvalidOperationException($"Registry path not found: {registryPath}");
        }

        return new RegistrySnapshot
        {
            SourcePath = normalizedPath,
            CapturedAtUtc = DateTime.UtcNow,
            Root = ParseNode(registryKey, GetLeafName(normalizedPath), normalizedPath),
        };
    }

    public void Export(ExportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var snapshot = Capture(request.RegistryPath);
        var exportObject = ToExportObject(snapshot.Root);
        var json = JsonSerializer.Serialize(exportObject, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(request.OutputFilePath, json);
    }

    private static RegistryKey? OpenRegistryKey(string registryPath, out string normalizedPath)
    {
        normalizedPath = NormalizeRegistryPath(registryPath);
        var parts = normalizedPath.Split('\\', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var root = parts[0].ToUpperInvariant();
        var subKeyPath = parts.Length > 1 ? parts[1] : string.Empty;

        RegistryKey? rootKey = root switch
        {
            "HKEY_CLASSES_ROOT" => Registry.ClassesRoot,
            "HKEY_CURRENT_USER" => Registry.CurrentUser,
            "HKEY_LOCAL_MACHINE" => Registry.LocalMachine,
            "HKEY_USERS" => Registry.Users,
            "HKEY_CURRENT_CONFIG" => Registry.CurrentConfig,
            _ => null,
        };

        if (rootKey is null)
        {
            return null;
        }

        return string.IsNullOrEmpty(subKeyPath) ? rootKey : rootKey.OpenSubKey(subKeyPath);
    }

    private static RegistryNodeSnapshot ParseNode(RegistryKey registryKey, string name, string fullPath)
    {
        var node = new RegistryNodeSnapshot
        {
            Name = name,
            FullPath = fullPath,
        };

        foreach (var valueName in registryKey.GetValueNames().OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
        {
            var value = registryKey.GetValue(valueName);
            node.Values.Add(new RegistryValueSnapshot
            {
                Name = string.IsNullOrEmpty(valueName) ? "(Default)" : valueName,
                Kind = registryKey.GetValueKind(valueName).ToString(),
                Data = ConvertRegistryValue(value),
            });
        }

        foreach (var subKeyName in registryKey.GetSubKeyNames().OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
        {
            using var subKey = registryKey.OpenSubKey(subKeyName);
            if (subKey is null)
            {
                continue;
            }

            node.Children.Add(ParseNode(subKey, subKeyName, $"{fullPath}\\{subKeyName}"));
        }

        return node;
    }

    private static object ToExportObject(RegistryNodeSnapshot node)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in node.Values)
        {
            result[value.Name] = value.Data;
        }

        foreach (var child in node.Children)
        {
            result[child.Name] = ToExportObject(child);
        }

        return result;
    }

    private static string ConvertRegistryValue(object? value)
    {
        return value switch
        {
            null => string.Empty,
            byte[] bytes => Convert.ToHexString(bytes),
            string[] items => string.Join(Environment.NewLine, items),
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static string NormalizeRegistryPath(string registryPath)
    {
        var trimmed = registryPath.Trim();
        var index = trimmed.IndexOf("HKEY_", StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            trimmed = trimmed[index..];
        }

        return trimmed.Trim().TrimEnd('\\');
    }

    private static string GetLeafName(string normalizedPath)
    {
        var lastSeparator = normalizedPath.LastIndexOf('\\');
        return lastSeparator >= 0 ? normalizedPath[(lastSeparator + 1)..] : normalizedPath;
    }
}