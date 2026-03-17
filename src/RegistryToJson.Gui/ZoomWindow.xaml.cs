using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using RegistryToJson.Core;

namespace RegistryToJson.Gui;

public partial class ZoomWindow : Window
{
    private const int MaxPreviewLength = 240;
    private TextCompareService? _compareService;
    private CompareViewState? _currentCompareState;

    public ZoomWindow()
    {
        InitializeComponent();
    }

    public void ShowSnapshotTree(IEnumerable<SnapshotTreeItem> items, string title, string description, string meta)
    {
        UpdateHeader(title, description, meta);
        ZoomContentHost.Content = Wrap(CreateReadonlyTextBox(FormatSnapshotTree(items)));
    }

    public void ShowDiffEntries(IEnumerable<DiffListItem> items, string title, string description, string meta)
    {
        UpdateHeader(title, description, meta);
        ZoomContentHost.Content = Wrap(CreateReadonlyTextBox(FormatDiffEntries(items)));
    }

    public async Task ShowDiffCompareAsync(
        DiffListItem item,
        TextCompareService compareService,
        string title,
        string description,
        string meta,
        int depth = 0,
        bool collapseUnchangedRows = false,
        string? baseTitle = null)
    {
        _compareService = compareService;
        _currentCompareState = null;

        var state = new CompareViewState(
            item,
            title,
            description,
            meta,
            depth == 0 ? "根层" : $"第 {depth + 1} 层",
            "正在生成左右对照视图...",
            depth,
            baseTitle ?? ResolveBaseTitle(title),
            collapseUnchangedRows);

        await LoadCompareStateAsync(state);
    }

    private async Task LoadCompareStateAsync(CompareViewState state)
    {
        if (_compareService is null)
        {
            return;
        }

        UpdateHeader(state.Title, state.Description, state.Meta);
        ZoomContentHost.Content = Wrap(CreateLoadingView(state.Item, state.LoadingText));
        await Task.Yield();

        try
        {
            var result = await Task.Run(() => _compareService.Compare(state.Item.OldValue, state.Item.NewValue));
            _currentCompareState = state with { Result = result };
            ZoomContentHost.Content = Wrap(CreateCompareView(_currentCompareState, _compareService));
        }
        catch (Exception ex)
        {
            ZoomContentHost.Content = Wrap(CreateErrorView(state.Item, ex));
        }
    }

    private void UpdateHeader(string title, string description, string meta)
    {
        Title = title;
        WindowTitleTextBlock.Text = title;
        WindowDescriptionTextBlock.Text = description;
        WindowMetaTextBlock.Text = meta;
    }

