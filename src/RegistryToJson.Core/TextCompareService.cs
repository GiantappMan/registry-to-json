using System.Text;
using System.Text.Json;
using System.Xml.Linq;

namespace RegistryToJson.Core;

public enum TextCompareChangeKind
{
    Unchanged,
    Added,
    Removed,
    Modified,
}

public sealed class TextCompareLine
{
    public int? LeftLineNumber { get; init; }

    public required string LeftText { get; init; }

    public int? RightLineNumber { get; init; }

    public required string RightText { get; init; }

    public required TextCompareChangeKind ChangeKind { get; init; }
}

public sealed class TextCompareResult
{
    public required string LeftFormattedText { get; init; }

    public required string RightFormattedText { get; init; }

    public List<TextCompareLine> Lines { get; init; } = [];
}

public sealed class TextCompareService
{
    private const int MaxLinesForFullDiff = 400;
    private const int ContextWindowLineCount = 40;

    public TextCompareResult Compare(string? oldText, string? newText)
    {
        var leftFormatted = FormatForCompare(oldText);
        var rightFormatted = FormatForCompare(newText);
        var leftLines = SplitLines(leftFormatted);
        var rightLines = SplitLines(rightFormatted);
        var totalLineCount = leftLines.Count + rightLines.Count;

        var lines = totalLineCount <= MaxLinesForFullDiff
            ? BuildAlignedLines(leftLines, rightLines)
            : BuildWindowedLines(leftLines, rightLines);

        return new TextCompareResult
        {
            LeftFormattedText = leftFormatted,
            RightFormattedText = rightFormatted,
            Lines = lines,
        };
    }

    private static List<TextCompareLine> BuildAlignedLines(IReadOnlyList<string> leftLines, IReadOnlyList<string> rightLines)
    {
        var operations = BuildOperations(leftLines, rightLines);
        var rows = new List<TextCompareLine>();

        for (var index = 0; index < operations.Count; index++)
        {
            var operation = operations[index];
            if (operation.Kind == RawCompareOperationKind.Equal)
            {
                rows.Add(new TextCompareLine
                {
                    LeftLineNumber = operation.LeftLineNumber,
                    LeftText = operation.LeftText,
                    RightLineNumber = operation.RightLineNumber,
                    RightText = operation.RightText,
                    ChangeKind = TextCompareChangeKind.Unchanged,
                });
                continue;
            }

            var removed = new List<RawCompareOperation>();
            var added = new List<RawCompareOperation>();

            while (index < operations.Count && operations[index].Kind != RawCompareOperationKind.Equal)
            {
                if (operations[index].Kind == RawCompareOperationKind.Remove)
                {
                    removed.Add(operations[index]);
                }
                else
                {
                    added.Add(operations[index]);
                }

                index++;
            }

            index--;

            var pairedCount = Math.Min(removed.Count, added.Count);
            for (var pairIndex = 0; pairIndex < pairedCount; pairIndex++)
            {
                rows.Add(new TextCompareLine
                {
                    LeftLineNumber = removed[pairIndex].LeftLineNumber,
                    LeftText = removed[pairIndex].LeftText,
                    RightLineNumber = added[pairIndex].RightLineNumber,
                    RightText = added[pairIndex].RightText,
                    ChangeKind = TextCompareChangeKind.Modified,
                });
            }

            for (var removeIndex = pairedCount; removeIndex < removed.Count; removeIndex++)
            {
                rows.Add(new TextCompareLine
                {
                    LeftLineNumber = removed[removeIndex].LeftLineNumber,
                    LeftText = removed[removeIndex].LeftText,
                    RightLineNumber = null,
                    RightText = string.Empty,
                    ChangeKind = TextCompareChangeKind.Removed,
                });
            }

            for (var addIndex = pairedCount; addIndex < added.Count; addIndex++)
            {
                rows.Add(new TextCompareLine
                {
                    LeftLineNumber = null,
                    LeftText = string.Empty,
                    RightLineNumber = added[addIndex].RightLineNumber,
                    RightText = added[addIndex].RightText,
                    ChangeKind = TextCompareChangeKind.Added,
                });
            }
        }

        return rows;
    }

