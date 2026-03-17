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
    private readonly Stack<CompareViewState> _compareHistory = new();
    private TextCompareService? _compareService;
    private CompareViewState? _currentCompareState;

    public ZoomWindow()
    {
        InitializeComponent();
    }

    public void ShowSnapshotTree(IEnumerable<SnapshotTreeItem> items, string title, string description, string meta)
    {
        Title = title;
        WindowTitleTextBlock.Text = title;
        WindowDescriptionTextBlock.Text = description;
        WindowMetaTextBlock.Text = meta;
        ZoomContentHost.Content = WrapInPanel(CreateReadonlyTextBox(FormatSnapshotTree(items)));
    }

    public void ShowDiffEntries(IEnumerable<DiffListItem> items, string title, string description, string meta)
    {
        Title = title;
        WindowTitleTextBlock.Text = title;
        WindowDescriptionTextBlock.Text = description;
        WindowMetaTextBlock.Text = meta;
        ZoomContentHost.Content = WrapInPanel(CreateReadonlyTextBox(FormatDiffEntries(items)));
    }

    public async Task ShowDiffCompareAsync(DiffListItem item, TextCompareService compareService, string title, string description, string meta)
    {
        _compareService = compareService;
        _compareHistory.Clear();
        _currentCompareState = null;

        await LoadCompareStateAsync(new CompareViewState(
            item,
            title,
            description,
            meta,
            "根层",
            pushCurrentToHistory: false,
            loadingText: "正在生成左右对照视图..."));
    }

    private async Task LoadCompareStateAsync(CompareViewState state)
    {
        if (_compareService is null)
        {
            return;
        }

        UpdateWindowHeader(state.Title, state.Description, state.Meta);
        ZoomContentHost.Content = WrapInPanel(CreateLoadingView(state.Item, state.LoadingText));

        await Task.Yield();

        try
        {
            var compareResult = await Task.Run(() => _compareService.Compare(state.Item.OldValue, state.Item.NewValue));
            if (state.PushCurrentToHistory && _currentCompareState is not null)
            {
                _compareHistory.Push(_currentCompareState);
            }

            var resolvedState = state with { Result = compareResult };
            _currentCompareState = resolvedState;
            ZoomContentHost.Content = WrapInPanel(CreateCompareView(resolvedState, _compareService));
        }
        catch (Exception ex)
        {
            ZoomContentHost.Content = WrapInPanel(CreateErrorView(state.Item, ex));
        }
    }

    private void UpdateWindowHeader(string title, string description, string meta)
    {
        Title = title;
        WindowTitleTextBlock.Text = title;
        WindowDescriptionTextBlock.Text = description;
        WindowMetaTextBlock.Text = meta;
    }

    private static Border WrapInPanel(UIElement content)
    {
        return new Border
        {
            Background = (Brush)Application.Current.FindResource("PanelBackgroundBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("PanelBorderBrush"),
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
            Background = (Brush)Application.Current.FindResource("ControlBackgroundBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("PanelBorderBrush"),
            Foreground = (Brush)Application.Current.FindResource("TextBrush"),
            ContextMenu = CreateContextMenu(),
        };
    }

    private static ContextMenu CreateContextMenu()
    {
        var contextMenu = new ContextMenu();
        contextMenu.Items.Add(new MenuItem { Header = "复制", Command = ApplicationCommands.Copy });
        contextMenu.Items.Add(new MenuItem { Header = "全选", Command = ApplicationCommands.SelectAll });
        return contextMenu;
    }

    private static FrameworkElement CreateLoadingView(DiffListItem item, string loadingText)
    {
        var panel = new StackPanel();
        panel.Children.Add(CreateSummaryText($"变化: {item.ChangeLabel} | 类型: {item.ItemType}"));
        panel.Children.Add(CreateSummaryText($"路径: {item.Path}"));
        panel.Children.Add(CreateSummaryText($"名称: {item.Name}"));
        panel.Children.Add(new TextBlock
        {
            Text = loadingText,
            Margin = new Thickness(0, 18, 0, 10),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("TextBrush"),
        });
        panel.Children.Add(new ProgressBar
        {
            IsIndeterminate = true,
            Height = 6,
        });
        panel.Children.Add(CreateSummaryText("窗口会先打开，再在后台生成 compare，避免双击时主界面卡死。"));
        return panel;
    }

    private FrameworkElement CreateCompareView(CompareViewState state, TextCompareService compareService)
    {
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var summaryPanel = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 14),
        };
        summaryPanel.Children.Add(CreateSummaryText($"变化: {state.Item.ChangeLabel} | 类型: {state.Item.ItemType}"));
        summaryPanel.Children.Add(CreateSummaryText($"路径: {state.Item.Path}"));
        summaryPanel.Children.Add(CreateSummaryText($"名称: {state.Item.Name}"));
        summaryPanel.Children.Add(CreateSummaryText($"当前层级: {state.LevelLabel}"));
        summaryPanel.Children.Add(CreateSummaryText($"展示行数: {state.Result?.Lines.Count ?? 0}"));
        summaryPanel.Children.Add(CreateSummaryText($"变化行数: {state.ChangeCount}"));
        summaryPanel.Children.Add(CreateSummaryText("操作: 黄色/红色/青色代表变化，蓝色代表可继续下钻；右侧迷你地图可快速跳到变化位。"));

        if (NeedsPreviewNotice(state.Item, state.Result))
        {
            summaryPanel.Children.Add(CreateSummaryText("说明: 超长文本已在表格中截断预览，使用下方按钮可复制完整旧值/新值。"));
        }

        var actionPanel = new WrapPanel
        {
            Margin = new Thickness(0, 10, 0, 0),
        };
        if (_compareHistory.Count > 0)
        {
            actionPanel.Children.Add(CreateActionButton("返回上一级", async (_, _) => await NavigateBackAsync()));
        }

        actionPanel.Children.Add(CreateActionButton("上一处变化", (_, _) => MoveToChange(state, -1)));
        actionPanel.Children.Add(CreateActionButton("下一处变化", (_, _) => MoveToChange(state, 1)));
        actionPanel.Children.Add(CreateActionButton("复制旧值全文", (_, _) => Clipboard.SetText(string.IsNullOrEmpty(state.Item.OldValue) ? "(空)" : state.Item.OldValue)));
        actionPanel.Children.Add(CreateActionButton("复制新值全文", (_, _) => Clipboard.SetText(string.IsNullOrEmpty(state.Item.NewValue) ? "(空)" : state.Item.NewValue)));
        summaryPanel.Children.Add(actionPanel);
        layout.Children.Add(summaryPanel);

        var compareHost = CreateCompareHost(state, compareService);
        Grid.SetRow(compareHost, 1);
        layout.Children.Add(compareHost);
        return layout;
    }

    private FrameworkElement CreateCompareHost(CompareViewState state, TextCompareService compareService)
    {
        var host = new Grid();
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var grid = CreateCompareGrid(state, compareService);
        state.Grid = grid;
        host.Children.Add(grid);

        var miniMap = CreateMiniMap(state);
        Grid.SetColumn(miniMap, 1);
        host.Children.Add(miniMap);
        return host;
    }

    private FrameworkElement CreateMiniMap(CompareViewState state)
    {
        var miniMapBorder = new Border
        {
            Width = 28,
            Margin = new Thickness(10, 0, 0, 0),
            Background = (Brush)Application.Current.FindResource("ControlBackgroundBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(4),
        };

        var canvas = new Canvas
        {
            Width = 18,
            Background = Brushes.Transparent,
        };

        var totalRows = Math.Max(1, state.Rows.Count);
        const double mapHeight = 560;
        canvas.Height = mapHeight;

        if (state.ChangeIndices.Count == 0)
        {
            var emptyText = new TextBlock
            {
                Text = "0",
                Foreground = (Brush)Application.Current.FindResource("MutedTextBrush"),
                FontSize = 10,
                Width = 18,
                TextAlignment = TextAlignment.Center,
            };
            Canvas.SetTop(emptyText, 4);
            canvas.Children.Add(emptyText);
        }
        else
        {
            foreach (var changeIndex in state.ChangeIndices)
            {
                var row = state.Rows[changeIndex];
                var marker = new Button
                {
                    Width = 10,
                    Height = 8,
                    Padding = new Thickness(0),
                    Margin = new Thickness(0),
                    BorderThickness = new Thickness(0),
                    Background = GetMiniMapBrush(row.ChangeKind, row.CanDrillDown),
                    ToolTip = $"第 {changeIndex + 1} 行: {row.ChangeKindLabel}",
                    Tag = changeIndex,
                    Cursor = Cursors.Hand,
                };
                marker.Click += (_, _) => ScrollToRow(state, changeIndex);

                var top = totalRows == 1
                    ? 0d
                    : Math.Round((mapHeight - 8) * (changeIndex / (double)(totalRows - 1)));
                Canvas.SetTop(marker, top);
                Canvas.SetLeft(marker, 4);
                canvas.Children.Add(marker);
            }
        }

        miniMapBorder.Child = canvas;
        return miniMapBorder;
    }

    private static FrameworkElement CreateErrorView(DiffListItem item, Exception ex)
    {
        var panel = new StackPanel();
        panel.Children.Add(CreateSummaryText($"变化: {item.ChangeLabel} | 类型: {item.ItemType}"));
        panel.Children.Add(CreateSummaryText($"路径: {item.Path}"));
        panel.Children.Add(CreateSummaryText($"名称: {item.Name}"));
        panel.Children.Add(new TextBlock
        {
            Text = "生成 compare 视图时出错，已停止渲染。",
            Margin = new Thickness(0, 18, 0, 10),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("DangerBrush"),
        });
        panel.Children.Add(CreateReadonlyTextBox(ex.ToString()));
        return panel;
    }

    private static TextBlock CreateSummaryText(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = (Brush)Application.Current.FindResource("MutedTextBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4),
        };
    }

    private static Button CreateActionButton(string label, RoutedEventHandler onClick)
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

    private DataGrid CreateCompareGrid(CompareViewState state, TextCompareService compareService)
    {
        var rows = (state.Result?.Lines ?? []).Select(line => CreateCompareRowViewModel(line, state, compareService)).ToList();
        state.Rows = rows;
        state.ChangeIndices = rows
            .Select((row, index) => (row, index))
            .Where(static pair => pair.row.IsChanged || pair.row.CanDrillDown)
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
            Background = (Brush)Application.Current.FindResource("ControlBackgroundBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            Foreground = (Brush)Application.Current.FindResource("TextBrush"),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 13,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            EnableRowVirtualization = true,
            EnableColumnVirtualization = true,
            RowBackground = (Brush)Application.Current.FindResource("ControlBackgroundBrush"),
            AlternatingRowBackground = (Brush)Application.Current.FindResource("PanelBackgroundBrush"),
            HorizontalGridLinesBrush = (Brush)Application.Current.FindResource("PanelBorderBrush"),
            VerticalGridLinesBrush = (Brush)Application.Current.FindResource("PanelBorderBrush"),
            AlternationCount = 2,
            ItemsSource = rows,
        };

        grid.MouseDoubleClick += CompareGrid_MouseDoubleClick;
        VirtualizingPanel.SetIsVirtualizing(grid, true);
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Recycling);
        ScrollViewer.SetCanContentScroll(grid, true);
        grid.RowStyle = CreateRowStyle();
        grid.CellStyle = CreateCellStyle();

        grid.Columns.Add(CreateNumberColumn("旧行", nameof(CompareRowViewModel.LeftLineNumber), 68));
        grid.Columns.Add(CreateTextColumn("旧值", nameof(CompareRowViewModel.LeftPreview), nameof(CompareRowViewModel.LeftBackground)));
        grid.Columns.Add(CreateNumberColumn("新行", nameof(CompareRowViewModel.RightLineNumber), 68));
        grid.Columns.Add(CreateTextColumn("新值", nameof(CompareRowViewModel.RightPreview), nameof(CompareRowViewModel.RightBackground)));
        return grid;
    }

    private async void CompareGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_compareService is null || sender is not DataGrid grid || grid.SelectedItem is not CompareRowViewModel row || row.NestedCandidate is null)
        {
            return;
        }

        var nestedState = CreateNestedState(row);
        await LoadCompareStateAsync(nestedState);
    }

    private async Task NavigateBackAsync()
    {
        if (_compareHistory.Count == 0)
        {
            return;
        }

        var previousState = _compareHistory.Pop();
        _currentCompareState = previousState;
        UpdateWindowHeader(previousState.Title, previousState.Description, previousState.Meta);
        ZoomContentHost.Content = WrapInPanel(CreateCompareView(previousState, _compareService!));
        await Task.CompletedTask;
    }

    private void MoveToChange(CompareViewState state, int direction)
    {
        if (state.Grid is null || state.ChangeIndices.Count == 0)
        {
            return;
        }

        var currentIndex = state.Grid.SelectedIndex;
        int targetIndex;

        if (currentIndex < 0)
        {
            targetIndex = direction >= 0 ? state.ChangeIndices[0] : state.ChangeIndices[^1];
        }
        else if (direction >= 0)
        {
            targetIndex = state.ChangeIndices.FirstOrDefault(index => index > currentIndex, state.ChangeIndices[0]);
        }
        else
        {
            targetIndex = state.ChangeIndices.LastOrDefault(index => index < currentIndex, state.ChangeIndices[^1]);
        }

        ScrollToRow(state, targetIndex);
    }

    private void ScrollToRow(CompareViewState state, int rowIndex)
    {
        if (state.Grid is null || rowIndex < 0 || rowIndex >= state.Rows.Count)
        {
            return;
        }

        state.Grid.SelectedIndex = rowIndex;
        state.Grid.ScrollIntoView(state.Rows[rowIndex]);
        state.Grid.Focus();
    }

    private static CompareViewState CreateNestedState(CompareRowViewModel row)
    {
        var nested = row.NestedCandidate!;
        var depth = row.ParentState.Depth + 1;
        var title = $"{row.ParentState.BaseTitle} | 嵌套对照";
        var description = $"{nested.KindLabel} 嵌套内容，第 {depth + 1} 层 compare。";
        var meta = $"{row.ParentState.Meta} | 嵌套行 {row.LeftLineNumber}/{row.RightLineNumber}".Trim();
        var item = new DiffListItem
        {
            ChangeLabel = "嵌套",
            ItemType = nested.KindLabel,
            Path = row.ParentState.Item.Path,
            Name = $"{row.ParentState.Item.Name} -> 第 {row.LeftLineNumber}/{row.RightLineNumber} 行",
            OldValue = nested.LeftText,
            NewValue = nested.RightText,
        };

        return new CompareViewState(
            item,
            title,
            description,
            meta,
            $"第 {depth + 1} 层",
            pushCurrentToHistory: true,
            loadingText: $"正在进入第 {depth + 1} 层嵌套 compare...")
        {
            Depth = depth,
            BaseTitle = row.ParentState.BaseTitle,
        };
    }

    private static DataGridTextColumn CreateNumberColumn(string header, string bindingPath, double width)
    {
        return new DataGridTextColumn
        {
            Header = header,
            Binding = new Binding(bindingPath),
            Width = width,
            ElementStyle = CreateNumberCellStyle(),
            HeaderStyle = CreateHeaderStyle(),
        };
    }

    private static DataGridTemplateColumn CreateTextColumn(string header, string textPath, string backgroundPath)
    {
        return new DataGridTemplateColumn
        {
            Header = header,
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            HeaderStyle = CreateHeaderStyle(),
            CellTemplate = CreateTextCellTemplate(textPath, backgroundPath),
        };
    }

    private static Style CreateHeaderStyle()
    {
        var style = new Style(typeof(DataGridColumnHeader));
        style.Setters.Add(new Setter(Control.BackgroundProperty, (Brush)Application.Current.FindResource("HeaderBackgroundBrush")));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, (Brush)Application.Current.FindResource("PanelBorderBrush")));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
        style.Setters.Add(new Setter(Control.ForegroundProperty, (Brush)Application.Current.FindResource("TextBrush")));
        style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)));
        return style;
    }

    private static Style CreateRowStyle()
    {
        var style = new Style(typeof(DataGridRow));
        style.Setters.Add(new Setter(Control.ForegroundProperty, (Brush)Application.Current.FindResource("TextBrush")));
        style.Setters.Add(new Setter(Control.BackgroundProperty, (Brush)Application.Current.FindResource("ControlBackgroundBrush")));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, (Brush)Application.Current.FindResource("PanelBorderBrush")));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));

        var selectedTrigger = new Trigger
        {
            Property = DataGridRow.IsSelectedProperty,
            Value = true,
        };
        selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, (Brush)Application.Current.FindResource("SelectionBrush")));
        selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, (Brush)Application.Current.FindResource("TextBrush")));
        style.Triggers.Add(selectedTrigger);
        return style;
    }

    private static Style CreateCellStyle()
    {
        var style = new Style(typeof(DataGridCell));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.ForegroundProperty, (Brush)Application.Current.FindResource("TextBrush")));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, (Brush)Application.Current.FindResource("PanelBorderBrush")));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));

        var selectedTrigger = new Trigger
        {
            Property = DataGridCell.IsSelectedProperty,
            Value = true,
        };
        selectedTrigger.Setters.Add(new Setter(Control.BackgroundProperty, (Brush)Application.Current.FindResource("SelectionBrush")));
        selectedTrigger.Setters.Add(new Setter(Control.ForegroundProperty, (Brush)Application.Current.FindResource("TextBrush")));
        style.Triggers.Add(selectedTrigger);
        return style;
    }

    private static Style CreateNumberCellStyle()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.MarginProperty, new Thickness(8, 6, 8, 6)));
        style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(TextBlock.ForegroundProperty, (Brush)Application.Current.FindResource("TextBrush")));
        return style;
    }

    private static DataTemplate CreateTextCellTemplate(string textPath, string backgroundPath)
    {
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.SetBinding(Border.BackgroundProperty, new Binding(backgroundPath));
        borderFactory.SetValue(Border.PaddingProperty, new Thickness(8, 6, 8, 6));

        var textFactory = new FrameworkElementFactory(typeof(TextBlock));
        textFactory.SetBinding(TextBlock.TextProperty, new Binding(textPath));
        textFactory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
        textFactory.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        textFactory.SetValue(TextBlock.ForegroundProperty, (Brush)Application.Current.FindResource("TextBrush"));
        textFactory.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        textFactory.SetValue(TextBlock.FontWeightProperty, FontWeights.Medium);
        borderFactory.AppendChild(textFactory);

        return new DataTemplate
        {
            VisualTree = borderFactory,
        };
    }

    private static CompareRowViewModel CreateCompareRowViewModel(TextCompareLine line, CompareViewState parentState, TextCompareService compareService)
    {
        var nestedCandidate = compareService.TryGetNestedCandidate(line);
        var isChanged = line.ChangeKind != TextCompareChangeKind.Unchanged;
        var leftBackground = nestedCandidate is not null
            ? CreateTintBrush("#66355F85")
            : GetLeftBackground(line.ChangeKind);
        var rightBackground = nestedCandidate is not null
            ? CreateTintBrush("#66355F85")
            : GetRightBackground(line.ChangeKind);

        return new CompareRowViewModel
        {
            LeftLineNumber = line.LeftLineNumber?.ToString() ?? string.Empty,
            LeftPreview = nestedCandidate is null ? ToPreview(line.LeftText) : $"[双击继续下钻] {ToPreview(line.LeftText)}",
            RightLineNumber = line.RightLineNumber?.ToString() ?? string.Empty,
            RightPreview = nestedCandidate is null ? ToPreview(line.RightText) : $"[双击继续下钻] {ToPreview(line.RightText)}",
            LeftBackground = leftBackground,
            RightBackground = rightBackground,
            NestedCandidate = nestedCandidate,
            ParentState = parentState,
            IsChanged = isChanged,
            CanDrillDown = nestedCandidate is not null,
            ChangeKind = line.ChangeKind,
            ChangeKindLabel = nestedCandidate is not null ? $"可下钻 {nestedCandidate.KindLabel}" : line.ChangeKind switch
            {
                TextCompareChangeKind.Modified => "修改",
                TextCompareChangeKind.Added => "新增",
                TextCompareChangeKind.Removed => "删除",
                _ => "未变化",
            },
        };
    }

    private static Brush GetMiniMapBrush(TextCompareChangeKind changeKind, bool canDrillDown)
    {
        if (canDrillDown)
        {
            return (Brush)Application.Current.FindResource("AccentBrush");
        }

        return changeKind switch
        {
            TextCompareChangeKind.Modified => (Brush)Application.Current.FindResource("WarningBrush"),
            TextCompareChangeKind.Added => (Brush)Application.Current.FindResource("AccentBrush"),
            TextCompareChangeKind.Removed => (Brush)Application.Current.FindResource("DangerBrush"),
            _ => (Brush)Application.Current.FindResource("MutedTextBrush"),
        };
    }

    private static string ToPreview(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= MaxPreviewLength
            ? text
            : $"{text[..MaxPreviewLength]} ... ({text.Length} chars)";
    }

    private static bool NeedsPreviewNotice(DiffListItem item, TextCompareResult? compareResult)
    {
        return (item.OldValue?.Length ?? 0) > MaxPreviewLength
            || (item.NewValue?.Length ?? 0) > MaxPreviewLength
            || (compareResult?.Lines.Any(static line => line.LeftText.Length > MaxPreviewLength || line.RightText.Length > MaxPreviewLength) ?? false);
    }

    private static Brush GetLeftBackground(TextCompareChangeKind kind)
    {
        return kind switch
        {
            TextCompareChangeKind.Removed => CreateTintBrush("#55FF8585"),
            TextCompareChangeKind.Modified => CreateTintBrush("#66FFD166"),
            _ => Brushes.Transparent,
        };
    }

    private static Brush GetRightBackground(TextCompareChangeKind kind)
    {
        return kind switch
        {
            TextCompareChangeKind.Added => CreateTintBrush("#5535E1CF"),
            TextCompareChangeKind.Modified => CreateTintBrush("#66FFD166"),
            _ => Brushes.Transparent,
        };
    }

    private static Brush CreateTintBrush(string hex)
    {
        return (Brush)new BrushConverter().ConvertFrom(hex)!;
    }

    private static string FormatSnapshotTree(IEnumerable<SnapshotTreeItem> items)
    {
        var builder = new StringBuilder();
        foreach (var item in items)
        {
            AppendSnapshotTreeItem(builder, item, 0);
        }

        return builder.ToString();
    }

    private static void AppendSnapshotTreeItem(StringBuilder builder, SnapshotTreeItem item, int depth)
    {
        var indent = new string(' ', depth * 2);
        builder.Append(indent).Append("- ").AppendLine(item.Title);

        foreach (var detailLine in item.DetailLines)
        {
            builder.Append(indent).Append("  ").AppendLine(detailLine);
        }

        foreach (var child in item.Children)
        {
            AppendSnapshotTreeItem(builder, child, depth + 1);
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

    private sealed class CompareRowViewModel
    {
        public required string LeftLineNumber { get; init; }

        public required string LeftPreview { get; init; }

        public required string RightLineNumber { get; init; }

        public required string RightPreview { get; init; }

        public required Brush LeftBackground { get; init; }

        public required Brush RightBackground { get; init; }

        public TextCompareNestedCandidate? NestedCandidate { get; init; }

        public required CompareViewState ParentState { get; init; }

        public required bool IsChanged { get; init; }

        public required bool CanDrillDown { get; init; }

        public required TextCompareChangeKind ChangeKind { get; init; }

        public required string ChangeKindLabel { get; init; }
    }

    private sealed record CompareViewState(
        DiffListItem Item,
        string Title,
        string Description,
        string Meta,
        string LevelLabel,
        bool pushCurrentToHistory,
        string loadingText)
    {
        public TextCompareResult? Result { get; init; }

        public int Depth { get; init; }

        public string BaseTitle { get; init; } = Title.Contains('|', StringComparison.Ordinal)
            ? Title[..Title.IndexOf('|', StringComparison.Ordinal)].TrimEnd()
            : Title;

        public bool PushCurrentToHistory => pushCurrentToHistory;

        public string LoadingText => loadingText;

        public DataGrid? Grid { get; set; }

        public List<CompareRowViewModel> Rows { get; set; } = [];

        public List<int> ChangeIndices { get; set; } = [];

        public int ChangeCount => ChangeIndices.Count;
    }
}
