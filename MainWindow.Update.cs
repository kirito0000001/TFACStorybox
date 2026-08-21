using System;
using System.Threading;
using System.Threading.Tasks;
using GalExcleTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GalExcleTools;

public sealed partial class MainWindow
{
    private void SyncUpdateControls()
    {
        if (UpdateChannelComboBox is not null)
        {
            UpdateChannelComboBox.SelectedIndex = Settings.UpdateChannel == UpdateChannel.Beta ? 1 : 0;
        }
    }

    private void UpdateChannelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UpdateChannelComboBox.SelectedIndex < 0)
        {
            return;
        }

        Settings.UpdateChannel = UpdateChannelComboBox.SelectedIndex == 1
            ? UpdateChannel.Beta
            : UpdateChannel.Stable;
    }

    private async void CheckUpdateButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync(showProgress: true, showNoUpdateTip: true);
    }

    private async void TestUpdateConnectionButton_Click(object sender, RoutedEventArgs e)
    {
        ShowGlobalProgress("测试更新连接", "正在访问 Gitee，失败时自动尝试 GitHub。");
        UpdateGlobalProgress("正在测试更新源...", 10, Settings.UpdateChannelText, true);
        try
        {
            var result = await _updateService.TestConnectionAsync(
                Settings.UpdateChannel,
                Settings.UpdateConnectionTimeoutSecondsValue,
                GetGlobalProgressCancellationToken());
            var message = $"{result.Message} 耗时 {result.Elapsed.TotalSeconds:0.0} 秒。";
            Settings.SetUpdateStatus(message);
            CompleteGlobalProgress(result.IsSuccess ? "更新连接正常" : "更新连接不可用", message);
            ShowFloatingTip(
                result.IsSuccess ? InfoBarSeverity.Success : InfoBarSeverity.Warning,
                result.IsSuccess ? "更新连接正常" : "更新连接不可用",
                message);
        }
        catch (OperationCanceledException)
        {
            CompleteGlobalProgress("连接测试已取消", "没有下载或替换任何文件。");
        }
        catch (Exception ex)
        {
            Settings.SetUpdateStatus($"更新连接失败：{ex.Message}");
            CompleteGlobalProgress("更新连接失败", ex.Message);
            ShowFloatingTip(InfoBarSeverity.Warning, "更新连接失败", ex.Message, ex, $"更新连接测试失败：{ex}");
        }
        finally
        {
            await HideGlobalProgressAfterDelayAsync();
        }
    }

#if DEBUG
    private Task CheckUpdateOnStartupAsync()
    {
        return Task.CompletedTask;
    }
#else
    private async Task CheckUpdateOnStartupAsync()
    {
        if (!Settings.UpdateAutoCheckEnabled)
        {
            return;
        }

        await Task.Delay(TimeSpan.FromSeconds(5));
        await CheckForUpdatesAsync(showProgress: false, showNoUpdateTip: false);
    }
