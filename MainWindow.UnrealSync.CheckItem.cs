using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using GalExcleTools.Services;
using GalExcleTools.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace GalExcleTools
{
    /// <summary>「同步前检测」里点某一项之后的那个弹窗。</summary>
    public sealed partial class MainWindow
    {
        /// <summary>
        /// 点检测表里的一项：说清"它应该建在哪"，再给两个动作 —— 建文件夹 / 复制名称。
        ///
        /// 【为什么不是直接复制】
        /// 零境那边点一下就是复制名字，因为它的检查项名字是唯一的、照着建就行。
        /// 而这张表里最容易搞错的是**在哪一层**：对象路径 <c>/Game/A/B/C.C</c> 里
        /// 包名和资产名是重复的，连成一串看起来就像"B 文件夹里装着 C"。
        ///
        /// 2026-10-12 实测：检测要的是 <c>ExcelTexts/ExsIndexMap.uasset</c>，
        /// 人建成了 <c>ExcelTexts/ExIndexMap/ExsIndexMap.uasset</c>（多了一层文件夹），
        /// 然后对着"资产不存在"发愣 —— 光看那行路径根本看不出来。
        /// 所以弹窗把"文件夹"和"名字"**分两行**写出来，并把当前文件夹在不在说清楚。
        /// </summary>
        private async Task ShowUnrealSyncCheckItemDialogAsync(UnrealSyncCheckRow row)
        {
            var item = row.Item;
            var folderOnDisk = ResolveCheckItemFolderOnDisk(item);
            var hasFolderOnDisk = !string.IsNullOrWhiteSpace(folderOnDisk);
            var folderExists = hasFolderOnDisk && Directory.Exists(folderOnDisk);

            var message = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(item.FolderPath))
            {
                message.AppendLine($"建在（Unreal）：{item.FolderPath}");
            }

            message.AppendLine($"名称：{item.CorrectName}");
            if (!string.IsNullOrWhiteSpace(item.ExpectedType))
            {
                message.AppendLine($"类型：{item.ExpectedType}");
            }

            if (hasFolderOnDisk)
            {
                message.AppendLine();
                message.AppendLine($"对应的磁盘文件夹：{folderOnDisk}");
                message.AppendLine(folderExists ? "（这个文件夹已经存在）" : "（这个文件夹还没有）");
            }

            message.AppendLine();
            message.AppendLine($"当前状态：{item.StatusText}");
            message.AppendLine(item.DetailText);

            if (item.Kind == UnrealSyncCheckKind.Asset && folderExists)
            {
                // 最容易卡住的就是这一步：文件夹在，但"在 Unreal 里怎么建"说不清。
                message.AppendLine();
                message.AppendLine(
                    $"文件夹已经有了 —— 到 Unreal 内容浏览器里打开 {item.FolderPath}，"
                    + $"在**这一层**新建名为 {item.CorrectName} 的资产，别再往下建文件夹。");
            }

            // 文件夹缺了，主按钮就是"建文件夹"（那是它此刻唯一能帮上忙的事）；
            // 文件夹在，主按钮就是"复制名称" —— 这时候还给"创建文件夹"是个空动作，
            // 点了没反应会让人以为坏了。
            var primaryCreatesFolder = hasFolderOnDisk && !folderExists;
            var result = await _dialogService.ShowAsync(new DialogRequest(
                item.DisplayName,
                message.ToString(),
                primaryCreatesFolder ? "创建文件夹" : "复制名称",
                "关闭",
                primaryCreatesFolder ? "复制名称" : "创建文件夹"));

            if (result is not (DialogResultKind.Primary or DialogResultKind.Secondary))
            {
                return;
            }

            var wantsCreate = result == DialogResultKind.Primary ? primaryCreatesFolder : !primaryCreatesFolder;
            if (wantsCreate)
            {
                CreateUnrealSyncCheckFolder(folderOnDisk);
            }
            else
            {
                CopyUnrealSyncCorrectName(item.CorrectName);
            }
        }

        /// <summary>
        /// 这一项对应的**磁盘**文件夹；算不出来就返回空串（那就不给"创建文件夹"这个选项）。
        ///
        /// 目录类：它的 ExpectedPath 本身就是磁盘路径。
        /// 资产类：ExpectedPath 是 <c>/Game</c> 对象路径，要按落点策略换算成
        /// <c>.uasset</c> 磁盘路径再取父目录 —— 这一步算出来的就是"该建在哪个文件夹"。
        /// 文件类（引擎、.uproject）：不给创建选项 —— 给引擎"建文件夹"是没意义的事。
        /// </summary>
        private string ResolveCheckItemFolderOnDisk(UnrealSyncCheckItem item)
        {
            if (string.IsNullOrWhiteSpace(item.ExpectedPath) || item.Kind == UnrealSyncCheckKind.File)
            {
                return string.Empty;
            }

            if (item.Kind == UnrealSyncCheckKind.Directory)
            {
                return item.ExpectedPath;
            }

            var projectPath = SyncPanel.ProjectPath.Trim();
            if (string.IsNullOrWhiteSpace(projectPath))
            {
                return string.Empty;
            }

            try
            {
                var contentFolder = UnrealSyncPathPolicy.ResolveContentFolderPath(projectPath);
                var diskPath = item.ExpectedPath.StartsWith(
                    UnrealSyncPathPolicy.GameRoot + "/",
                    StringComparison.Ordinal)
                    ? UnrealSyncPreflightCheck.ResolveAssetDiskPath(contentFolder, item.ExpectedPath)
                    : item.ExpectedPath;
                return Path.GetDirectoryName(diskPath) ?? string.Empty;
            }
            catch (Exception exception)
            {
                AppendLog(LogKind.Info, $"算不出这一项对应的文件夹：{exception.Message}");
                return string.Empty;
            }
        }

        private void CreateUnrealSyncCheckFolder(string folderOnDisk)
        {
            if (string.IsNullOrWhiteSpace(folderOnDisk))
            {
                return;
            }

            try
            {
                // 只建到这一层，不递归造一整条链 —— 造出来的空目录在 Unreal 里也不显示，
                // 反而会让人以为"目录已经好了"。缺上层的话下次检测会继续报，那是对的。
                if (Directory.Exists(folderOnDisk))
                {
                    ShowFloatingTip(InfoBarSeverity.Informational, "文件夹已经存在", folderOnDisk);
                    return;
                }

                Directory.CreateDirectory(folderOnDisk);
                AppendLog(LogKind.User, $"已创建文件夹：{folderOnDisk}");
                ShowFloatingTip(InfoBarSeverity.Success, "文件夹已创建", folderOnDisk);
                // 建完立刻重检一次，让它当场变绿 —— 不然用户不知道这一步到底生效没有。
                RefreshUnrealSyncStatus();
            }
            catch (Exception exception)
            {
                AppendLog(LogKind.Error, $"创建文件夹失败：{folderOnDisk}", exception);
                ShowFloatingTip(InfoBarSeverity.Error, "创建文件夹失败", exception.Message, exception);
            }
        }

        private void CopyUnrealSyncCorrectName(string correctName)
        {
            if (string.IsNullOrWhiteSpace(correctName))
            {
                return;
            }

            var package = new DataPackage();
            package.SetText(correctName);
            Clipboard.SetContent(package);
            // 复制完说一声 —— 不说的话点了之后界面上什么变化都没有
            // （零境那边也是复制完立刻弹一句「已复制正确名称」）。
            ShowFloatingTip(InfoBarSeverity.Success, "已复制正确名称", correctName);
        }
    }
}
