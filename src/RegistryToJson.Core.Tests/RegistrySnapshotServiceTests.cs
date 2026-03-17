using Microsoft.Win32;
using Xunit;

namespace RegistryToJson.Core.Tests;

public sealed class RegistrySnapshotServiceTests : IDisposable
{
    private readonly string _testKeyPath = $@"HKEY_CURRENT_USER\Software\RegistryToJson.Tests\{Guid.NewGuid():N}";
    private readonly RegistrySnapshotService _snapshotService = new();
    private readonly RegistryDiffService _diffService = new();

    [Fact]
    public void Capture_WithValueSelector_ReturnsOnlyRequestedValue()
    {
        using var key = Registry.CurrentUser.CreateSubKey(GetSubKeyPath(_testKeyPath));
        key!.SetValue("Alpha", "one");
        key.SetValue("Beta", "two");

        var snapshot = _snapshotService.Capture($"{_testKeyPath}:Alpha");

        Assert.Equal($"{_testKeyPath}:Alpha", snapshot.SourcePath);
        Assert.Single(snapshot.Root.Values);
        Assert.Equal("Alpha", snapshot.Root.Values[0].Name);
        Assert.Equal("one", snapshot.Root.Values[0].Data);
        Assert.Empty(snapshot.Root.Children);
    }

    [Fact]
    public void Compare_WithValueSelector_DetectsModifiedValue()
    {
        using var key = Registry.CurrentUser.CreateSubKey(GetSubKeyPath(_testKeyPath));
        key!.SetValue("TargetValue", "before");

        var baseline = _snapshotService.Capture($"{_testKeyPath}:TargetValue");

        key.SetValue("TargetValue", "after");

        var current = _snapshotService.Capture($"{_testKeyPath}:TargetValue");
        var diff = _diffService.Compare(baseline, current);

        var entry = Assert.Single(diff.Entries);
        Assert.Equal(RegistryChangeType.Modified, entry.ChangeType);
        Assert.Equal("Value", entry.ItemType);
        Assert.Equal(_testKeyPath, entry.Path);
        Assert.Equal("TargetValue", entry.Name);
        Assert.Equal("before", entry.OldValue);
        Assert.Equal("after", entry.NewValue);
    }

    [Fact]
    public void Compare_WithValueSelector_DetectsRemovedValue()
    {
        using var key = Registry.CurrentUser.CreateSubKey(GetSubKeyPath(_testKeyPath));
        key!.SetValue("TargetValue", "before");

        var baseline = _snapshotService.Capture($"{_testKeyPath}:TargetValue");

        key.DeleteValue("TargetValue");

        var current = _snapshotService.Capture($"{_testKeyPath}:TargetValue");
        var diff = _diffService.Compare(baseline, current);

        var entry = Assert.Single(diff.Entries);
        Assert.Equal(RegistryChangeType.Removed, entry.ChangeType);
        Assert.Equal("Value", entry.ItemType);
        Assert.Equal(_testKeyPath, entry.Path);
        Assert.Equal("TargetValue", entry.Name);
        Assert.Equal("before", entry.OldValue);
        Assert.Null(entry.NewValue);
    }

    public void Dispose()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(GetSubKeyPath(_testKeyPath), throwOnMissingSubKey: false);
        }
        catch
        {
            // Ignore cleanup failures in tests.
        }
    }

    private static string GetSubKeyPath(string fullRegistryPath)
    {
        const string prefix = @"HKEY_CURRENT_USER\";
        return fullRegistryPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? fullRegistryPath[prefix.Length..]
            : fullRegistryPath;
    }
}
