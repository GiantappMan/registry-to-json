using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace RegistryToJson.Gui;

public partial class MainWindow
{
    private void SnapshotTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedConfiguration?.CurrentSnapshot is null)
        {
            SetStatus("No Snapshot", "当前没有可放大的快照内容。", isError: true);
            return;
        }

        var zoomWindow = new ZoomWindow
        {
            Owner = this,
        };

        zoomWindow.ShowSnapshotTree(
            RootItems,
            $"{SelectedConfiguration.Name} | 当前快照树",
            "双击主界面的快照树区域后，可在此窗口查看更大范围的树结构。",
            SnapshotMetaTextBlock.Text);
        zoomWindow.Show();
    }

    private void DiffListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedConfiguration is null)
        {
            SetStatus("No Selection", "当前没有可放大的差异内容。", isError: true);
            return;
        }

        var zoomWindow = new ZoomWindow
        {
            Owner = this,
        };

        zoomWindow.ShowDiffEntries(
            SelectedConfiguration.DiffEntries,
            $"{SelectedConfiguration.Name} | 变化明细",
            "双击主界面的变化明细区域后，可在此窗口查看更宽的差异表格。",
            DiffMetaTextBlock.Text);
        zoomWindow.Show();
    }
}
