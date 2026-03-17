using Microsoft.Win32;
using RegistryToJson.Core;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace RegistryToJson.Gui;

public partial class MainWindow : Window
{
    private readonly RegistrySnapshotService _snapshotService = new();
    private readonly RegistryWatchService _watchService;
    private readonly DispatcherTimer _watchTimer;
    private RegistrySnapshot? _baselineSnapshot;
    private RegistrySnapshot? _currentSnapshot;
    private bool _suppressSettingsPersistence;
    private bool _suppressWatchToggleHandling;

    public ObservableCollection<SnapshotTreeItem> RootItems { get; } = [];

    public MainWindow()
    {
        _watchService = new RegistryWatchService(_snapshotService, new RegistryDiffService());
        _watchTimer = new DispatcherTimer();
        _watchTimer.Tick += WatchTimer_Tick;

        InitializeComponent();
        DataContext = this;

        RegisterPersistenceHandlers();
        RestorePersistedSettings();
        EnsureValidInitialInterval();

        Closing += MainWindow_Closing;

        if (WatchCheckBox.IsChecked == true)
        {
            Loaded += MainWindow_Loaded;
        }
    }

    private async void RefreshSnapshot_Click(object sender, RoutedEventArgs e)
    {
        await RefreshSnapshotAsync(updateStatusOnNoBaseline: true);
    }

    private async void ExportJson_Click(object sender, RoutedEventArgs e)
    {
        var registryPath = RegistryPathTextBox.Text.Trim();
        var outputPath = OutputPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(registryPath) || string.IsNullOrWhiteSpace(outputPath))
        {
            SetStatus("Missing Input", "请同时提供注册表路径和输出文件。", isError: true);
            return;
        }

