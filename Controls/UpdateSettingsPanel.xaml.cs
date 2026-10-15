using Microsoft.UI.Xaml.Controls;
using GalExcleTools.ViewModels;

namespace GalExcleTools.Controls;

/// <summary>热更新面板（设计 §11.2）。它只负责显示与绑定，流程在 <c>UpdateViewModel</c> 里。</summary>
public sealed partial class UpdateSettingsPanel : UserControl
{
    public UpdateSettingsPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 通道选择交回设置层：ComboBox 只负责显示，真正的值写进 <c>SettingsViewModel</c>，
    /// 于是「更新通道」也和其他设置一样会落盘、也就能撤回。
    /// </summary>
    private void UpdateChannelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox combo || combo.SelectedIndex < 0)
        {
            return;
        }

        if (DataContext is ApplicationViewModel application)
        {
            application.Settings.UpdateChannel = combo.SelectedIndex == 1
                ? UpdateChannel.Beta
                : UpdateChannel.Stable;
        }
    }
}
