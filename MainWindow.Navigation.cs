using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using GalExcleTools.Services;
using GalExcleTools.ViewModels;
using GalExcleTools.Views;
using static GalExcleTools.Services.ColorUtility;
using static GalExcleTools.Services.FileSystemUtility;
using static GalExcleTools.Services.TextUtility;
using static GalExcleTools.Services.WorkspacePathUtility;
using Microsoft.UI.Windowing;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace GalExcleTools
{
    public sealed partial class MainWindow
    {
        private async void ShowProjectRootHelpButton_Click(object sender, RoutedEventArgs e)
        {
            await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "整体项目位置说明",
                DialogContentFactory.CreateProjectRootHelpContent(),
                string.Empty,
                "关闭",
                DefaultButton: ContentDialogButton.Close,
                PrimarySound: DialogSoundIntent.None));
        }

        private async void ShowLogHelpButton_Click(object sender, RoutedEventArgs e)
        {
            await _dialogService.ShowContentAsync(new ContentDialogRequest(
                "辅助显示说明",
                DialogContentFactory.CreateLogHelpContent(),
                string.Empty,
                "关闭",
                DefaultButton: ContentDialogButton.Close,
                PrimarySound: DialogSoundIntent.None));
        }

        private void ShowWorkbenchPage()
        {
            _workbenchReturnPage = WorkbenchReturnPage.Root;
            StopStoryEditorAudio();
            ResetCreateProjectForm();
            _currentAssetLibrary = null;
            WorkbenchPage.Visibility = Visibility.Visible;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            ProjectTextToolPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            if (!ReferenceEquals(ShellNavigation.SelectedItem, WorkbenchNavItem))
            {
                SelectShellNavigationItem(WorkbenchNavItem);
            }
            LoadProjects();
            RefreshWorkbenchUnrealSyncTip();
            PlayPageEntrance(WorkbenchPage);
        }

        private void ShowAssetLibraryPage()
        {
            StopStoryEditorAudio();
            ResetCreateAssetLibraryForm();
            _currentAssetLibrary = null;
            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Visible;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            ProjectTextToolPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            if (!ReferenceEquals(ShellNavigation.SelectedItem, AssetLibraryNavItem))
            {
                SelectShellNavigationItem(AssetLibraryNavItem);
            }
            LoadAssetLibraries();
            PlayPageEntrance(AssetLibraryPage);
        }

        private void ShowAssetLibraryDetailPage(AssetLibraryInfo assetLibrary)
        {
            StopStoryEditorAudio();
            var cancellationToken = ResetAssetLibraryLoadCancellation();
            _currentAssetLibrary = assetLibrary;
            EnsureAssetLibraryCategoryFolders(assetLibrary.Path);
            AssetLibraryDetailTitleText.Text = assetLibrary.Name;
            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Visible;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            ProjectTextToolPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            if (!ReferenceEquals(ShellNavigation.SelectedItem, AssetLibraryNavItem))
            {
                SelectShellNavigationItem(AssetLibraryNavItem);
            }
            RunAssetLibraryLoad(RefreshBackgroundImageCardsAsync(assetLibrary, cancellationToken));
            RunAssetLibraryLoad(RefreshAudioCardsAsync(assetLibrary, AudioAssetKind.Music, cancellationToken));
            RunAssetLibraryLoad(RefreshAudioCardsAsync(assetLibrary, AudioAssetKind.Ambient, cancellationToken));
            RunAssetLibraryLoad(RefreshAudioCardsAsync(assetLibrary, AudioAssetKind.SoundEffect, cancellationToken));
            RunAssetLibraryLoad(LoadFunctionsAsync(assetLibrary, cancellationToken));
            RunAssetLibraryLoad(LoadCharactersAsync(assetLibrary, cancellationToken));
            RunAssetLibraryLoad(LoadCharacterFiltersAsync(assetLibrary, cancellationToken));
            ApplyAssetLibraryMetadataToUi(assetLibrary);
            UpdateAssetLibraryDiskUsage(assetLibrary);
            PlayPageEntrance(AssetLibraryDetailPage);
        }

        private void ShowCreateProjectPage()
        {
            LoadAssetLibraryOptions();
            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            ProjectTextToolPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Visible;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            PlayPageEntrance(CreateProjectPage);

            if (ProjectAssetLibraryComboBox.Items.Count == 0)
            {
                ShowCreateProjectError("还没有素材库，请先到“素材库”页面创建素材库。");
            }
        }

        private void ShowCreateAssetLibraryPage()
        {
            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            ProjectTextToolPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Visible;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Collapsed;
            PlayPageEntrance(CreateAssetLibraryPage);
        }

        private void ShowUnrealSyncPage()
        {
            StopStoryEditorAudio();
            LoadUnrealSyncProjectOptions();
            ApplyUnrealSyncSettingsToUi();
            _currentAssetLibrary = null;
            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            ProjectTextToolPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Visible;
            SettingsPage.Visibility = Visibility.Collapsed;
            if (!ReferenceEquals(ShellNavigation.SelectedItem, UnrealSyncNavItem))
            {
                SelectShellNavigationItem(UnrealSyncNavItem);
            }

            RefreshUnrealSyncStatus();
            PlayPageEntrance(UnrealSyncPage);
        }

        private void ShowSettingsPage()
        {
            StopStoryEditorAudio();
            _currentAssetLibrary = null;
            WorkbenchPage.Visibility = Visibility.Collapsed;
            ProjectDetailPage.Visibility = Visibility.Collapsed;
            StoryEditorPage.Visibility = Visibility.Collapsed;
            AssetLibraryPage.Visibility = Visibility.Collapsed;
            AssetLibraryDetailPage.Visibility = Visibility.Collapsed;
            CharacterDetailPage.Visibility = Visibility.Collapsed;
            BackgroundImageViewerPage.Visibility = Visibility.Collapsed;
            MusicPlayerPage.Visibility = Visibility.Collapsed;
            ProjectTextToolPage.Visibility = Visibility.Collapsed;
            CreateProjectPage.Visibility = Visibility.Collapsed;
            CreateAssetLibraryPage.Visibility = Visibility.Collapsed;
            UnrealSyncPage.Visibility = Visibility.Collapsed;
            SettingsPage.Visibility = Visibility.Visible;
            PlayPageEntrance(SettingsPage);
        }

        private void SelectShellNavigationItem(NavigationViewItem item)
        {
            _isChangingShellSelectionInternally = true;
            try
            {
                ShellNavigation.SelectedItem = item;
            }
            finally
            {
                _isChangingShellSelectionInternally = false;
            }
        }

        private static void PlayPageEntrance(FrameworkElement page)
        {
            if (page.Visibility != Visibility.Visible)
            {
                return;
            }

            page.Transitions = null;
            page.Resources["PageEntranceStoryboard"] = null;

            if (page.RenderTransform is not TranslateTransform transform)
            {
                transform = new TranslateTransform();
                page.RenderTransform = transform;
            }

            transform.X = PageEntranceOffsetX;
            transform.Y = 0;
            page.Opacity = 0;

            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
            var slideAnimation = new DoubleAnimation
            {
                From = PageEntranceOffsetX,
                To = 0,
                Duration = PageEntranceDuration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(slideAnimation, transform);
            Storyboard.SetTargetProperty(slideAnimation, nameof(TranslateTransform.X));

            var fadeAnimation = new DoubleAnimation
            {
                From = 0.82,
                To = 1,
                Duration = PageEntranceDuration,
                EasingFunction = easing
            };
            Storyboard.SetTarget(fadeAnimation, page);
            Storyboard.SetTargetProperty(fadeAnimation, nameof(UIElement.Opacity));

            var storyboard = new Storyboard();
            storyboard.Children.Add(slideAnimation);
            storyboard.Children.Add(fadeAnimation);
            storyboard.Completed += (_, _) =>
            {
                transform.X = 0;
                transform.Y = 0;
                page.Opacity = 1;
                page.Resources.Remove("PageEntranceStoryboard");
            };
            page.Resources["PageEntranceStoryboard"] = storyboard;
            storyboard.Begin();
        }

    }
}
