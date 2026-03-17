using Microsoft.Win32;
using RegistryToJson.Core;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace RegistryToJson.Gui;

public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly RegistrySnapshotService _snapshotService = new();
    private readonly RegistryWatchService _watchService;
    private readonly DispatcherTimer _watchTimer;
    private bool _suppressSettingsPersistence = false;
    private bool _suppressSelectionHandling;
    private bool _suppressFieldEvents;
    private WatchConfigurationState? _selectedConfiguration;

    public ObservableCollection<WatchConfigurationState> Configurations { get; } = [];

    public ObservableCollection<SnapshotTreeItem> RootItems { get; } = [];

    public WatchConfigurationState? SelectedConfiguration
    {
        get => _selectedConfiguration;
        set
        {
            if (ReferenceEquals(_selectedConfiguration, value))
            {
                return;
            }

            _selectedConfiguration = value;
            OnPropertyChanged();
        }
    }

    public MainWindow()
    {
        _watchService = new RegistryWatchService(_snapshotService, new RegistryDiffService());
        _watchTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1),
        };
        _watchTimer.Tick += WatchTimer_Tick;

        InitializeComponent();
        DataContext = this;

        RestorePersistedSettings();
        EnsureSelection();
        RenderSelectedConfiguration();
        UpdateGlobalSummary();
        _watchTimer.Start();

        Closing += MainWindow_Closing;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void AddConfiguration_Click(object sender, RoutedEventArgs e)
    {
        var configuration = new WatchConfigurationState(WatchConfigurationSettings.CreateDefault(Configurations.Count + 1));
        Configurations.Add(configuration);
        SelectConfiguration(configuration);
        PersistSettings();
    }

    private void DuplicateConfiguration_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedConfiguration is null)
        {
            SetStatus("No Selection", "请先在左侧选择一个监控配置。", isError: true);
            return;
        }

        var duplicate = new WatchConfigurationState(new WatchConfigurationSettings
        {
            Name = BuildDuplicateName(SelectedConfiguration.Name),
            RegistryPath = SelectedConfiguration.RegistryPath,
            OutputPath = SelectedConfiguration.OutputPath,
            IntervalText = SelectedConfiguration.IntervalText,
            WatchEnabled = SelectedConfiguration.WatchEnabled,
        });

        Configurations.Add(duplicate);
        SelectConfiguration(duplicate);
        SetStatus("Configuration Duplicated", $"已复制监控配置: {SelectedConfiguration.Name}");
        PersistSettings();
    }

    private void DeleteConfiguration_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedConfiguration is null)
        {
            SetStatus("No Selection", "请先在左侧选择一个监控配置。", isError: true);
            return;
        }

        if (Configurations.Count == 1)
        {
            SetStatus("Delete Blocked", "至少需要保留一个监控配置。", isError: true);
            return;
        }

        var currentIndex = Configurations.IndexOf(SelectedConfiguration);
        var configurationName = SelectedConfiguration.Name;
        var nextIndex = Math.Clamp(currentIndex - 1, 0, Configurations.Count - 2);

        Configurations.Remove(SelectedConfiguration);
        SelectConfiguration(Configurations[nextIndex]);
        SetStatus("Configuration Removed", $"已删除监控配置: {configurationName}");
        PersistSettings();
    }

    private void ConfigurationsListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_suppressSelectionHandling)
        {
            return;
        }

        SelectedConfiguration = ConfigurationsListBox.SelectedItem as WatchConfigurationState;
        RenderSelectedConfiguration();
        PersistSettings();
    }

    private void ConfigurationNameTextBox_OnChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_suppressFieldEvents || SelectedConfiguration is null)
        {
            return;
        }

        SelectedConfiguration.Name = string.IsNullOrWhiteSpace(ConfigurationNameTextBox.Text)
            ? $"监控项 {Configurations.IndexOf(SelectedConfiguration) + 1}"
            : ConfigurationNameTextBox.Text.Trim();
        ConfigurationsListBox.Items.Refresh();
        PersistSettings();
    }

    private async void RefreshSnapshot_Click(object sender, RoutedEventArgs e)
    {
        await RefreshConfigurationAsync(SelectedConfiguration, updateStatusOnNoBaseline: true, userInitiated: true);
    }

    private async void ExportJson_Click(object sender, RoutedEventArgs e)
    {
        var configuration = SelectedConfiguration;
        if (configuration is null)
        {
            SetStatus("No Selection", "请先选择一个监控配置。", isError: true);
            return;
        }

        var registryPath = configuration.RegistryPath.Trim();
        var outputPath = configuration.OutputPath.Trim();
        if (string.IsNullOrWhiteSpace(registryPath) || string.IsNullOrWhiteSpace(outputPath))
        {
            SetStatus("Missing Input", "请同时提供注册表路径和输出文件。", isError: true);
            return;
        }

        try
        {
            await Task.Run(() => _snapshotService.Export(new ExportRequest { RegistryPath = registryPath, OutputFilePath = outputPath }));
            configuration.StatusLine = "Exported";
            configuration.StatusDetail = $"已写出 JSON: {outputPath}";
            SetStatus("Exported", $"{configuration.Name} 已写出 JSON: {outputPath}");
            UpdateSelectedConfigurationVisuals();
            PersistSettings();
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
        }
    }

    private void PinBaseline_Click(object sender, RoutedEventArgs e)
    {
        var configuration = SelectedConfiguration;
        if (configuration?.CurrentSnapshot is null)
        {
            SetStatus("No Snapshot", "请先读取当前快照，再保留基线。", isError: true);
            return;
        }

        configuration.BaselineSnapshot = configuration.CurrentSnapshot;
        configuration.StatusLine = "Baseline Pinned";
        configuration.StatusDetail = "已保留当前快照为比较基线。";
        configuration.BaselineSummary = $"基线已保留: {configuration.BaselineSnapshot.SourcePath} @ {ToLocalText(configuration.BaselineSnapshot.CapturedAtUtc)}";
        configuration.DiffMeta = "后续刷新或 watch 将与当前基线比较";
        ApplyDiffResult(configuration, new RegistryDiffResult { ComparedAtUtc = DateTime.UtcNow });
        SetStatus("Baseline Pinned", $"已为 {configuration.Name} 保留当前快照。");
        UpdateSelectedConfigurationVisuals();
    }

    private void ReplaceBaseline_Click(object sender, RoutedEventArgs e)
    {
        var configuration = SelectedConfiguration;
        if (configuration?.CurrentSnapshot is null)
        {
            SetStatus("No Snapshot", "当前没有可替换的快照。", isError: true);
            return;
        }

        configuration.BaselineSnapshot = configuration.CurrentSnapshot;
        configuration.BaselineSummary = $"基线已更新: {configuration.BaselineSnapshot.SourcePath} @ {ToLocalText(configuration.BaselineSnapshot.CapturedAtUtc)}";
        configuration.StatusLine = "Baseline Replaced";
        configuration.StatusDetail = "已用当前快照替换基线。";
        ApplyDiffResult(configuration, new RegistryDiffResult { ComparedAtUtc = DateTime.UtcNow });
        SetStatus("Baseline Replaced", $"已用 {configuration.Name} 的当前快照替换基线。");
        UpdateSelectedConfigurationVisuals();
    }

    private void WatchCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        if (_suppressFieldEvents || SelectedConfiguration is null)
        {
            return;
        }

        SelectedConfiguration.WatchEnabled = WatchCheckBox.IsChecked == true;
        SelectedConfiguration.NextRefreshAtUtc = SelectedConfiguration.WatchEnabled ? DateTime.UtcNow : null;
        SelectedConfiguration.StatusLine = SelectedConfiguration.WatchEnabled ? "Watch Enabled" : "Watch Disabled";
        SelectedConfiguration.StatusDetail = SelectedConfiguration.WatchEnabled
            ? "watch 已启动，等待下一次调度。"
            : "watch 已停止。";

        SetStatus(SelectedConfiguration.StatusLine, $"{SelectedConfiguration.Name}: {SelectedConfiguration.StatusDetail}", isWarning: SelectedConfiguration.WatchEnabled);
        ConfigurationsListBox.Items.Refresh();
        UpdateGlobalSummary();
        PersistSettings();
    }

    private async void WatchTimer_Tick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var dueConfigurations = Configurations
            .Where(config => config.WatchEnabled && !config.IsRefreshing && config.NextRefreshAtUtc <= now)
            .ToList();

        foreach (var configuration in dueConfigurations)
        {
            await RefreshConfigurationAsync(configuration, updateStatusOnNoBaseline: false, userInitiated: false);
        }

        UpdateGlobalSummary();
    }

    private async Task RefreshConfigurationAsync(WatchConfigurationState? configuration, bool updateStatusOnNoBaseline, bool userInitiated)
    {
        if (configuration is null || configuration.IsRefreshing)
        {
            return;
        }

        var registryPath = configuration.RegistryPath.Trim();
        if (string.IsNullOrWhiteSpace(registryPath))
        {
            if (ReferenceEquals(configuration, SelectedConfiguration))
            {
                SetStatus("Missing Path", "请填写注册表路径。", isError: true);
            }

            return;
        }

        if (!TryGetIntervalSeconds(configuration, out var intervalSeconds, out var intervalError))
        {
            configuration.StatusLine = "Invalid Interval";
            configuration.StatusDetail = intervalError;
            configuration.NextRefreshAtUtc = DateTime.UtcNow.AddSeconds(1);
            if (ReferenceEquals(configuration, SelectedConfiguration))
            {
                SetStatus("Invalid Interval", intervalError, isError: true);
                UpdateSelectedConfigurationVisuals();
            }

            return;
        }

        configuration.IsRefreshing = true;
        configuration.StatusLine = "Refreshing";
        configuration.StatusDetail = $"正在读取 {registryPath}";
        if (ReferenceEquals(configuration, SelectedConfiguration))
        {
            SetStatus("Refreshing", $"{configuration.Name}: 正在读取 {registryPath}", isWarning: true);
            UpdateSelectedConfigurationVisuals();
        }

        try
        {
            var result = await Task.Run(() => _watchService.Refresh(registryPath, configuration.BaselineSnapshot));
            configuration.CurrentSnapshot = result.CurrentSnapshot;
            configuration.LastRefreshAtUtc = result.CurrentSnapshot.CapturedAtUtc;
            ApplyDiffResult(configuration, result.Diff);

            if (configuration.BaselineSnapshot is null)
            {
                if (updateStatusOnNoBaseline)
                {
                    configuration.StatusLine = "Snapshot Loaded";
                    configuration.StatusDetail = "已读取当前快照。可点击“保留为基线”开始比较。";
                }
            }
            else
            {
                var diffCount = result.Diff.Entries.Count;
                configuration.StatusLine = diffCount == 0 ? "No Changes" : "Changes Detected";
                configuration.StatusDetail = diffCount == 0 ? "当前快照与基线一致。" : $"检测到 {diffCount} 条变化。";
            }

            configuration.NextRefreshAtUtc = configuration.WatchEnabled
                ? DateTime.UtcNow.AddSeconds(intervalSeconds)
                : null;

            if (ReferenceEquals(configuration, SelectedConfiguration))
            {
                RenderSelectedConfiguration();
                if (userInitiated || configuration.BaselineSnapshot is not null || updateStatusOnNoBaseline)
                {
                    SetStatus(configuration.StatusLine, $"{configuration.Name}: {configuration.StatusDetail}", isWarning: configuration.DiffEntries.Count > 0 && configuration.BaselineSnapshot is not null);
                }
            }
        }
        catch (Exception ex)
        {
            configuration.StatusLine = "Refresh Failed";
            configuration.StatusDetail = ex.Message;
            configuration.NextRefreshAtUtc = configuration.WatchEnabled
                ? DateTime.UtcNow.AddSeconds(intervalSeconds)
                : null;
            if (ReferenceEquals(configuration, SelectedConfiguration))
            {
                SetStatus("Refresh Failed", ex.Message, isError: true);
                UpdateSelectedConfigurationVisuals();
            }
        }
        finally
        {
            configuration.IsRefreshing = false;
            ConfigurationsListBox.Items.Refresh();
            UpdateGlobalSummary();
        }
    }

    private void PersistedInput_OnChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_suppressFieldEvents || SelectedConfiguration is null)
        {
            return;
        }

        SelectedConfiguration.RegistryPath = RegistryPathTextBox.Text.Trim();
        SelectedConfiguration.OutputPath = OutputPathTextBox.Text.Trim();
        SelectedConfiguration.IntervalText = IntervalTextBox.Text.Trim();
        ConfigurationsListBox.Items.Refresh();
        UpdateSelectedConfigurationVisuals();
        PersistSettings();
    }

    private void RestorePersistedSettings()
    {
        var settings = GuiSettingsStore.Load();
        Configurations.Clear();
        foreach (var config in settings.Configurations)
        {
            Configurations.Add(new WatchConfigurationState(config));
        }

        var selected = Configurations.FirstOrDefault(config => config.Id == settings.SelectedConfigurationId)
            ?? Configurations.FirstOrDefault();

        if (selected is null)
        {
            selected = new WatchConfigurationState(WatchConfigurationSettings.CreateDefault());
            Configurations.Add(selected);
        }

        SelectConfiguration(selected, persist: false);
    }

    private void EnsureSelection()
    {
        if (SelectedConfiguration is not null || Configurations.Count == 0)
        {
            return;
        }

        SelectConfiguration(Configurations[0], persist: false);
    }

    private void SelectConfiguration(WatchConfigurationState configuration, bool persist = true)
    {
        _suppressSelectionHandling = true;
        ConfigurationsListBox.SelectedItem = configuration;
        _suppressSelectionHandling = false;

        SelectedConfiguration = configuration;
        RenderSelectedConfiguration();
        if (persist)
        {
            PersistSettings();
        }
    }

    private void RenderSelectedConfiguration()
    {
        _suppressFieldEvents = true;

        if (SelectedConfiguration is null)
        {
            ConfigurationNameTextBox.Text = string.Empty;
            RegistryPathTextBox.Text = string.Empty;
            OutputPathTextBox.Text = string.Empty;
            IntervalTextBox.Text = "2";
            WatchCheckBox.IsChecked = false;
            RootItems.Clear();
            DiffListView.ItemsSource = null;
            SnapshotMetaTextBlock.Text = "尚未读取快照";
            DiffMetaTextBlock.Text = "保留基线后可查看与当前快照的差异";
            BaselineSummaryTextBlock.Text = "尚未保留基线";
            ConfigurationStatusTextBlock.Text = "未选择配置";
            AddedCountTextBlock.Text = "0";
            ModifiedCountTextBlock.Text = "0";
            RemovedCountTextBlock.Text = "0";
            SelectedConfigurationMetaTextBlock.Text = "未选择配置";
            _suppressFieldEvents = false;
            return;
        }

        ConfigurationNameTextBox.Text = SelectedConfiguration.Name;
        RegistryPathTextBox.Text = SelectedConfiguration.RegistryPath;
        OutputPathTextBox.Text = SelectedConfiguration.OutputPath;
        IntervalTextBox.Text = SelectedConfiguration.IntervalText;
        WatchCheckBox.IsChecked = SelectedConfiguration.WatchEnabled;

        RenderSnapshot(SelectedConfiguration);
        ApplyDiffResult(SelectedConfiguration, SelectedConfiguration.LastDiff ?? new RegistryDiffResult { ComparedAtUtc = DateTime.UtcNow });
        BaselineSummaryTextBlock.Text = SelectedConfiguration.BaselineSummary;
        ConfigurationStatusTextBlock.Text = SelectedConfiguration.StatusDetail;
        SelectedConfigurationMetaTextBlock.Text = $"{SelectedConfiguration.Name} | {SelectedConfiguration.WatchStatusLabel}";
        _suppressFieldEvents = false;
    }

    private void RenderSnapshot(WatchConfigurationState configuration)
    {
        RootItems.Clear();
        if (configuration.CurrentSnapshot is null)
        {
            SnapshotMetaTextBlock.Text = "尚未读取快照";
            return;
        }

        RootItems.Add(BuildTreeItem(configuration.CurrentSnapshot.Root));
        SnapshotMetaTextBlock.Text = $"{configuration.CurrentSnapshot.SourcePath} | {ToLocalText(configuration.CurrentSnapshot.CapturedAtUtc)}";
    }

    private static SnapshotTreeItem BuildTreeItem(RegistryNodeSnapshot node)
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

    private void ApplyDiffResult(WatchConfigurationState configuration, RegistryDiffResult diff)
    {
        configuration.LastDiff = diff;
        configuration.DiffEntries = diff.Entries.Select(entry => new DiffListItem
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

        configuration.AddedCount = configuration.DiffEntries.Count(static item => item.ChangeLabel == "新增");
        configuration.ModifiedCount = configuration.DiffEntries.Count(static item => item.ChangeLabel == "修改");
        configuration.RemovedCount = configuration.DiffEntries.Count(static item => item.ChangeLabel == "删除");
        configuration.DiffMeta = configuration.BaselineSnapshot is null
            ? "保留基线后可查看与当前快照的差异"
            : $"最近比较时间: {ToLocalText(diff.ComparedAtUtc)}";

        if (!ReferenceEquals(configuration, SelectedConfiguration))
        {
            return;
        }

        DiffListView.ItemsSource = configuration.DiffEntries;
        AddedCountTextBlock.Text = configuration.AddedCount.ToString();
        ModifiedCountTextBlock.Text = configuration.ModifiedCount.ToString();
        RemovedCountTextBlock.Text = configuration.RemovedCount.ToString();
        DiffMetaTextBlock.Text = configuration.DiffMeta;
    }

    private void UpdateSelectedConfigurationVisuals()
    {
        if (SelectedConfiguration is null)
        {
            return;
        }

        SelectedConfigurationMetaTextBlock.Text = $"{SelectedConfiguration.Name} | {SelectedConfiguration.WatchStatusLabel}";
        BaselineSummaryTextBlock.Text = SelectedConfiguration.BaselineSummary;
        ConfigurationStatusTextBlock.Text = SelectedConfiguration.StatusDetail;
        ConfigurationsListBox.Items.Refresh();
    }

    private void UpdateGlobalSummary()
    {
        var activeWatchCount = Configurations.Count(config => config.WatchEnabled);
        var changedConfigurations = Configurations.Count(config => config.DiffEntries.Count > 0 && config.BaselineSnapshot is not null);

        MultiWatchSummaryTextBlock.Text = $"共 {Configurations.Count} 个配置，其中 {activeWatchCount} 个正在 watch，{changedConfigurations} 个存在变化。";
    }

    private string BuildDuplicateName(string sourceName)
    {
        var baseName = string.IsNullOrWhiteSpace(sourceName) ? "监控项" : sourceName.Trim();
        var duplicateName = $"{baseName} - 副本";
        if (Configurations.All(config => !string.Equals(config.Name, duplicateName, StringComparison.OrdinalIgnoreCase)))
        {
            return duplicateName;
        }

        var index = 2;
        while (true)
        {
            var candidate = $"{baseName} - 副本 {index}";
            if (Configurations.All(config => !string.Equals(config.Name, candidate, StringComparison.OrdinalIgnoreCase)))
            {
                return candidate;
            }

            index++;
        }
    }

    private bool TryGetIntervalSeconds(WatchConfigurationState configuration, out int seconds, out string error)
    {
        if (!int.TryParse(configuration.IntervalText.Trim(), out seconds) || seconds <= 0)
        {
            error = "watch 间隔必须是正整数秒。";
            return false;
        }

        error = string.Empty;
        return true;
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
                Configurations = Configurations.Select(config => new WatchConfigurationSettings
                {
                    Id = config.Id,
                    Name = config.Name,
                    RegistryPath = config.RegistryPath,
                    OutputPath = config.OutputPath,
                    IntervalText = config.IntervalText,
                    WatchEnabled = config.WatchEnabled,
                }).ToList(),
                SelectedConfigurationId = SelectedConfiguration?.Id,
            });
        }
        catch
        {
            // Ignore persistence failures to avoid blocking the main workflow.
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
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

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class WatchConfigurationState : INotifyPropertyChanged
{
    private string _name;
    private string _registryPath;
    private string _outputPath;
    private string _intervalText;
    private bool _watchEnabled;
    private string _statusLine;
    private string _statusDetail;
    private string _baselineSummary;
    private string _diffMeta;

    public WatchConfigurationState(WatchConfigurationSettings settings)
    {
        Id = settings.Id;
        _name = settings.Name;
        _registryPath = settings.RegistryPath;
        _outputPath = settings.OutputPath;
        _intervalText = settings.IntervalText;
        _watchEnabled = settings.WatchEnabled;
        _statusLine = settings.WatchEnabled ? "Watch Enabled" : "Idle";
        _statusDetail = settings.WatchEnabled ? "watch 已恢复，等待下一次调度。" : "尚未读取快照";
        _baselineSummary = "尚未保留基线";
        _diffMeta = "保留基线后可查看与当前快照的差异";
        NextRefreshAtUtc = settings.WatchEnabled ? DateTime.UtcNow : null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public Guid Id { get; }

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }

    public string RegistryPath
    {
        get => _registryPath;
        set => SetProperty(ref _registryPath, value);
    }

    public string OutputPath
    {
        get => _outputPath;
        set => SetProperty(ref _outputPath, value);
    }

    public string IntervalText
    {
        get => _intervalText;
        set => SetProperty(ref _intervalText, value);
    }

    public bool WatchEnabled
    {
        get => _watchEnabled;
        set
        {
            if (SetProperty(ref _watchEnabled, value))
            {
                OnPropertyChanged(nameof(WatchStatusLabel));
            }
        }
    }

    public string StatusLine
    {
        get => _statusLine;
        set => SetProperty(ref _statusLine, value);
    }

    public string StatusDetail
    {
        get => _statusDetail;
        set => SetProperty(ref _statusDetail, value);
    }

    public string BaselineSummary
    {
        get => _baselineSummary;
        set => SetProperty(ref _baselineSummary, value);
    }

    public string DiffMeta
    {
        get => _diffMeta;
        set => SetProperty(ref _diffMeta, value);
    }

    public string RegistryPathSummary => string.IsNullOrWhiteSpace(RegistryPath) ? "未设置注册表路径" : RegistryPath;

    public string WatchStatusLabel => WatchEnabled ? "Watching" : "Stopped";

    public bool IsRefreshing { get; set; }

    public DateTime? NextRefreshAtUtc { get; set; }

    public DateTime? LastRefreshAtUtc { get; set; }

    public RegistrySnapshot? BaselineSnapshot { get; set; }

    public RegistrySnapshot? CurrentSnapshot { get; set; }

    public RegistryDiffResult? LastDiff { get; set; }

    public List<DiffListItem> DiffEntries { get; set; } = [];

    public int AddedCount { get; set; }

    public int ModifiedCount { get; set; }

    public int RemovedCount { get; set; }

    private bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        if (propertyName is nameof(RegistryPath))
        {
            OnPropertyChanged(nameof(RegistryPathSummary));
        }

        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
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
