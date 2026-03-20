using Microsoft.Win32;
using Xunit;
using System.Text.Json;

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
    public void ExportSnapshot_WritesIndentedJson()
    {
        var snapshot = new RegistrySnapshot
        {
            SourcePath = _testKeyPath,
            CapturedAtUtc = DateTime.UtcNow,
            Root = new RegistryNodeSnapshot
            {
                Name = "Root",
                FullPath = _testKeyPath,
                Values =
                [
                    new RegistryValueSnapshot
                    {
                        Name = "Alpha",
                        Kind = "String",
                        Data = "one",
                    },
                ],
                Children =
                [
                    new RegistryNodeSnapshot
                    {
                        Name = "Child",
                        FullPath = $@"{_testKeyPath}\Child",
                        Values =
                        [
                            new RegistryValueSnapshot
                            {
                                Name = "Beta",
                                Kind = "String",
                                Data = "two",
                            },
                        ],
                    },
                ],
            },
        };
        var outputPath = Path.Combine(Path.GetTempPath(), $"registry-export-{Guid.NewGuid():N}.json");

        try
        {
            _snapshotService.ExportSnapshot(snapshot, outputPath);

            var json = File.ReadAllText(outputPath);
            Assert.Contains(Environment.NewLine, json);

            using var document = JsonDocument.Parse(json);
            Assert.Equal("one", document.RootElement.GetProperty("Alpha").GetString());
            Assert.Equal("two", document.RootElement.GetProperty("Child").GetProperty("Beta").GetString());
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void ExportSnapshot_WithNestedJsonString_ExpandsIntoJsonObject()
    {
        var snapshot = new RegistrySnapshot
        {
            SourcePath = _testKeyPath,
            CapturedAtUtc = DateTime.UtcNow,
            Root = new RegistryNodeSnapshot
            {
                Name = "Root",
                FullPath = _testKeyPath,
                Values =
                [
                    new RegistryValueSnapshot
                    {
                        Name = "Payload",
                        Kind = "String",
                        Data = """{"alpha":1,"beta":{"enabled":true},"items":[1,"two",false]}""",
                    },
                ],
            },
        };
        var outputPath = Path.Combine(Path.GetTempPath(), $"registry-export-{Guid.NewGuid():N}.json");

        try
        {
            _snapshotService.ExportSnapshot(snapshot, outputPath);

            using var document = JsonDocument.Parse(File.ReadAllText(outputPath));
            var payload = document.RootElement.GetProperty("Payload");
            Assert.Equal(JsonValueKind.Object, payload.ValueKind);
            Assert.Equal(1, payload.GetProperty("alpha").GetInt32());
            Assert.True(payload.GetProperty("beta").GetProperty("enabled").GetBoolean());
            Assert.Equal("two", payload.GetProperty("items")[1].GetString());
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void ExportSnapshot_WithPlainString_KeepsOriginalText()
    {
        var snapshot = new RegistrySnapshot
        {
            SourcePath = _testKeyPath,
            CapturedAtUtc = DateTime.UtcNow,
            Root = new RegistryNodeSnapshot
            {
                Name = "Root",
                FullPath = _testKeyPath,
                Values =
                [
                    new RegistryValueSnapshot
                    {
                        Name = "PlainText",
                        Kind = "String",
                        Data = "not-json-content",
                    },
                ],
            },
        };
        var outputPath = Path.Combine(Path.GetTempPath(), $"registry-export-{Guid.NewGuid():N}.json");

        try
        {
            _snapshotService.ExportSnapshot(snapshot, outputPath);

            using var document = JsonDocument.Parse(File.ReadAllText(outputPath));
            Assert.Equal("not-json-content", document.RootElement.GetProperty("PlainText").GetString());
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void ExportSnapshot_WithNestedJsonStringFields_RecursivelyExpandsNestedJson()
    {
        var snapshot = new RegistrySnapshot
        {
            SourcePath = _testKeyPath,
            CapturedAtUtc = DateTime.UtcNow,
            Root = new RegistryNodeSnapshot
            {
                Name = "Root",
                FullPath = _testKeyPath,
                Values =
                [
                    new RegistryValueSnapshot
                    {
                        Name = "Payload",
                        Kind = "String",
                        Data = """{"uiSaveData":"{\"currWindowResolutionIndex\":4}","nestedList":["{\"enabled\":true}"]}""",
                    },
                ],
            },
        };
        var outputPath = Path.Combine(Path.GetTempPath(), $"registry-export-{Guid.NewGuid():N}.json");

        try
        {
            _snapshotService.ExportSnapshot(snapshot, outputPath);

            using var document = JsonDocument.Parse(File.ReadAllText(outputPath));
            var payload = document.RootElement.GetProperty("Payload");
            Assert.Equal(4, payload.GetProperty("uiSaveData").GetProperty("currWindowResolutionIndex").GetInt32());
            Assert.True(payload.GetProperty("nestedList")[0].GetProperty("enabled").GetBoolean());
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public void ExportSnapshot_WithXmlString_ExportsReadableXmlLines()
    {
        var snapshot = new RegistrySnapshot
        {
            SourcePath = _testKeyPath,
            CapturedAtUtc = DateTime.UtcNow,
            Root = new RegistryNodeSnapshot
            {
                Name = "Root",
                FullPath = _testKeyPath,
                Values =
                [
                    new RegistryValueSnapshot
                    {
                        Name = "XmlPayload",
                        Kind = "String",
                        Data = "<root><item>value</item></root>",
                    },
                ],
            },
        };
        var outputPath = Path.Combine(Path.GetTempPath(), $"registry-export-{Guid.NewGuid():N}.json");

        try
        {
            _snapshotService.ExportSnapshot(snapshot, outputPath);

            var json = File.ReadAllText(outputPath);
            Assert.Contains("<root>", json);
            Assert.DoesNotContain("\\u003C", json, StringComparison.OrdinalIgnoreCase);

            using var document = JsonDocument.Parse(json);
            var xmlPayload = document.RootElement.GetProperty("XmlPayload");
            Assert.Equal("xml", xmlPayload.GetProperty("_format").GetString());
            var lines = xmlPayload.GetProperty("_lines");
            Assert.Equal(JsonValueKind.Array, lines.ValueKind);
            Assert.Contains(lines.EnumerateArray().Select(static item => item.GetString()), static line => line == "<root>");
            Assert.Contains(lines.EnumerateArray().Select(static item => item.GetString()), static line => line == "  <item>value</item>");
        }
        finally
        {
            File.Delete(outputPath);
        }
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

    [Fact]
    public void TryGetNestedCandidate_WithSameNestedJson_DoesNotMarkNestedDiff()
    {
        var line = new TextCompareLine
        {
            LeftLineNumber = 1,
            LeftText = """
            "payload": "{\"alpha\":1,\"beta\":2}",
            """,
            RightLineNumber = 1,
            RightText = """
            "payload": "{\"alpha\":1,\"beta\":2}",
            """,
            ChangeKind = TextCompareChangeKind.Unchanged,
        };

        var nested = _textCompareService.TryGetNestedCandidate(line);

        Assert.NotNull(nested);
        Assert.False(nested.HasNestedDiff);
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
