using Microsoft.Win32;
using System.Text;
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

        var target = ParseTarget(registryPath);
        using var registryKey = OpenRegistryKey(target.RegistryKeyPath, out var normalizedKeyPath);
        if (registryKey is null)
        {
            throw new InvalidOperationException($"Registry path not found: {registryPath}");
        }

        var normalizedSourcePath = target.ValueName is null
            ? normalizedKeyPath
            : $"{normalizedKeyPath}:{target.ValueName}";

        return new RegistrySnapshot
        {
            SourcePath = normalizedSourcePath,
            CapturedAtUtc = DateTime.UtcNow,
            Root = ParseNode(registryKey, GetLeafName(normalizedKeyPath), normalizedKeyPath, target.ValueName),
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
        normalizedPath = NormalizeKeyPath(registryPath);
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

    private static RegistryNodeSnapshot ParseNode(RegistryKey registryKey, string name, string fullPath, string? valueNameFilter = null)
    {
        var node = new RegistryNodeSnapshot
        {
            Name = name,
            FullPath = fullPath,
        };

        if (valueNameFilter is not null)
        {
            if (TryReadValueSnapshot(registryKey, valueNameFilter, out var valueSnapshot))
            {
                node.Values.Add(valueSnapshot);
            }

            return node;
        }

        foreach (var valueName in registryKey.GetValueNames().OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
        {
            var value = registryKey.GetValue(valueName);
            node.Values.Add(new RegistryValueSnapshot
            {
                Name = NormalizeValueName(valueName),
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

    private static bool TryReadValueSnapshot(RegistryKey registryKey, string requestedValueName, out RegistryValueSnapshot valueSnapshot)
    {
        var actualValueName = GetActualValueName(requestedValueName);
        var matchedValueName = registryKey
            .GetValueNames()
            .FirstOrDefault(name => string.Equals(NormalizeValueName(name), requestedValueName, StringComparison.OrdinalIgnoreCase));

        if (matchedValueName is null)
        {
            valueSnapshot = null!;
            return false;
        }

        var value = registryKey.GetValue(actualValueName);
        valueSnapshot = new RegistryValueSnapshot
        {
            Name = NormalizeValueName(matchedValueName),
            Kind = registryKey.GetValueKind(actualValueName).ToString(),
            Data = ConvertRegistryValue(value),
        };
        return true;
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
            byte[] bytes => ConvertBinaryValue(bytes),
            string[] items => string.Join(Environment.NewLine, items),
            _ => value.ToString() ?? string.Empty,
        };
    }

    private static string ConvertBinaryValue(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        var utf8Text = Encoding.UTF8.GetString(bytes);
        var cleanedUtf8 = CleanDecodedText(utf8Text);
        if (!string.IsNullOrWhiteSpace(cleanedUtf8))
        {
            return cleanedUtf8;
        }

        var unicodeText = Encoding.Unicode.GetString(bytes);
        var cleanedUnicode = CleanDecodedText(unicodeText);
        if (!string.IsNullOrWhiteSpace(cleanedUnicode))
        {
            return cleanedUnicode;
        }

        return Encoding.Default.GetString(bytes)
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static string CleanDecodedText(string text)
    {
        return text
            .Replace("\0", string.Empty, StringComparison.Ordinal)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Trim();
    }

    private static RegistryTarget ParseTarget(string registryPath)
    {
        var normalizedInput = NormalizeRegistryInput(registryPath);
        var separatorIndex = normalizedInput.IndexOf(':');
        if (separatorIndex > 0 && separatorIndex < normalizedInput.Length - 1)
        {
            var keyPath = NormalizeKeyPath(normalizedInput[..separatorIndex]);
            var valueName = NormalizeValueName(normalizedInput[(separatorIndex + 1)..].Trim());
            if (!string.IsNullOrWhiteSpace(keyPath) && !string.IsNullOrWhiteSpace(valueName))
            {
                return new RegistryTarget
                {
                    RegistryKeyPath = keyPath,
                    ValueName = valueName,
                };
            }
        }

        return new RegistryTarget
        {
            RegistryKeyPath = NormalizeKeyPath(normalizedInput),
        };
    }

    private static string NormalizeRegistryInput(string registryPath)
    {
        var trimmed = registryPath.Trim();
        var index = trimmed.IndexOf("HKEY_", StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            trimmed = trimmed[index..];
        }

        return trimmed.Trim();
    }

    private static string NormalizeKeyPath(string registryPath)
    {
        return NormalizeRegistryInput(registryPath).TrimEnd('\\');
    }

    private static string NormalizeValueName(string valueName)
    {
        return string.IsNullOrEmpty(valueName) ? "(Default)" : valueName.Trim();
    }

    private static string GetActualValueName(string valueName)
    {
        return string.Equals(valueName, "(Default)", StringComparison.OrdinalIgnoreCase) ? string.Empty : valueName;
    }

    private static string GetLeafName(string normalizedPath)
    {
        var lastSeparator = normalizedPath.LastIndexOf('\\');
        return lastSeparator >= 0 ? normalizedPath[(lastSeparator + 1)..] : normalizedPath;
    }

    private sealed class RegistryTarget
    {
        public required string RegistryKeyPath { get; init; }

        public string? ValueName { get; init; }
    }
}
