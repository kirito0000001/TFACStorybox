using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GalExcleTools.Controls;

/// <summary>
/// 同步台的「合规检测」层（三层里的第二层）。只负责显示与绑定：
/// 检测结果在 <c>UnrealSyncViewModel</c>，检测本身在
/// <c>Services/UnrealSync/UnrealSyncPreflightCheck</c>。
///
/// 「开始检测」不能在这里做：它要读界面上的引擎/工程路径、要落状态、要写日志 ——
/// 那些都是窗口的事。所以发一个事件，由宿主接过去。
/// （参考项目里同类面板走的是 <c>IVoiceMaterialManagerHost</c> 那种宿主接口；
/// 这里只有一个动作，用事件更直白。）
/// </summary>
public sealed partial class UnrealSyncCheckPanel : UserControl
{
    public UnrealSyncCheckPanel()
    {
        InitializeComponent();
    }

    /// <summary>点「开始检测」。没订阅就什么也不发生。</summary>
    public event EventHandler? CheckRequested;

    private void RunCheckButton_Click(object sender, RoutedEventArgs e) =>
        CheckRequested?.Invoke(this, EventArgs.Empty);
}
