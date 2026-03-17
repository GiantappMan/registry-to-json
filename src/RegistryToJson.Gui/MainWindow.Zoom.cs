using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RegistryToJson.Core;

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

    private async void DiffListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
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

        zoomWindow.Show();

        if (DiffListView.SelectedItem is DiffListItem selectedItem)
        {
            await zoomWindow.ShowDiffCompareAsync(
                selectedItem,
                new TextCompareService(),
                $"{SelectedConfiguration.Name} | 左右对照",
                "当前变化项已切换为左右双栏 compare 视图，方便像 SVN compare 一样快速定位变化。",
                DiffMetaTextBlock.Text);
        }
        else
        {
            zoomWindow.ShowDiffEntries(
                SelectedConfiguration.DiffEntries,
                $"{SelectedConfiguration.Name} | 变化明细",
                "当前未选中单条变化，已回退为完整变化列表。",
                DiffMetaTextBlock.Text);
        }
    }
}
