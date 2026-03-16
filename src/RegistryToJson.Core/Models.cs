namespace RegistryToJson.Core;

public enum RegistryChangeType
{
    Added,
    Removed,
    Modified,
}

public sealed class RegistrySnapshot
{
    public required string SourcePath { get; init; }

    public required DateTime CapturedAtUtc { get; init; }

    public required RegistryNodeSnapshot Root { get; init; }
}

public sealed class RegistryNodeSnapshot
{
    public required string Name { get; init; }

    public required string FullPath { get; init; }

    public List<RegistryValueSnapshot> Values { get; init; } = [];

    public List<RegistryNodeSnapshot> Children { get; init; } = [];
}

public sealed class RegistryValueSnapshot
{
    public required string Name { get; init; }

    public required string Kind { get; init; }

    public required string Data { get; init; }
}

public sealed class RegistryDiffEntry
{
    public required RegistryChangeType ChangeType { get; init; }

    public required string ItemType { get; init; }

    public required string Path { get; init; }

    public string? Name { get; init; }

    public string? OldValue { get; init; }

    public string? NewValue { get; init; }
}

public sealed class RegistryDiffResult
{
    public required DateTime ComparedAtUtc { get; init; }

    public List<RegistryDiffEntry> Entries { get; init; } = [];

    public bool HasChanges => Entries.Count > 0;
}

public sealed class ExportRequest
{
    public required string RegistryPath { get; init; }

    public required string OutputFilePath { get; init; }
}