        try
        {
            await Task.Run(() => _snapshotService.Export(new ExportRequest { RegistryPath = registryPath, OutputFilePath = outputPath }));
            SetStatus("Exported", $"已写出 JSON: {outputPath}");
        }
        catch (Exception ex)
        {
            SetStatus("Export Failed", ex.Message, isError: true);
        }
    }

    private void BrowseOutputPath_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = "registry-export.json",
            AddExtension = true,
            DefaultExt = ".json",
        };

        if (dialog.ShowDialog(this) == true)
        {
            OutputPathTextBox.Text = dialog.FileName;
            PersistSettings();
        }
    }

    private void PinBaseline_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSnapshot is null)
        {
            SetStatus("No Snapshot", "请先读取当前快照，再保留基线。", isError: true);
            return;
        }

        _baselineSnapshot = _currentSnapshot;
        BaselineSummaryTextBlock.Text = $"基线已保留: {_baselineSnapshot.SourcePath} @ {ToLocalText(_baselineSnapshot.CapturedAtUtc)}";
        DiffMetaTextBlock.Text = "后续刷新或 watch 将与当前基线比较";
        SetStatus("Baseline Pinned", "已保留当前快照为比较基线。");
    }

    private void ReplaceBaseline_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSnapshot is null)
        {
            SetStatus("No Snapshot", "当前没有可替换的快照。", isError: true);
            return;
        }

        _baselineSnapshot = _currentSnapshot;
        BaselineSummaryTextBlock.Text = $"基线已更新: {_baselineSnapshot.SourcePath} @ {ToLocalText(_baselineSnapshot.CapturedAtUtc)}";
        ApplyDiffResult(new RegistryDiffResult { ComparedAtUtc = DateTime.UtcNow });
        SetStatus("Baseline Replaced", "已用当前快照替换基线。");
    }

    private async void WatchCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressWatchToggleHandling)
        {
            return;
        }

        if (WatchCheckBox.IsChecked == true)
        {
            if (!await StartWatchAsync())
            {
                WatchCheckBox.IsChecked = false;
                return;
            }
        }
        else
        {
            StopWatch(updateStatus: true);
        }

        PersistSettings();
    }

    private async void WatchTimer_Tick(object? sender, EventArgs e)
    {
        await RefreshSnapshotAsync(updateStatusOnNoBaseline: false);
    }

    private async Task RefreshSnapshotAsync(bool updateStatusOnNoBaseline)
    {
        var registryPath = RegistryPathTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(registryPath))
        {
            SetStatus("Missing Path", "请填写注册表路径。", isError: true);
            return;
        }

        try
        {
            SetStatus("Refreshing", $"正在读取 {registryPath}", isWarning: true);
            var result = await Task.Run(() => _watchService.Refresh(registryPath, _baselineSnapshot));
            _currentSnapshot = result.CurrentSnapshot;
            RenderSnapshot(result.CurrentSnapshot);
            ApplyDiffResult(result.Diff);

            if (_baselineSnapshot is null)
            {
                if (updateStatusOnNoBaseline)
                {
                    SetStatus("Snapshot Loaded", "已读取当前快照。可点击“保留为基线”开始比较。");
                }
            }
            else
            {
                var diffCount = result.Diff.Entries.Count;
                SetStatus(diffCount == 0 ? "No Changes" : "Changes Detected", diffCount == 0 ? "当前快照与基线一致。" : $"检测到 {diffCount} 条变化。", isWarning: diffCount > 0);
            }
        }
        catch (Exception ex)
        {
            SetStatus("Refresh Failed", ex.Message, isError: true);
        }
    }

    private void RenderSnapshot(RegistrySnapshot snapshot)
    {
        RootItems.Clear();
        RootItems.Add(BuildTreeItem(snapshot.Root));
        SnapshotMetaTextBlock.Text = $"{snapshot.SourcePath} | {ToLocalText(snapshot.CapturedAtUtc)}";
    }

    private SnapshotTreeItem BuildTreeItem(RegistryNodeSnapshot node)
    {
        var item = new SnapshotTreeItem { Title = node.Name };
        item.DetailLines.Add($"Path: {node.FullPath}");
        foreach (var value in node.Values)
        {
            item.DetailLines.Add($"[{value.Kind}] {value.Name} = {value.Data}");
        }

        foreach (var child in node.Children)
        {
            item.Children.Add(BuildTreeItem(child));
        }

        return item;
    }

    private void ApplyDiffResult(RegistryDiffResult diff)
    {
        var entries = diff.Entries.Select(entry => new DiffListItem
        {
            ChangeLabel = entry.ChangeType switch
            {
                RegistryChangeType.Added => "新增",
                RegistryChangeType.Removed => "删除",
                _ => "修改",
            },
            ItemType = entry.ItemType,
            Path = entry.Path,
            Name = entry.Name ?? string.Empty,
            OldValue = entry.OldValue ?? string.Empty,
            NewValue = entry.NewValue ?? string.Empty,
        }).ToList();

        DiffListView.ItemsSource = entries;
        AddedCountTextBlock.Text = entries.Count(static item => item.ChangeLabel == "新增").ToString();
        ModifiedCountTextBlock.Text = entries.Count(static item => item.ChangeLabel == "修改").ToString();
        RemovedCountTextBlock.Text = entries.Count(static item => item.ChangeLabel == "删除").ToString();
        DiffMetaTextBlock.Text = _baselineSnapshot is null ? "保留基线后可查看与当前快照的差异" : $"最近比较时间: {ToLocalText(diff.ComparedAtUtc)}";
    }

    private bool UpdateWatchTimerInterval()
    {
        if (!int.TryParse(IntervalTextBox.Text.Trim(), out var seconds) || seconds <= 0)
        {
            SetStatus("Invalid Interval", "watch 间隔必须是正整数秒。", isError: true);
            return false;
        }

        _watchTimer.Interval = TimeSpan.FromSeconds(seconds);
        return true;
    }

    private void RegisterPersistenceHandlers()
    {
        RegistryPathTextBox.TextChanged += PersistedInput_OnChanged;
        OutputPathTextBox.TextChanged += PersistedInput_OnChanged;
        IntervalTextBox.TextChanged += PersistedInput_OnChanged;
    }

    private void RestorePersistedSettings()
    {
        var settings = GuiSettingsStore.Load();

        _suppressSettingsPersistence = true;
        _suppressWatchToggleHandling = true;

        RegistryPathTextBox.Text = string.IsNullOrWhiteSpace(settings.RegistryPath)
            ? RegistryPathTextBox.Text
            : settings.RegistryPath;
        OutputPathTextBox.Text = settings.OutputPath;
        IntervalTextBox.Text = string.IsNullOrWhiteSpace(settings.IntervalText)
            ? IntervalTextBox.Text
            : settings.IntervalText;
        WatchCheckBox.IsChecked = settings.WatchEnabled;

        _suppressWatchToggleHandling = false;
        _suppressSettingsPersistence = false;
    }

    private void EnsureValidInitialInterval()
    {
        if (UpdateWatchTimerInterval())
        {
            return;
        }

        _suppressSettingsPersistence = true;
        IntervalTextBox.Text = "2";
        _suppressSettingsPersistence = false;
        UpdateWatchTimerInterval();
        PersistSettings();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;

        if (WatchCheckBox.IsChecked != true)
        {
            return;
        }

        if (!await StartWatchAsync())
        {
            _suppressWatchToggleHandling = true;
            WatchCheckBox.IsChecked = false;
            _suppressWatchToggleHandling = false;
            PersistSettings();
        }
    }

    private async Task<bool> StartWatchAsync()
    {
        if (!UpdateWatchTimerInterval())
        {
            return false;
        }

        _watchTimer.Start();
        SetStatus("Watch Enabled", "watch 已启动，正在定时刷新。", isWarning: true);
        await RefreshSnapshotAsync(updateStatusOnNoBaseline: false);
        return true;
    }

    private void StopWatch(bool updateStatus)
    {
        _watchTimer.Stop();
        if (updateStatus)
        {
            SetStatus("Watch Disabled", "watch 已停止。");
        }
    }

    private void PersistedInput_OnChanged(object sender, RoutedEventArgs e)
    {
        if (sender == IntervalTextBox && _watchTimer.IsEnabled)
        {
            _watchTimer.Interval = int.TryParse(IntervalTextBox.Text.Trim(), out var seconds) && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : _watchTimer.Interval;
        }

        PersistSettings();
    }

    private void PersistSettings()
    {
        if (_suppressSettingsPersistence)
        {
            return;
        }

        try
        {
            GuiSettingsStore.Save(new GuiSettings
            {
                RegistryPath = RegistryPathTextBox.Text.Trim(),
                OutputPath = OutputPathTextBox.Text.Trim(),
                IntervalText = IntervalTextBox.Text.Trim(),
                WatchEnabled = WatchCheckBox.IsChecked == true,
            });
        }
        catch
        {
            // Ignore persistence failures to avoid blocking the main workflow.
        }
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        PersistSettings();
    }

    private void SetStatus(string headline, string detail, bool isError = false, bool isWarning = false)
    {
        StatusHeadlineTextBlock.Text = headline;
        StatusDetailTextBlock.Text = detail;
        StatusHeadlineTextBlock.Foreground = isError
            ? (System.Windows.Media.Brush)FindResource("DangerBrush")
            : isWarning
                ? (System.Windows.Media.Brush)FindResource("WarningBrush")
                : (System.Windows.Media.Brush)FindResource("AccentBrush");
    }

    private static string ToLocalText(DateTime utc)
    {
        return utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    }
}

public sealed class SnapshotTreeItem
{
    public required string Title { get; init; }

    public ObservableCollection<string> DetailLines { get; } = [];

    public ObservableCollection<SnapshotTreeItem> Children { get; } = [];
}

public sealed class DiffListItem
{
    public required string ChangeLabel { get; init; }

    public required string ItemType { get; init; }

    public required string Path { get; init; }

    public required string Name { get; init; }

    public required string OldValue { get; init; }

    public required string NewValue { get; init; }
}
