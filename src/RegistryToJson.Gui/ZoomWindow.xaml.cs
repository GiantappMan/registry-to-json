using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Text;
using RegistryToJson.Core;

namespace RegistryToJson.Gui;

public partial class ZoomWindow : Window
{
    private const int MaxPreviewLength = 240;

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
        Title = title;
        WindowTitleTextBlock.Text = title;
        WindowDescriptionTextBlock.Text = description;
        WindowMetaTextBlock.Text = meta;
        ZoomContentHost.Content = WrapInPanel(CreateLoadingView(item));

        await Task.Yield();

        try
        {
            var compareResult = await Task.Run(() => compareService.Compare(item.OldValue, item.NewValue));
            ZoomContentHost.Content = WrapInPanel(CreateCompareView(item, compareResult));
        }
        catch (Exception ex)
        {
            ZoomContentHost.Content = WrapInPanel(CreateErrorView(item, ex));
        }
    }

    private static Border WrapInPanel(UIElement content)
    {
        return new Border
        {
            Background = (System.Windows.Media.Brush)Application.Current.FindResource("PanelBackgroundBrush"),
            BorderBrush = (System.Windows.Media.Brush)Application.Current.FindResource("PanelBorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(18),
            Child = content,
        };
    }

    private static TextBox CreateReadonlyTextBox(string content)
    {
        var textBox = new TextBox
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

        return textBox;
    }

    private static ContextMenu CreateContextMenu()
    {
        var contextMenu = new ContextMenu();
        contextMenu.Items.Add(new MenuItem { Header = "复制", Command = ApplicationCommands.Copy });
        contextMenu.Items.Add(new MenuItem { Header = "全选", Command = ApplicationCommands.SelectAll });
        return contextMenu;
    }

    private static FrameworkElement CreateLoadingView(DiffListItem item)
    {
        var panel = new StackPanel();
        panel.Children.Add(CreateSummaryText($"变化: {item.ChangeLabel} | 类型: {item.ItemType}"));
        panel.Children.Add(CreateSummaryText($"路径: {item.Path}"));
        panel.Children.Add(CreateSummaryText($"名称: {item.Name}"));
        panel.Children.Add(new TextBlock
        {
            Text = "正在生成左右对照视图...",
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

    private static FrameworkElement CreateCompareView(DiffListItem item, TextCompareResult compareResult)
    {
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var summaryPanel = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 14),
        };
        summaryPanel.Children.Add(CreateSummaryText($"变化: {item.ChangeLabel} | 类型: {item.ItemType}"));
        summaryPanel.Children.Add(CreateSummaryText($"路径: {item.Path}"));
        summaryPanel.Children.Add(CreateSummaryText($"名称: {item.Name}"));
        summaryPanel.Children.Add(CreateSummaryText($"展示行数: {compareResult.Lines.Count}"));
        if (NeedsPreviewNotice(item, compareResult))
        {
            summaryPanel.Children.Add(CreateSummaryText($"说明: 超长文本已在表格中截断预览，使用下方按钮可复制完整旧值/新值。"));
        }

        var actionPanel = new WrapPanel
        {
            Margin = new Thickness(0, 10, 0, 0),
        };
        actionPanel.Children.Add(CreateCopyButton("复制旧值全文", string.IsNullOrEmpty(item.OldValue) ? "(空)" : item.OldValue));
        actionPanel.Children.Add(CreateCopyButton("复制新值全文", string.IsNullOrEmpty(item.NewValue) ? "(空)" : item.NewValue));
        summaryPanel.Children.Add(actionPanel);
        layout.Children.Add(summaryPanel);

        var grid = CreateCompareGrid(compareResult.Lines);
        Grid.SetRow(grid, 1);
        layout.Children.Add(grid);
        return layout;
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

    private static Button CreateCopyButton(string label, string content)
    {
        var button = new Button
        {
            Content = label,
            Margin = new Thickness(0, 0, 8, 8),
            Padding = new Thickness(16, 8, 16, 8),
        };
        button.Click += (_, _) => Clipboard.SetText(content);
        return button;
    }

    private static DataGrid CreateCompareGrid(IReadOnlyList<TextCompareLine> lines)
    {
        var rows = lines.Select(CreateCompareRowViewModel).ToList();
        var grid = new DataGrid
        {
            AutoGenerateColumns = false,
            IsReadOnly = true,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            CanUserReorderColumns = false,
            CanUserResizeRows = false,
            SelectionMode = DataGridSelectionMode.Extended,
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
        borderFactory.AppendChild(textFactory);

        return new DataTemplate
        {
            VisualTree = borderFactory,
        };
    }

    private static CompareRowViewModel CreateCompareRowViewModel(TextCompareLine line)
    {
        return new CompareRowViewModel
        {
            LeftLineNumber = line.LeftLineNumber?.ToString() ?? string.Empty,
            LeftPreview = ToPreview(line.LeftText),
            RightLineNumber = line.RightLineNumber?.ToString() ?? string.Empty,
            RightPreview = ToPreview(line.RightText),
            LeftBackground = GetLeftBackground(line.ChangeKind),
            RightBackground = GetRightBackground(line.ChangeKind),
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

    private static bool NeedsPreviewNotice(DiffListItem item, TextCompareResult compareResult)
    {
        return (item.OldValue?.Length ?? 0) > MaxPreviewLength
            || (item.NewValue?.Length ?? 0) > MaxPreviewLength
            || compareResult.Lines.Any(static line => line.LeftText.Length > MaxPreviewLength || line.RightText.Length > MaxPreviewLength);
    }

    private static Brush GetLeftBackground(TextCompareChangeKind kind)
    {
        return kind switch
        {
            TextCompareChangeKind.Removed => CreateTintBrush("#4DFF8585"),
            TextCompareChangeKind.Modified => CreateTintBrush("#4DFFD166"),
            _ => Brushes.Transparent,
        };
    }

    private static Brush GetRightBackground(TextCompareChangeKind kind)
    {
        return kind switch
        {
            TextCompareChangeKind.Added => CreateTintBrush("#4D35E1CF"),
            TextCompareChangeKind.Modified => CreateTintBrush("#4DFFD166"),
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
    }
}
