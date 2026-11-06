using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace GalExcleTools.Controls;

/// <summary>
/// 统一路径栏（返回 / 上一层 / 路径 / 打开文件夹）。
///
/// 素材浏览器与角色立绘详情页共用：这两处原来各写一套，按钮尺寸 36 vs 40、
/// 排版也不一样，切页面时看着像两个软件。
/// </summary>
public sealed partial class PathBar : UserControl
{
    public PathBar()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 关掉自带的描边/底色/内边距。
    /// 宿主想把路径栏和别的按钮画进**同一条边框**时用（素材库里 ⚙ 就并在这一行）。
    /// </summary>
    public void SetChromeVisible(bool visible)
    {
        if (Chrome is null)
        {
            return;
        }

        if (visible)
        {
            Chrome.Style = (Style)Application.Current.Resources["PanelBorderStyle"];
            return;
        }

        Chrome.Style = null;
        Chrome.BorderThickness = new Thickness(0);
        Chrome.Padding = new Thickness(0);
        Chrome.Background = null;
    }

    public event EventHandler? BackRequested;

    public event EventHandler? UpRequested;

    public event EventHandler? OpenFolderRequested;

    /// <summary>路径栏上那行字。</summary>
    public void SetPath(string path)
    {
        if (PathText is not null)
        {
            PathText.Text = path ?? string.Empty;
        }
    }

    /// <summary>"上一层"是否可用（根分类时置 false，按钮变灰）。</summary>
    public void SetUpEnabled(bool enabled)
    {
        if (UpButton is not null)
        {
            UpButton.IsEnabled = enabled;
        }
    }

    /// <summary>整块隐藏"上一层"（角色详情页没有层级，用不上）。</summary>
    public void SetUpVisible(bool visible)
    {
        if (UpButton is not null)
        {
            UpButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public void SetBackToolTip(string toolTip)
    {
        if (BackButton is not null)
        {
            ToolTipService.SetToolTip(BackButton, toolTip);
        }
    }

    public void SetOpenFolderToolTip(string toolTip)
    {
        if (OpenFolderButton is not null)
        {
            ToolTipService.SetToolTip(OpenFolderButton, toolTip);
        }
    }

    /// <summary>没有"打开文件夹"的场景（比如还没定位到具体目录）可以藏掉。</summary>
    public void SetOpenFolderVisible(bool visible)
    {
        if (OpenFolderButton is not null)
        {
            OpenFolderButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
        => BackRequested?.Invoke(this, EventArgs.Empty);

    private void UpButton_Click(object sender, RoutedEventArgs e)
        => UpRequested?.Invoke(this, EventArgs.Empty);

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        => OpenFolderRequested?.Invoke(this, EventArgs.Empty);
}