#endif

    private async Task CheckForUpdatesAsync(bool showProgress, bool showNoUpdateTip)
    {
        try
        {
            if (showProgress)
            {
                ShowGlobalProgress("检查应用更新", "优先访问 Gitee，失败时自动尝试 GitHub。");
                UpdateGlobalProgress("正在检查新版本...", 10, Settings.UpdateChannelText, true);
            }

            var result = await _updateService.CheckAsync(
                Settings.UpdateChannel,
                Settings.UpdateConnectionTimeoutSecondsValue,
                showProgress ? GetGlobalProgressCancellationToken() : CancellationToken.None);
            Settings.SetUpdateStatus(result.Message);
            if (showProgress)
            {
                CompleteGlobalProgress("更新检查完成", result.Message);
                await HideGlobalProgressAfterDelayAsync();
            }

            if (!result.HasUpdate || result.Manifest is null || result.Asset is null)
            {
                if (showNoUpdateTip)
                {
                    ShowFloatingTip(InfoBarSeverity.Success, "更新检查完成", result.Message);
                }

                return;
            }

            await PromptAndInstallUpdateAsync(result);
        }
        catch (OperationCanceledException)
        {
            if (showProgress)
            {
                CompleteGlobalProgress("更新检查已取消", "没有下载或替换任何文件。");
                await HideGlobalProgressAfterDelayAsync();
            }
        }
        catch (Exception ex)
        {
            Settings.SetUpdateStatus($"更新检查失败：{ex.Message}");
            if (showProgress)
            {
                CompleteGlobalProgress("更新检查失败", ex.Message);
                await HideGlobalProgressAfterDelayAsync();
            }

            ShowFloatingTip(InfoBarSeverity.Warning, "更新检查失败", ex.Message, ex, $"更新检查失败：{ex}");
        }
    }

    private async Task PromptAndInstallUpdateAsync(UpdateCheckResult result)
    {
#if DEBUG
        ShowFloatingTip(
            InfoBarSeverity.Informational,
            "开发版检测到更新",
            $"发现 {result.LatestVersion}，Debug 开发版不会覆盖自身；请通过 AX 工具箱启动或打包正式版。");
        await Task.CompletedTask;
        return;
#else
        var manifest = result.Manifest!;
        var asset = result.Asset!;
        var content = new StackPanel
        {
            Width = 460,
            Spacing = 8
        };
        content.Children.Add(new TextBlock { Text = $"当前版本：{result.CurrentVersion}" });
        content.Children.Add(new TextBlock { Text = $"新版本：{result.LatestVersion}" });
        content.Children.Add(new TextBlock { Text = $"更新来源：{UpdateService.GetSourceDisplayName(result.Source)}" });
        content.Children.Add(new TextBlock { Text = $"更新包：{asset.FileName}" });
        content.Children.Add(new TextBlock { Text = $"包大小：{UpdateService.FormatBytes(asset.SizeBytes)}" });
        content.Children.Add(new TextBlock
        {
            Margin = new Thickness(0, 6, 0, 0),
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            Text = "更新公告"
        });
        content.Children.Add(new Border
        {
            MaxHeight = 180,
            Padding = new Thickness(12),
            Background = Application.Current.Resources["CardBackgroundFillColorDefaultBrush"] as Brush,
            BorderBrush = Application.Current.Resources["CardStrokeColorDefaultBrush"] as Brush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(result.ReleaseNotes) ? "本次更新没有填写公告。" : result.ReleaseNotes,
                    TextWrapping = TextWrapping.Wrap
                }
            }
        });
        content.Children.Add(new TextBlock
        {
            Text = "更新包会先完成大小、SHA-256 和程序身份校验；校验通过后工具箱退出，由外部更新器覆盖并重新启动。",
            TextWrapping = TextWrapping.Wrap
        });

        var dialogResult = await _dialogService.ShowContentAsync(new ContentDialogRequest(
            "发现新版本",
            content,
            "下载并更新",
            "继续使用当前版本"));
        if (dialogResult != DialogResultKind.Primary)
        {
            return;
        }

        await DownloadAndLaunchUpdaterAsync(manifest, asset);
#endif
    }

    private async Task DownloadAndLaunchUpdaterAsync(UpdateManifest manifest, UpdateAssetManifest asset)
    {
        ShowGlobalProgress("下载应用更新", asset.FileName);
        try
        {
            var progress = new Progress<ProgressUpdate>(update =>
                UpdateGlobalProgress(update.Message, update.Percent, update.Detail, update.IsIndeterminate));
            var download = await _updateService.DownloadAndVerifyAsync(
                manifest,
                asset,
                progress,
                GetGlobalProgressCancellationToken());
            CompleteGlobalProgress("更新包已校验", "正在启动外部更新器。");
            await _updateService.LaunchUpdaterAsync(
                download.PackagePath,
                manifest,
                progress,
                GetGlobalProgressCancellationToken());
            Application.Current.Exit();
        }
        catch (OperationCanceledException)
        {
            CompleteGlobalProgress("更新已取消", "没有替换任何程序文件。");
            ShowFloatingTip(InfoBarSeverity.Warning, "更新已取消", "没有替换任何程序文件。");
            await HideGlobalProgressAfterDelayAsync();
        }
        catch (Exception ex)
        {
            Settings.SetUpdateStatus($"应用更新失败：{ex.Message}");
            CompleteGlobalProgress("应用更新失败", ex.Message);
            ShowFloatingTip(InfoBarSeverity.Error, "应用更新失败", ex.Message, ex, $"应用更新失败：{ex}");
            await HideGlobalProgressAfterDelayAsync();
        }
    }
}
