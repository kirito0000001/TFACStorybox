using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace GalExcleTools.Views;

internal static class DialogContentFactory
{
    public static ScrollViewer CreateProjectRootHelpContent()
    {
        var panel = CreateHelpPanel();
        panel.Children.Add(CreateHelpHeading("整体项目位置"));
        panel.Children.Add(CreateHelpParagraph("这里设置的是所有 GalExcleTools 项目的总存放目录。程序启动时会检查这个目录，不存在就自动创建。"));
        panel.Children.Add(CreateHelpHeading("选择位置"));
        panel.Children.Add(CreateHelpParagraph("点击“选择位置”时，你选择的是父目录。程序会自动在该目录下追加 GalExcelProject 文件夹名。"));
        panel.Children.Add(CreateHelpCodeBlock("""
            示例：
            选择：E:\VNWork
            实际使用：E:\VNWork\GalExcelProject
            """));
        panel.Children.Add(CreateHelpHeading("迁移规则"));
        panel.Children.Add(CreateHelpParagraph("如果你更换了目录，程序会把旧目录里的所有文件复制到新目录，逐个校验文件大小和 SHA-256 内容哈希。全部确认无误后，才会保存新路径并删除旧目录。"));
        panel.Children.Add(CreateHelpHeading("安全限制"));
        panel.Children.Add(CreateHelpParagraph("新目录不能放在旧目录里面。这样可以避免迁移后删除旧目录时，把新目录也一起删除。"));

        return CreateHelpScrollViewer(panel);
    }

    public static ScrollViewer CreateLogHelpContent()
    {
        var panel = CreateHelpPanel();
        panel.Children.Add(CreateHelpHeading("辅助显示"));
        panel.Children.Add(CreateHelpParagraph("这里集中控制不影响项目文件内容的辅助界面。工作区路径用于查看当前文件位置；底部输出框用于记录程序触发、进度、用户操作、提示和错误。"));
        panel.Children.Add(CreateHelpHeading("用户操作"));
        panel.Children.Add(CreateHelpParagraph("用户操作会记录创建项目、创建素材库、导入素材、排序、备注、切换目录等动作。故事编辑器的数据编辑会额外记录可撤回操作，方便用 Ctrl+Z 或撤回按钮回到上一步。"));
        panel.Children.Add(CreateHelpHeading("提示和错误"));
        panel.Children.Add(CreateHelpParagraph("提示用于标记潜在风险或不规范操作；错误会带上失败原因。关闭对应开关后，底部输出框会过滤该类型。"));

        return CreateHelpScrollViewer(panel);
    }

    public static UIElement CreateAssetIndexSyncResultContent(AssetIndexSyncResult result)
    {
        var panel = CreateResultPanel();
        panel.Children.Add(new TextBlock
        {
            Text = $"已扫描 {result.ScannedCsvCount} 个 CSV，更新 {result.ChangedCsvCount} 个 CSV，变更 {result.ChangeCount} 处，异常 {result.WarningCount} 处。",
            TextWrapping = TextWrapping.Wrap
        });

        if (result.Changes.Count > 0)
        {
            panel.Children.Add(CreateResultHeading("变更前后对比"));
            panel.Children.Add(CreateScrollableTextBlock(string.Join("\n", result.Changes.Take(80).Select(change =>
                $"{change.ProjectName}/{change.ChapterName}/{change.CsvName} 行{change.RowName} {change.ColumnName}: {change.OldValueLabel} -> {change.NewValueLabel}"))));
        }

        if (result.Warnings.Count > 0)
        {
            panel.Children.Add(CreateResultHeading("需要注意的数据"));
            panel.Children.Add(CreateScrollableTextBlock(string.Join("\n", result.Warnings.Take(80).Select(warning =>
                $"{warning.ProjectName}/{warning.ChapterName}/{warning.CsvName} 行{warning.RowName} {warning.ColumnName}: {warning.Message}"))));
            panel.Children.Add(CreateSubtleParagraph("这些数据没有被强行改动。可以在章节卡右键点击“修复”做单章体检和保守修复。"));
        }

        return panel;
    }

    public static UIElement CreateChapterRepairResultContent(ChapterRepairResult result)
    {
        var panel = CreateResultPanel();
        panel.Children.Add(new TextBlock
        {
            Text = $"已扫描 {result.ScannedCsvCount} 个 CSV，发现 {result.IssueCount} 处异常。其中 {result.AutoFixableCount} 处可以自动归零修复。",
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(CreateScrollableTextBlock(string.Join("\n", result.Issues.Take(100).Select(issue =>
            $"{issue.ProjectName}/{issue.ChapterName}/{issue.CsvName} 行{issue.RowName} {issue.ColumnName}: {issue.Message}{(issue.CanAutoFix ? " [可自动修复]" : " [需手动确认]")}"))));

        return panel;
    }

    private static ScrollViewer CreateHelpScrollViewer(UIElement content)
    {
        return new ScrollViewer
        {
            Width = 440,
            MaxHeight = 420,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollMode = ScrollMode.Auto,
            Content = content
        };
    }

    private static StackPanel CreateHelpPanel()
    {
        return new StackPanel
        {
            Spacing = 12,
            Width = 420,
            HorizontalAlignment = HorizontalAlignment.Left
        };
    }

    private static StackPanel CreateResultPanel()
    {
        return new StackPanel
        {
            Spacing = 10,
            Width = 720
        };
    }

    private static TextBlock CreateHelpHeading(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = 18,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
    }

    internal static TextBlock CreateResultHeading(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
        };
    }

    private static TextBlock CreateHelpParagraph(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Width = 420,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
        };
    }

    internal static TextBlock CreateSubtleParagraph(string text)
    {
        return new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
        };
    }

    private static Border CreateHelpCodeBlock(string text)
    {
        return new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12),
            Background = Application.Current.Resources["LayerFillColorAltBrush"] as Brush,
            Child = new TextBlock
            {
                Text = text,
                FontFamily = new FontFamily("Consolas"),
                Width = 396,
                TextWrapping = TextWrapping.Wrap
            }
        };
    }

    private static UIElement CreateScrollableTextBlock(string text)
    {
        return new ScrollViewer
        {
            MaxHeight = 260,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontFamily = new FontFamily("Consolas"),
                Foreground = Application.Current.Resources["TextFillColorSecondaryBrush"] as Brush
            }
        };
    }
}
