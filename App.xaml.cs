using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using GalExcleTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
using Windows.Foundation;
using Windows.Foundation.Collections;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace GalExcleTools
{
    /// <summary>
    /// Provides application-specific behavior to supplement the default Application class.
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        /// <summary>
        /// Initializes the singleton application object.  This is the first line of authored code
        /// executed, and as such is the logical equivalent of main() or WinMain().
        /// </summary>
        public App()
        {
            ApplySavedThemeBeforeFirstWindow();

            InitializeComponent();
            UnhandledException += App_UnhandledException;
        }

        /// <summary>
        /// 启动时把**应用主题**钉到用户设置的那个主题上。
        ///
        /// 【为什么非钉不可】代码里取画笔只能写
        /// <c>Application.Current.Resources["…Brush"]</c>，那条查找路按**应用主题**解析，
        /// 而这个值从来没人设过 —— 它落在 WinUI 的默认上，**既不是元素主题、也不一定跟系统走**。
        /// 2026-10-16 实测（系统白天模式 + 界面浅色）它就落在**深色**那套上：
        ///   · 素材库里图片名字取到 <c>TextFillColorSecondaryBrush</c> 的深色值（#C5FFFFFF），
        ///     压在浅色卡片上就是看不见的白字；
        ///   · 同步对比表的行取到纯白，整片空白。
        /// 界面主题（<c>RootGrid.RequestedTheme</c>，由「夜间模式」开关定）只管 XAML 那半边，
        /// 管不到这条路 —— 两边就这么分了家。
        ///
        /// 【为什么在这里】<c>Application.RequestedTheme</c> 只能在**第一个窗口建出来之前**设，
        /// 之后就设不动了（运行中切换靠 MainWindow 里那次画笔覆盖兜着）。
        /// 兜底：这里失败最多是颜色退回旧毛病，绝不能拦启动。
        /// </summary>
        private void ApplySavedThemeBeforeFirstWindow()
        {
            try
            {
                var settings = new AppSettingsService().Load();
                RequestedTheme = settings.NightModeEnabled
                    ? ApplicationTheme.Dark
                    : ApplicationTheme.Light;
            }
            catch
            {
                // 读设置失败就维持默认主题：启动优先。
            }
        }

        private static void App_UnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            try
            {
                var directoryPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "GalExcleTools");
                Directory.CreateDirectory(directoryPath);
                File.AppendAllText(
                    System.IO.Path.Combine(directoryPath, "crash.log"),
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}\r\n\r\n");
            }
            catch
            {
                // Best effort crash logging only.
            }
        }

        /// <summary>
        /// Invoked when the application is launched.
        /// </summary>
        /// <param name="args">Details about the launch request and process.</param>
        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window = new MainWindow();
            _window.Activate();
        }
    }
}
