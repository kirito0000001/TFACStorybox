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

    /// <summary>
    /// 复制完了一个「正确名称」。参数就是复制走的那串字。
    ///
    /// 弹提示这件事归窗口（它有浮层），面板只负责说"我复制了什么"。
    /// 零境那边是复制完立刻弹一句「已复制正确名称」，这里保持一样 ——
    /// 不弹的话用户不知道到底复制上没有（点了整张卡，界面上没有任何变化）。
    /// </summary>
    internal event EventHandler<string>? CorrectNameCopied;

    // 下面两个方法名必须和 XAML 里的 Click= / Tapped= 完全一致 ——
    // 生成器是按名字找的，名字不对就是 CS1061。
    // （2026-10-12 实际踩过一次：这里叫 RunCheckButton_Click，
    //   而 XAML 里写的是 CheckUnrealSyncButton_Click。）

    private void CheckUnrealSyncButton_Click(object sender, RoutedEventArgs e) =>
        CheckRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// 点**整张卡** = 把这一项的**正确名称**复制走（照零境的做法：Border 上挂 Tapped）。
    ///
    /// ⚠️ 复制的是 <see cref="UnrealSyncCheckRow.CorrectName"/> —— **叶子资产名**
    /// （`Misaka_AnimBP`），不是整条路径。用户拿它是去 Unreal 里**新建**资产，
    /// 新建对话框里填的就是这个名字；粘一整条路径进去是建不出来的。
    /// （第一版我复制的是路径，那个功能等于白做。）
    /// </summary>
    private void CheckItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: UnrealSyncCheckRow row } ||
            string.IsNullOrWhiteSpace(row.CorrectName))
        {
            return;
        }

        var package = new DataPackage();
        package.SetText(row.CorrectName);
        Clipboard.SetContent(package);

        // 复制完说一声，让窗口弹个提示 —— 不然点了整张卡界面上什么变化都没有。
        CorrectNameCopied?.Invoke(this, row.CorrectName);
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