    private static Border Wrap(UIElement content)
    {
        return new Border
        {
            Background = Brush("PanelBackgroundBrush"),
            BorderBrush = Brush("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18),
            Child = content,
        };
    }

    private static TextBox CreateReadonlyTextBox(string content)
    {
        return new TextBox
        {
            Text = content,
            IsReadOnly = true,
            IsReadOnlyCaretVisible = true,
            AcceptsReturn = true,
            AcceptsTab = true,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            BorderThickness = new Thickness(1),
            Background = Brush("ControlBackgroundBrush"),
            BorderBrush = Brush("PanelBorderBrush"),
            Foreground = Brush("TextBrush"),
        };
    }

    private static FrameworkElement CreateLoadingView(DiffListItem item, string loadingText)
    {
        var panel = new StackPanel();
        panel.Children.Add(Summary($"变化: {item.ChangeLabel} | 类型: {item.ItemType}"));
        panel.Children.Add(Summary($"路径: {item.Path}"));
        panel.Children.Add(Summary($"名称: {item.Name}"));
        panel.Children.Add(new TextBlock
        {
            Text = loadingText,
            Margin = new Thickness(0, 18, 0, 10),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextBrush"),
        });
        panel.Children.Add(new ProgressBar
        {
            IsIndeterminate = true,
            Height = 6,
        });
        panel.Children.Add(Summary("窗口会先打开，再在后台生成 compare，避免双击时主界面卡死。"));
        return panel;
    }

    private FrameworkElement CreateCompareView(CompareViewState state, TextCompareService compareService)
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var summary = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        summary.Children.Add(Summary($"变化: {state.Item.ChangeLabel} | 类型: {state.Item.ItemType}"));
        summary.Children.Add(Summary($"路径: {state.Item.Path}"));
        summary.Children.Add(Summary($"名称: {state.Item.Name}"));
        summary.Children.Add(Summary($"当前层级: {state.LevelLabel}"));
        summary.Children.Add(Summary($"展示模式: {(state.CollapseUnchangedRows ? "仅变化" : "完整对照")}"));
        summary.Children.Add(Summary($"展示行数: {state.Result?.Lines.Count ?? 0}"));
        summary.Children.Add(Summary($"变化行数: {state.ChangeCount}"));
        summary.Children.Add(Summary("操作: 黄色/红色/青色代表变化，蓝色代表可双击打开嵌套变化；双击会新开子对比窗口。"));
        if (NeedsPreviewNotice(state.Item, state.Result))
        {
            summary.Children.Add(Summary("说明: 超长文本已截断预览，使用下方按钮可复制完整旧值/新值。"));
        }

        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        actions.Children.Add(Action("上一处变化", (_, _) => MoveToChange(state, -1)));
        actions.Children.Add(Action("下一处变化", (_, _) => MoveToChange(state, 1)));
        actions.Children.Add(Action("复制旧值全文", (_, _) => TryCopyToClipboard(string.IsNullOrEmpty(state.Item.OldValue) ? "(空)" : state.Item.OldValue, "旧值")));
        actions.Children.Add(Action("复制新值全文", (_, _) => TryCopyToClipboard(string.IsNullOrEmpty(state.Item.NewValue) ? "(空)" : state.Item.NewValue, "新值")));
        summary.Children.Add(actions);
        root.Children.Add(summary);

        var host = new Grid();
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var grid = CreateCompareGrid(state, compareService);
        state.Grid = grid;
        host.Children.Add(grid);

        var miniMap = CreateMiniMap(state);
        Grid.SetColumn(miniMap, 1);
        host.Children.Add(miniMap);

        Grid.SetRow(host, 1);
        root.Children.Add(host);
        return root;
    }

    private DataGrid CreateCompareGrid(CompareViewState state, TextCompareService compareService)
    {
        var rows = (state.Result?.Lines ?? []).Select(line => CreateRow(line, state, compareService)).ToList();
        if (state.CollapseUnchangedRows)
        {
            rows = CollapseUnchanged(rows, state);
        }

        state.Rows = rows;
        state.ChangeIndices = rows
            .Select((row, index) => (row, index))
            .Where(static pair => pair.row.IsChanged || pair.row.CanOpenNestedDiff)
            .Select(static pair => pair.index)
            .ToList();

        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserReorderColumns = false,
            CanUserResizeRows = false,
            SelectionMode = DataGridSelectionMode.Single,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            RowHeaderWidth = 0,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
            Background = Brush("ControlBackgroundBrush"),
            BorderBrush = Brush("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            Foreground = Brush("TextBrush"),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true,
            RowBackground = Brush("ControlBackgroundBrush"),
            AlternatingRowBackground = Brush("PanelBackgroundBrush"),
            HorizontalGridLinesBrush = Brush("PanelBorderBrush"),
            VerticalGridLinesBrush = Brush("PanelBorderBrush"),
            AlternationCount = 2,
            ItemsSource = rows,
        };

        grid.MouseDoubleClick += CompareGrid_MouseDoubleClick;
        VirtualizingPanel.SetIsVirtualizing(grid, true);
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(grid, true);
        grid.RowStyle = RowStyle();
        grid.CellStyle = CellStyle();

        grid.Columns.Add(NumberColumn("旧行", nameof(CompareRow.LeftLineNumber), 68));
        grid.Columns.Add(TextColumn("旧值", nameof(CompareRow.LeftPreview), nameof(CompareRow.LeftBackground)));
        grid.Columns.Add(NumberColumn("新行", nameof(CompareRow.RightLineNumber), 68));
        grid.Columns.Add(TextColumn("新值", nameof(CompareRow.RightPreview), nameof(CompareRow.RightBackground)));
        return grid;
    }

