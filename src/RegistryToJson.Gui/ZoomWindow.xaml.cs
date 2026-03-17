using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Text;

namespace RegistryToJson.Gui;

public partial class ZoomWindow : Window
{
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

    private static Border WrapInPanel(Control content)
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
}
