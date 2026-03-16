namespace RegistryToJson.Core;

public sealed class RegistryWatchService
{
    private readonly RegistrySnapshotService _snapshotService;
    private readonly RegistryDiffService _diffService;

    public RegistryWatchService(RegistrySnapshotService snapshotService, RegistryDiffService diffService)
    {
        _snapshotService = snapshotService;
        _diffService = diffService;
    }

    public RegistryWatchResult Refresh(string registryPath, RegistrySnapshot? baselineSnapshot)
    {
        var current = _snapshotService.Capture(registryPath);
        var diff = baselineSnapshot is null
            ? new RegistryDiffResult { ComparedAtUtc = DateTime.UtcNow }
            : _diffService.Compare(baselineSnapshot, current);

        return new RegistryWatchResult
        {
            CurrentSnapshot = current,
            Diff = diff,
        };
    }
}

public sealed class RegistryWatchResult
{
    public required RegistrySnapshot CurrentSnapshot { get; init; }

    public required RegistryDiffResult Diff { get; init; }
}