    private FrameworkElement CreateMiniMap(CompareViewState state)
    {
        var border = new Border
        {
            Width = 28,
            Margin = new Thickness(10, 0, 0, 0),
            Background = Brush("ControlBackgroundBrush"),
            BorderBrush = Brush("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(4),
        };

        var canvas = new Canvas { Width = 18, Height = 560, Background = Brushes.Transparent };
        var totalRows = Math.Max(1, state.Rows.Count);

        if (state.ChangeIndices.Count == 0)
        {
            var empty = new TextBlock
            {
                Text = "0",
                Foreground = Brush("MutedTextBrush"),
                FontSize = 10,
                Width = 18,
                TextAlignment = TextAlignment.Center,
            };
            Canvas.SetTop(empty, 4);
            canvas.Children.Add(empty);
        }
        else
        {
            foreach (var index in state.ChangeIndices)
            {
                var row = state.Rows[index];
                var marker = new Button
                {
                    Width = 10,
                    Height = 8,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0),
                    BorderThickness = new Thickness(0),
                    Background = MiniMapBrush(row.ChangeKind, row.CanOpenNestedDiff),
                    ToolTip = $"第 {index + 1} 行: {row.ChangeKindLabel}",
                    Cursor = Cursors.Hand,
                };
                marker.Click += (_, _) => ScrollToRow(state, index);
                var top = totalRows == 1 ? 0d : Math.Round((560d - 8d) * (index / (double)(totalRows - 1)));
                Canvas.SetLeft(marker, 4);
                Canvas.SetTop(marker, top);
                canvas.Children.Add(marker);
            }
        }

        border.Child = canvas;
        return border;
    }

