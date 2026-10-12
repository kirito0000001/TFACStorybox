using Microsoft.UI.Xaml.Controls;

namespace GalExcleTools.Controls;

/// <summary>
/// 同步台的「同步规则」说明面板。纯静态文本：没有绑定、没有事件、没有命令。
/// 抽出来是为了让 MainWindow.xaml 不再背着这两百多行说明文字。
/// </summary>
public sealed partial class UnrealSyncRulesPanel : UserControl
{
    public UnrealSyncRulesPanel()
    {
        InitializeComponent();
    }
}