    private static List<RawCompareOperation> BuildOperations(IReadOnlyList<string> leftLines, IReadOnlyList<string> rightLines)
    {
        var leftCount = leftLines.Count;
        var rightCount = rightLines.Count;
        var lcs = new int[leftCount + 1, rightCount + 1];

        for (var leftIndex = leftCount - 1; leftIndex >= 0; leftIndex--)
        {
            for (var rightIndex = rightCount - 1; rightIndex >= 0; rightIndex--)
            {
                lcs[leftIndex, rightIndex] = string.Equals(leftLines[leftIndex], rightLines[rightIndex], StringComparison.Ordinal)
                    ? lcs[leftIndex + 1, rightIndex + 1] + 1
                    : Math.Max(lcs[leftIndex + 1, rightIndex], lcs[leftIndex, rightIndex + 1]);
            }
        }

        var operations = new List<RawCompareOperation>();
        var currentLeft = 0;
        var currentRight = 0;

        while (currentLeft < leftCount && currentRight < rightCount)
        {
            if (string.Equals(leftLines[currentLeft], rightLines[currentRight], StringComparison.Ordinal))
            {
                operations.Add(RawCompareOperation.Equal(currentLeft + 1, leftLines[currentLeft], currentRight + 1, rightLines[currentRight]));
                currentLeft++;
                currentRight++;
                continue;
            }

            if (lcs[currentLeft + 1, currentRight] >= lcs[currentLeft, currentRight + 1])
            {
                operations.Add(RawCompareOperation.Remove(currentLeft + 1, leftLines[currentLeft]));
                currentLeft++;
            }
            else
            {
                operations.Add(RawCompareOperation.Add(currentRight + 1, rightLines[currentRight]));
                currentRight++;
            }
        }

        while (currentLeft < leftCount)
        {
            operations.Add(RawCompareOperation.Remove(currentLeft + 1, leftLines[currentLeft]));
            currentLeft++;
        }

        while (currentRight < rightCount)
        {
            operations.Add(RawCompareOperation.Add(currentRight + 1, rightLines[currentRight]));
            currentRight++;
        }

        return operations;
    }

    private static List<TextCompareLine> BuildWindowedLines(IReadOnlyList<string> leftLines, IReadOnlyList<string> rightLines)
    {
        var prefixLength = 0;
        var sharedLineLimit = Math.Min(leftLines.Count, rightLines.Count);
        while (prefixLength < sharedLineLimit
            && string.Equals(leftLines[prefixLength], rightLines[prefixLength], StringComparison.Ordinal))
        {
            prefixLength++;
        }

        var suffixLength = 0;
        while (suffixLength < sharedLineLimit - prefixLength
            && string.Equals(
                leftLines[leftLines.Count - 1 - suffixLength],
                rightLines[rightLines.Count - 1 - suffixLength],
                StringComparison.Ordinal))
        {
            suffixLength++;
        }

        var rows = new List<TextCompareLine>();
        AppendRange(
            rows,
            leftLines,
            rightLines,
            0,
            Math.Min(prefixLength, ContextWindowLineCount),
            0,
            Math.Min(prefixLength, ContextWindowLineCount),
            TextCompareChangeKind.Unchanged);

        var hiddenPrefixLines = prefixLength - Math.Min(prefixLength, ContextWindowLineCount);
        if (hiddenPrefixLines > 0)
        {
            rows.Add(CreateSummaryRow($"... 省略前面 {hiddenPrefixLines} 行未变化内容 ..."));
        }

        var leftChangedStart = prefixLength;
        var leftChangedEnd = leftLines.Count - suffixLength;
        var rightChangedStart = prefixLength;
        var rightChangedEnd = rightLines.Count - suffixLength;

        AppendChangedBlock(
            rows,
            leftLines,
            rightLines,
            leftChangedStart,
            leftChangedEnd,
            rightChangedStart,
            rightChangedEnd);

        var suffixWindow = Math.Min(suffixLength, ContextWindowLineCount);
        var hiddenSuffixLines = suffixLength - suffixWindow;
        if (hiddenSuffixLines > 0)
        {
            rows.Add(CreateSummaryRow($"... 省略后面 {hiddenSuffixLines} 行未变化内容 ..."));
        }

        if (suffixWindow > 0)
        {
            AppendRange(
                rows,
                leftLines,
                rightLines,
                leftLines.Count - suffixWindow,
                leftLines.Count,
                rightLines.Count - suffixWindow,
                rightLines.Count,
                TextCompareChangeKind.Unchanged);
        }

        if (rows.Count == 0)
        {
            rows.Add(new TextCompareLine
            {
                LeftLineNumber = 1,
                LeftText = leftLines.FirstOrDefault() ?? string.Empty,
                RightLineNumber = 1,
                RightText = rightLines.FirstOrDefault() ?? string.Empty,
                ChangeKind = string.Equals(leftLines.FirstOrDefault(), rightLines.FirstOrDefault(), StringComparison.Ordinal)
                    ? TextCompareChangeKind.Unchanged
                    : TextCompareChangeKind.Modified,
            });
        }

        return rows;
    }

    private static void AppendChangedBlock(
        List<TextCompareLine> rows,
        IReadOnlyList<string> leftLines,
        IReadOnlyList<string> rightLines,
        int leftStart,
        int leftEnd,
        int rightStart,
        int rightEnd)
    {
        var leftCount = Math.Max(0, leftEnd - leftStart);
        var rightCount = Math.Max(0, rightEnd - rightStart);
        var displayCount = Math.Max(leftCount, rightCount);

        if (displayCount == 0)
        {
            return;
        }

        if (displayCount > ContextWindowLineCount * 2)
        {
            AppendChangedPairs(rows, leftLines, rightLines, leftStart, rightStart, ContextWindowLineCount);
            rows.Add(CreateSummaryRow($"... 变化区过长，已折叠中间 {displayCount - ContextWindowLineCount * 2} 行 ..."));

            var leftTailStart = Math.Max(leftStart, leftEnd - ContextWindowLineCount);
            var rightTailStart = Math.Max(rightStart, rightEnd - ContextWindowLineCount);
            AppendChangedPairs(rows, leftLines, rightLines, leftTailStart, rightTailStart, ContextWindowLineCount);
            return;
        }

        AppendChangedPairs(rows, leftLines, rightLines, leftStart, rightStart, displayCount);
    }