    private async void CompareGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_compareService is null || sender is not DataGrid grid || grid.SelectedItem is not CompareRow row || !row.CanOpenNestedDiff || row.NestedCandidate is null)
        {
            return;
        }

        var child = new ZoomWindow { Owner = this };
        child.Show();
        var nested = CreateNestedState(row);
        await child.ShowDiffCompareAsync(
            nested.Item,
            _compareService,
            nested.Title,
            nested.Description,
            nested.Meta,
            nested.Depth,
            collapseUnchangedRows: true,
            baseTitle: nested.BaseTitle);
    }

    private void MoveToChange(CompareViewState state, int direction)
    {
        if (state.Grid is null || state.ChangeIndices.Count == 0)
        {
            return;
        }

        var current = state.Grid.SelectedIndex;
        int target;
        if (current < 0)
        {
            target = direction >= 0 ? state.ChangeIndices[0] : state.ChangeIndices[^1];
        }
        else if (direction >= 0)
        {
            var next = state.ChangeIndices.FirstOrDefault(i => i > current);
            target = next == 0 && state.ChangeIndices[0] != 0 ? state.ChangeIndices[0] : next;
        }
        else
        {
            var previous = state.ChangeIndices.LastOrDefault(i => i < current);
            target = previous == 0 && state.ChangeIndices[0] != 0 ? state.ChangeIndices[^1] : previous;
        }

        ScrollToRow(state, target);
    }

    private void ScrollToRow(CompareViewState state, int index)
    {
        if (state.Grid is null || index < 0 || index >= state.Rows.Count)
        {
            return;
        }

        state.Grid.SelectedIndex = index;
        state.Grid.ScrollIntoView(state.Rows[index]);
        state.Grid.Focus();
    }

    private static CompareViewState CreateNestedState(CompareRow row)
    {
        var nested = row.NestedCandidate!;
        var depth = row.ParentState.Depth + 1;
        return new CompareViewState(
            new DiffListItem
            {
                ChangeLabel = "嵌套",
                ItemType = nested.KindLabel,
                Path = row.ParentState.Item.Path,
                Name = $"{row.ParentState.Item.Name} -> 第 {row.LeftLineNumber}/{row.RightLineNumber} 行",
                OldValue = nested.LeftText,
                NewValue = nested.RightText,
            },
            $"{row.ParentState.BaseTitle} | 嵌套对照",
            $"{nested.KindLabel} 嵌套内容，第 {depth + 1} 层 compare。",
            $"{row.ParentState.Meta} | 嵌套行 {row.LeftLineNumber}/{row.RightLineNumber}".Trim(),
            $"第 {depth + 1} 层",
            $"正在进入第 {depth + 1} 层嵌套 compare...",
            depth,
            row.ParentState.BaseTitle,
            true);
    }

    private static CompareRow CreateRow(TextCompareLine line, CompareViewState state, TextCompareService compareService)
    {
        var nested = compareService.TryGetNestedCandidate(line);
        var canOpenNestedDiff = nested?.HasNestedDiff == true;
        var changed = line.ChangeKind != TextCompareChangeKind.Unchanged;

        return new CompareRow
        {
            LeftLineNumber = line.LeftLineNumber?.ToString() ?? string.Empty,
            LeftPreview = canOpenNestedDiff ? $"[双击打开] {Preview(line.LeftText)}" : Preview(line.LeftText),
            RightLineNumber = line.RightLineNumber?.ToString() ?? string.Empty,
            RightPreview = canOpenNestedDiff ? $"[双击打开] {Preview(line.RightText)}" : Preview(line.RightText),
            LeftBackground = canOpenNestedDiff ? Tint("#66355F85") : LeftBrush(line.ChangeKind),
            RightBackground = canOpenNestedDiff ? Tint("#66355F85") : RightBrush(line.ChangeKind),
            ParentState = state,
            IsChanged = changed,
            CanOpenNestedDiff = canOpenNestedDiff,
            ChangeKind = line.ChangeKind,
            ChangeKindLabel = canOpenNestedDiff ? $"可打开 {nested!.KindLabel} 变化" : line.ChangeKind switch
            {
                TextCompareChangeKind.Modified => "修改",
                TextCompareChangeKind.Added => "新增",
                TextCompareChangeKind.Removed => "删除",
                _ => "未变化",
            },
            NestedCandidate = nested,
        };
    }

    private static List<CompareRow> CollapseUnchanged(List<CompareRow> rows, CompareViewState state)
    {
        var result = new List<CompareRow>();
        var hidden = 0;

        foreach (var row in rows)
        {
            if (!row.IsChanged && !row.CanOpenNestedDiff)
            {
                hidden++;
                continue;
            }

            if (hidden > 0)
            {
                result.Add(CollapsedRow(hidden, state));
                hidden = 0;
            }

            result.Add(row);
        }

        if (hidden > 0)
        {
            result.Add(CollapsedRow(hidden, state));
        }

        return result;
    }

    private static CompareRow CollapsedRow(int hidden, CompareViewState state)
    {
        var text = $"... 已折叠 {hidden} 行未变化内容 ...";
        return new CompareRow
        {
            LeftLineNumber = string.Empty,
            LeftPreview = text,
            RightLineNumber = string.Empty,
            RightPreview = text,
            LeftBackground = Brushes.Transparent,
            RightBackground = Brushes.Transparent,
            ParentState = state,
            IsChanged = false,
            CanOpenNestedDiff = false,
            ChangeKind = TextCompareChangeKind.Unchanged,
            ChangeKindLabel = "折叠",
            NestedCandidate = null,
        };
    }

    private static DataGridTextColumn NumberColumn(string header, string path, double width)
    {
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(path),
            Width = width,
            ElementStyle = NumberStyle(),
            HeaderStyle = HeaderStyle(),
        };
    }

    private static DataGridTemplateColumn TextColumn(string header, string textPath, string backgroundPath)
    {
        return new DataGridTemplateColumn
        {
            Header = header,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            HeaderStyle = HeaderStyle(),
            CellTemplate = TextTemplate(textPath, backgroundPath),
        };
    }

    private static Style HeaderStyle()
    {
        var style = new Style(typeof(DataGridColumnHeader));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brush("HeaderBackgroundBrush")));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brush("PanelBorderBrush")));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brush("TextBrush")));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
        return style;
    }

    private static Style RowStyle()
    {
        var style = new Style(typeof(DataGridRow));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brush("TextBrush")));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brush("ControlBackgroundBrush")));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brush("PanelBorderBrush")));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));

        var trigger = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
        trigger.Setters.Add(new Setter(Control.BackgroundProperty, Brush("SelectionBrush")));
        trigger.Setters.Add(new Setter(Control.ForegroundProperty, Brush("TextBrush")));
        style.Triggers.Add(trigger);
        return style;
    }

    private static Style CellStyle()
    {
        var style = new Style(typeof(DataGridCell));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.ForegroundProperty, Brush("TextBrush")));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brush("PanelBorderBrush")));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));

        var trigger = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        trigger.Setters.Add(new Setter(Control.BackgroundProperty, Brush("SelectionBrush")));
        trigger.Setters.Add(new Setter(Control.ForegroundProperty, Brush("TextBrush")));
        style.Triggers.Add(trigger);
        return style;
    }

    private static Style NumberStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(8, 6, 8, 6)));
        style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brush("TextBrush")));
        return style;
    }

    private static DataTemplate TextTemplate(string textPath, string backgroundPath)
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetBinding(Border.BackgroundProperty, new Binding(backgroundPath));
        border.SetValue(Border.PaddingProperty, new Thickness(8, 6, 8, 6));

        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(textPath));
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(TextBlock.ForegroundProperty, Brush("TextBrush"));
        text.SetValue(TextBlock.FontWeightProperty, FontWeights.Medium);
        border.AppendChild(text);

        return new DataTemplate { VisualTree = border };
    }

    private static TextBlock Summary(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = Brush("MutedTextBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        };
    }

    private static Button Action(string label, RoutedEventHandler onClick)
    {
        var button = new Button
        {
            Content = label,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(16, 8, 16, 8),
        };
        button.Click += onClick;
        return button;
    }

    private void TryCopyToClipboard(string content, string label)
    {
        try
        {
            Clipboard.SetText(content);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                $"复制{label}失败：{ex.Message}",
                "复制失败",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private static Brush Brush(string key) => (Brush)Application.Current.FindResource(key);

    private static Brush LeftBrush(TextCompareChangeKind kind) => kind switch
    {
        TextCompareChangeKind.Removed => Tint("#55FF8585"),
        TextCompareChangeKind.Modified => Tint("#66FFD166"),
        _ => Brushes.Transparent,
    };

    private static Brush RightBrush(TextCompareChangeKind kind) => kind switch
    {
        TextCompareChangeKind.Added => Tint("#5535E1CF"),
        TextCompareChangeKind.Modified => Tint("#66FFD166"),
        _ => Brushes.Transparent,
    };

    private static Brush MiniMapBrush(TextCompareChangeKind kind, bool nestedDiff) => nestedDiff
        ? Brush("AccentBrush")
        : kind switch
        {
            TextCompareChangeKind.Modified => Brush("WarningBrush"),
            TextCompareChangeKind.Added => Brush("AccentBrush"),
            TextCompareChangeKind.Removed => Brush("DangerBrush"),
            _ => Brush("MutedTextBrush"),
        };

    private static Brush Tint(string hex) => (Brush)new BrushConverter().ConvertFrom(hex)!;

    private static string Preview(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= MaxPreviewLength ? text : $"{text[..MaxPreviewLength]} ... ({text.Length} chars)";
    }

    private static bool NeedsPreviewNotice(DiffListItem item, TextCompareResult? result)
    {
        return (item.OldValue?.Length ?? 0) > MaxPreviewLength
            || (item.NewValue?.Length ?? 0) > MaxPreviewLength
            || (result?.Lines.Any(static line => line.LeftText.Length > MaxPreviewLength || line.RightText.Length > MaxPreviewLength) ?? false);
    }

    private static string ResolveBaseTitle(string title)
    {
        return title.Contains('|', StringComparison.Ordinal)
            ? title[..title.IndexOf('|', StringComparison.Ordinal)].TrimEnd()
            : title;
    }

    private static FrameworkElement CreateErrorView(DiffListItem item, Exception ex)
    {
        var panel = new StackPanel();
        panel.Children.Add(Summary($"变化: {item.ChangeLabel} | 类型: {item.ItemType}"));
        panel.Children.Add(Summary($"路径: {item.Path}"));
        panel.Children.Add(Summary($"名称: {item.Name}"));
        panel.Children.Add(new TextBlock
        {
            Text = "生成 compare 视图时出错，已停止渲染。",
            Margin = new Thickness(0, 18, 0, 10),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("DangerBrush"),
        });
        panel.Children.Add(CreateReadonlyTextBox(ex.ToString()));
        return panel;
    }

    private static string FormatSnapshotTree(IEnumerable<SnapshotTreeItem> items)
    {
        var builder = new StringBuilder();
        foreach (var item in items)
        {
            AppendSnapshot(builder, item, 0);
        }
        return builder.ToString();
    }

    private static void AppendSnapshot(StringBuilder builder, SnapshotTreeItem item, int depth)
    {
        var indent = new string(' ', depth * 2);
        builder.Append(indent).Append("- ").AppendLine(item.Title);
        foreach (var line in item.DetailLines)
        {
            builder.Append(indent).Append("  ").AppendLine(line);
        }
        foreach (var child in item.Children)
        {
            AppendSnapshot(builder, child, depth + 1);
        }
    }

    private static string FormatDiffEntries(IEnumerable<DiffListItem> items)
    {
        var builder = new StringBuilder();
        var index = 1;
        foreach (var item in items)
        {
            builder.Append('#').Append(index++).AppendLine();
            builder.Append("变化: ").AppendLine(item.ChangeLabel);
            builder.Append("类型: ").AppendLine(item.ItemType);
            builder.Append("路径: ").AppendLine(item.Path);
            builder.Append("名称: ").AppendLine(item.Name);
            builder.Append("旧值: ").AppendLine(string.IsNullOrEmpty(item.OldValue) ? "(空)" : item.OldValue);
            builder.Append("新值: ").AppendLine(string.IsNullOrEmpty(item.NewValue) ? "(空)" : item.NewValue);
            builder.AppendLine(new string('-', 72));
        }
        return builder.Length == 0 ? "当前没有可复制的差异内容。" : builder.ToString();
    }

    private sealed class CompareRow
    {
        public required string LeftLineNumber { get; init; }
        public required string LeftPreview { get; init; }
        public required string RightLineNumber { get; init; }
        public required string RightPreview { get; init; }
        public required Brush LeftBackground { get; init; }
        public required Brush RightBackground { get; init; }
        public required CompareViewState ParentState { get; init; }
        public required bool IsChanged { get; init; }
        public required bool CanOpenNestedDiff { get; init; }
        public required TextCompareChangeKind ChangeKind { get; init; }
        public required string ChangeKindLabel { get; init; }
        public TextCompareNestedCandidate? NestedCandidate { get; init; }
    }

    private sealed record CompareViewState(
        DiffListItem Item,
        string Title,
        string Description,
        string Meta,
        string LevelLabel,
        string LoadingText,
        int Depth,
        string BaseTitle,
        bool CollapseUnchangedRows)
    {
        public TextCompareResult? Result { get; init; }
        public DataGrid? Grid { get; set; }
        public List<CompareRow> Rows { get; set; } = [];
        public List<int> ChangeIndices { get; set; } = [];
        public int ChangeCount => ChangeIndices.Count;
    }
}
