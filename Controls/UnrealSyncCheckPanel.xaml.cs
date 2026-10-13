using System;
using GalExcleTools.Services;
using GalExcleTools.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;

namespace GalExcleTools.Controls;

/// <summary>
/// 同步台的「合规检测」层（三层里的第二层）。只负责显示与绑定：
/// 检测结果在 <c>UnrealSyncViewModel</c>，检测本身在
/// <c>Services/UnrealSync/UnrealSyncPreflightCheck</c>。
///
/// 两个动作都做不了，得交回宿主：
/// - 「开始检测」要读界面上的引擎 / 工程路径、要落状态、要写日志；
/// - 「更换」要弹文件选择器（需要窗口句柄）。
/// 所以各发一个事件，由窗口接过去。
/// </summary>
public sealed partial class UnrealSyncCheckPanel : UserControl
{
    public UnrealSyncCheckPanel()
    {
        InitializeComponent();
    }

    /// <summary>点「开始检测」。没订阅就什么也不发生。</summary>
    public event EventHandler? CheckRequested;

    /// <summary>
    /// 点了某一项的「更换」；参数是那一项要换什么（引擎 / 工程）。
    ///
    /// 这个事件是 <c>internal</c> 而不是 public：参数类型
    /// <see cref="UnrealSyncCheckReplaceTarget"/> 本身是 internal，
    /// public 事件用它就是 CS7025（可访问性不一致）。
    /// 订阅方（UnrealSyncPanel 与窗口）都在同一个程序集里，internal 够用。
    /// </summary>
    internal event EventHandler<UnrealSyncCheckReplaceTarget>? ReplaceRequested;

    // 下面两个方法名必须和 XAML 里的 Click= / Tapped= 完全一致 ——
    // 生成器是按名字找的，名字不对就是 CS1061。
    // （2026-10-12 实际踩过一次：这里叫 RunCheckButton_Click，
    //   而 XAML 里写的是 CheckUnrealSyncButton_Click。）

    private void CheckUnrealSyncButton_Click(object sender, RoutedEventArgs e) =>
        CheckRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// 点**整张卡** = 把这一项的路径复制走（照零境的做法：Border 上挂 Tapped）。
    ///
    /// 检测报"这个目录 / 资产不存在"时，用户得拿着这个名字去 Unreal 里建 ——
    /// 手抄一遍长路径既慢又容易错，所以整张卡都是复制热区。
    /// </summary>
    private void CheckItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: UnrealSyncCheckRow row } ||
            string.IsNullOrWhiteSpace(row.Path))
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(row.Path);
        Clipboard.SetContent(package);
    }

    /// <summary>
    /// 点某一项的「更换」。
    /// 引擎和工程填错了不是"去 Unreal 里建一个"能解决的，得重新选文件 ——
    /// 那要弹文件选择器，面板自己做不到，交给宿主。
    /// </summary>
    private void ReplaceCheckItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: UnrealSyncCheckRow row } &&
            row.ReplaceTarget != UnrealSyncCheckReplaceTarget.None)
        {
            ReplaceRequested?.Invoke(this, row.ReplaceTarget);
        }
    }
}