    private static void AppendChangedPairs(
        List<TextCompareLine> rows,
        IReadOnlyList<string> leftLines,
        IReadOnlyList<string> rightLines,
        int leftStart,
        int rightStart,
        int count)
    {
        for (var index = 0; index < count; index++)
        {
            var leftIndex = leftStart + index;
            var rightIndex = rightStart + index;
            var hasLeft = leftIndex >= 0 && leftIndex < leftLines.Count;
            var hasRight = rightIndex >= 0 && rightIndex < rightLines.Count;

            rows.Add(new TextCompareLine
            {
                LeftLineNumber = hasLeft ? leftIndex + 1 : null,
                LeftText = hasLeft ? leftLines[leftIndex] : string.Empty,
                RightLineNumber = hasRight ? rightIndex + 1 : null,
                RightText = hasRight ? rightLines[rightIndex] : string.Empty,
                ChangeKind = !hasLeft
                    ? TextCompareChangeKind.Added
                    : !hasRight
                        ? TextCompareChangeKind.Removed
                        : string.Equals(leftLines[leftIndex], rightLines[rightIndex], StringComparison.Ordinal)
                            ? TextCompareChangeKind.Unchanged
                            : TextCompareChangeKind.Modified,
            });
        }
    }

    private static void AppendRange(
        List<TextCompareLine> rows,
        IReadOnlyList<string> leftLines,
        IReadOnlyList<string> rightLines,
        int leftStart,
        int leftEnd,
        int rightStart,
        int rightEnd,
        TextCompareChangeKind kind)
    {
        var count = Math.Min(leftEnd - leftStart, rightEnd - rightStart);
        for (var index = 0; index < count; index++)
        {
            rows.Add(new TextCompareLine
            {
                LeftLineNumber = leftStart + index + 1,
                LeftText = leftLines[leftStart + index],
                RightLineNumber = rightStart + index + 1,
                RightText = rightLines[rightStart + index],
                ChangeKind = kind,
            });
        }
    }

    private static TextCompareLine CreateSummaryRow(string text)
    {
        return new TextCompareLine
        {
            LeftLineNumber = null,
            LeftText = text,
            RightLineNumber = null,
            RightText = text,
            ChangeKind = TextCompareChangeKind.Unchanged,
        };
    }

    private static string FormatForCompare(string? text)
    {
        var normalized = string.IsNullOrWhiteSpace(text) ? "(空)" : text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        if (TryFormatJson(normalized, out var formattedJson))
        {
            return formattedJson;
        }

        if (TryFormatXml(normalized, out var formattedXml))
        {
            return formattedXml;
        }

        return normalized;
    }

    private static bool TryFormatJson(string text, out string formatted)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            formatted = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions
            {
                WriteIndented = true,
            });
            return true;
        }
        catch
        {
            formatted = string.Empty;
            return false;
        }
    }

    private static bool TryFormatXml(string text, out string formatted)
    {
        try
        {
            var document = XDocument.Parse(text, LoadOptions.PreserveWhitespace);
            formatted = document.ToString();
            return true;
        }
        catch
        {
            formatted = string.Empty;
            return false;
        }
    }

    private static IReadOnlyList<string> SplitLines(string text)
    {
        var lines = text.Split('\n');
        return lines.Length == 0 ? [string.Empty] : lines;
    }

    private enum RawCompareOperationKind
    {
        Equal,
        Add,
        Remove,
    }

    private sealed class RawCompareOperation
    {
        public required RawCompareOperationKind Kind { get; init; }

        public int? LeftLineNumber { get; init; }

        public required string LeftText { get; init; }

        public int? RightLineNumber { get; init; }

        public required string RightText { get; init; }

        public static RawCompareOperation Equal(int leftLineNumber, string leftText, int rightLineNumber, string rightText)
        {
            return new RawCompareOperation
            {
                Kind = RawCompareOperationKind.Equal,
                LeftLineNumber = leftLineNumber,
                LeftText = leftText,
                RightLineNumber = rightLineNumber,
                RightText = rightText,
            };
        }

        public static RawCompareOperation Remove(int leftLineNumber, string leftText)
        {
            return new RawCompareOperation
            {
                Kind = RawCompareOperationKind.Remove,
                LeftLineNumber = leftLineNumber,
                LeftText = leftText,
                RightText = string.Empty,
            };
        }

        public static RawCompareOperation Add(int rightLineNumber, string rightText)
        {
            return new RawCompareOperation
            {
                Kind = RawCompareOperationKind.Add,
                LeftText = string.Empty,
                RightLineNumber = rightLineNumber,
                RightText = rightText,
            };
        }
    }
}
