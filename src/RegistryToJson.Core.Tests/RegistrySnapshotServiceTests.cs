using Microsoft.Win32;
using Xunit;

namespace RegistryToJson.Core.Tests;

public sealed class RegistrySnapshotServiceTests : IDisposable
{
    private readonly string _testKeyPath = $@"HKEY_CURRENT_USER\Software\RegistryToJson.Tests\{Guid.NewGuid():N}";
    private readonly RegistrySnapshotService _snapshotService = new();
    private readonly RegistryDiffService _diffService = new();
    private readonly TextCompareService _textCompareService = new();

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

    [Fact]
    public void TextCompare_WithJsonContent_BuildsAlignedModifiedRows()
    {
        var result = _textCompareService.Compare(
            """
            {"alpha":1,"beta":2}
            """,
            """
            {"alpha":1,"beta":3,"gamma":4}
            """);

        Assert.Contains(result.Lines, static line => line.ChangeKind == TextCompareChangeKind.Modified);
        Assert.Contains(result.Lines, static line => line.ChangeKind == TextCompareChangeKind.Added);
        Assert.Contains("\"beta\": 2", result.LeftFormattedText);
        Assert.Contains("\"beta\": 3", result.RightFormattedText);
    }

    [Fact]
    public void TextCompare_WithLargeText_UsesWindowedRows()
    {
        var oldLines = Enumerable.Range(1, 600).Select(index => $"line-{index}").ToArray();
        var newLines = oldLines.ToArray();
        newLines[320] = "line-321-changed";

        var result = _textCompareService.Compare(
            string.Join('\n', oldLines),
            string.Join('\n', newLines));

        Assert.True(result.Lines.Count < 200);
        Assert.Contains(result.Lines, static line => line.LeftText.Contains("省略", StringComparison.Ordinal));
        Assert.Contains(result.Lines, static line => line.ChangeKind == TextCompareChangeKind.Modified);
    }

    [Fact]
    public void TryGetNestedCandidate_WithEscapedJsonString_ReturnsDecodedJson()
    {
        var line = new TextCompareLine
        {
            LeftLineNumber = 1,
            LeftText = """
            "payload": "{\"alpha\":1,\"beta\":2}",
            """,
            RightLineNumber = 1,
            RightText = """
            "payload": "{\"alpha\":1,\"beta\":3}",
            """,
            ChangeKind = TextCompareChangeKind.Modified,
        };

        var nested = _textCompareService.TryGetNestedCandidate(line);

        Assert.NotNull(nested);
        Assert.Equal("JSON", nested.KindLabel);
        Assert.Contains("\"alpha\": 1", nested.LeftText);
        Assert.Contains("\"beta\": 3", nested.RightText);
    }

    [Fact]
    public void TryGetNestedCandidate_WithEscapedXmlString_ReturnsDecodedXml()
    {
        var line = new TextCompareLine
        {
            LeftLineNumber = 1,
            LeftText = """
            "payload": "\u003Croot\u003E\u003Citem\u003Eold\u003C/item\u003E\u003C/root\u003E",
            """,
            RightLineNumber = 1,
            RightText = """
            "payload": "\u003Croot\u003E\u003Citem\u003Enew\u003C/item\u003E\u003C/root\u003E",
            """,
            ChangeKind = TextCompareChangeKind.Modified,
        };

        var nested = _textCompareService.TryGetNestedCandidate(line);

        Assert.NotNull(nested);
        Assert.Equal("XML", nested.KindLabel);
        Assert.Contains("<item>old</item>", nested.LeftText);
        Assert.Contains("<item>new</item>", nested.RightText);
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
