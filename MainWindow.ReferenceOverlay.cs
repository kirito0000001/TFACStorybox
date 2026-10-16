using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using GalExcleTools.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using static GalExcleTools.Services.TextUtility;

namespace GalExcleTools
{
    /// <summary>
    /// 参考层：F1 快捷键大全 / 设置页 Tips、F2 当前章节参考。
    ///
    /// 【照隔壁零境工具箱移植的】那边是一整套 F1–F4 的"文本参考浮层"：
    ///   · F1：上下文帮助 —— 在设置页给「设置页 Tips 合集」，其它页给「快捷键大全 + 各页说明」；
    ///   · F2：当前对象的草稿 —— 读那个对象目录里的草稿文本，没选中就提示一句；
    ///   · Esc：关掉最上面那层；右键也能关。
    /// 打开方式那边用的是挂在根元素上的 <c>KeyboardAccelerator</c>，所以**任何页面**都按得动 ——
    /// 这一条是重点：挂在某个页面上就只能在那页按。
    ///
    /// 【这边怎么适配】"对象"从角色换成**章节**：F2 给当前章节的参考
    /// （概览 + 小节结构 + 本章备注），没打开章节就提示一句（对应零境的"未选择角色"）。
    ///
    /// 【浮层形态】XAML 里的 ReferenceOverlayHost，形状照 ChapterChoiceManagerHost：
    /// 满屏半透明底 + 居中卡片 + Esc/右键/关闭按钮。这里只负责填内容与显隐。
    /// </summary>
    public sealed partial class MainWindow
    {
        /// <summary>
        /// 注册 F1 / F2 两个加速器。
        ///
        /// 挂在 RootGrid 上并指定 ScopeOwner：加速器是"窗口级"的，
        /// 不挂在某个页面上（挂页面上离开那页就失效）。
        /// </summary>
        private void RegisterReferenceOverlayShortcuts()
        {
            // 加速器挂在 RootGrid 上有个副作用：WinUI 会把它的键位当成"提示"画在光标旁
            //（用户 2026-10-16 截图里那个跟着鼠标跑的 F1 就是它）。RootGrid 是整页，
            // 于是全屏到处都在显示 —— 关掉。键位说明走 F1 那份表就够，不需要到处飘一个角标。
            RootGrid.KeyboardAcceleratorPlacementMode = KeyboardAcceleratorPlacementMode.Hidden;

            var shortcutGuideAccelerator = new KeyboardAccelerator { Key = VirtualKey.F1 };
            shortcutGuideAccelerator.ScopeOwner = RootGrid;
            shortcutGuideAccelerator.Invoked += ReferenceShortcutGuideAccelerator_Invoked;
            RootGrid.KeyboardAccelerators.Add(shortcutGuideAccelerator);

            var currentContextAccelerator = new KeyboardAccelerator { Key = VirtualKey.F2 };
            currentContextAccelerator.ScopeOwner = RootGrid;
            currentContextAccelerator.Invoked += ReferenceCurrentContextAccelerator_Invoked;
            RootGrid.KeyboardAccelerators.Add(currentContextAccelerator);
        }

        private void ReferenceShortcutGuideAccelerator_Invoked(
            KeyboardAccelerator sender,
            KeyboardAcceleratorInvokedEventArgs args)
        {
            ShowShortcutGuideOverlay();
            args.Handled = true;
        }

        private void ReferenceCurrentContextAccelerator_Invoked(
            KeyboardAccelerator sender,
            KeyboardAcceleratorInvokedEventArgs args)
        {
            ShowCurrentChapterReferenceOverlay();
            args.Handled = true;
        }

        /// <summary>
        /// F1：在设置页给 Tips 合集，其它页给快捷键大全 —— 这就是零境那条"上下文帮助"的口径。
        /// </summary>
        private void ShowShortcutGuideOverlay()
        {
            if (SettingsPage.Visibility == Visibility.Visible)
            {
                ShowReferenceOverlay("设置页 Tips 合集", BuildSettingsTipsText());
                AppendLog(LogKind.User, "已通过 F1 打开设置页 Tips 合集。");
                return;
            }

            ShowReferenceOverlay("快捷键大全", BuildShortcutGuideText());
            AppendLog(LogKind.User, "已通过 F1 打开快捷键大全。");
        }

        /// <summary>F2：当前章节参考；没打开章节就给一句话（对应零境的"未选择角色"）。</summary>
        private void ShowCurrentChapterReferenceOverlay()
        {
            var chapter = _currentStoryChapter;
            if (chapter is null)
            {
                ShowReferenceOverlay(
                    "当前没有打开章节",
                    "现在没有打开章节。\n\n"
                    + "先到「项目工作台」点开一个章节进剧情编辑器，再按 F2 —— 那时这里给的是"
                    + "这一章的概览、小节结构和本章备注。");
                AppendLog(LogKind.User, "已通过 F2 打开未打开章节提示。");
                return;
            }

            ShowReferenceOverlay($"{chapter.Name} - 章节参考", BuildCurrentChapterReferenceText(chapter));
            AppendLog(LogKind.User, $"已通过 F2 打开章节参考：{chapter.Name}（{chapter.Code}）。");
        }

        // ── 内容 ────────────────────────────────────────────────────────
        // 只写"代码里确实有"的东西：这张表是给人照着按的，写错比不写更坏。

        private static string BuildShortcutGuideText()
        {
            return string.Join(
                Environment.NewLine,
                [
                    "【全局】",
                    "F1：快捷键大全（在「整体设置」页里按，给的是设置 Tips）",
                    "F2：当前章节参考（正在编辑章节时给章节概览、小节结构、本章备注）",
                    "F12：同 F1（老习惯，保留）",
                    "Esc：关掉当前浮层",
                    "Ctrl+Z / Ctrl+Y：撤销 / 重做（剧情编辑器）",
                    string.Empty,
                    "【剧情编辑器 · 鼠标停在预览里的立绘位上】",
                    "Q / E：切换配饰",
                    "A / D：切换表情",
                    "Z / C：切换服装",
                    "↑ / ↓（或小键盘 8 / 2）：切换滤镜",
                    "← / →（或小键盘 4 / 6）：切换角色",
                    "Tab：清空这一位的立绘（Shift+Tab 仍然走焦点导航）",
                    "Ctrl+C / Ctrl+V：复制 / 粘贴这一位；鼠标停在素材栏上则对素材栏生效",
                    "鼠标侧键：上一句 / 下一句",
                    string.Empty,
                    "【几个区域】",
                    "项目工作台：左键点卡片进剧情编辑器；右键出菜单（修改 / 选项与分支 / AI 剧情填表 /"
                    + " 导入小节 / 导出章节 / 导入章节 / 备份 / 还原 / 修复 / 删除）",
                    "素材库：按分类导入素材；文件名里的编号就是剧情里引用的号",
                    "同步台：先「开始检测」看清差异，再「确认同步到虚幻」",
                    "整体设置：项目根目录、辅助显示、素材库显示、输出日志、夜间模式都在这一页"
                ]);
        }

        private static string BuildSettingsTipsText()
        {
            return string.Join(
                Environment.NewLine,
                [
                    "【整体项目位置】",
                    "所有项目、素材库都在这个根目录下。选父目录即可，程序自己会建 GalExcelProject 文件夹。",
                    "换位置要走「迁移」：迁移失败会保留原目录，不会把东西弄丢。",
                    string.Empty,
                    "【辅助显示】",
                    "控制底部的工作区路径和输出日志显示不显示，改完立即保存。",
                    string.Empty,
                    "【素材库显示】",
                    "控制素材库列表的显示方式。",
                    string.Empty,
                    "【输出日志】",
                    "记录用户操作、提示和错误。关掉之后底部日志面板会隐藏，也不再写新日志。",
                    string.Empty,
                    "【全局快捷键】",
                    "F1：设置页 Tips 合集（就是这一页）",
                    "F2：当前章节参考",
                    "Esc：关掉浮层"
                ]);
        }

        private string BuildCurrentChapterReferenceText(ChapterInfo chapter)
        {
            var rows = _storyRows;
            var document = _storyEditorViewModel.Document;
            var notes = LoadChapterTodoNotes(chapter);
            var sections = CollectStorySections(document, rows);

            var builder = new StringBuilder();
            builder.AppendLine($"{chapter.Name}（{chapter.Code}）");
            builder.AppendLine($"{rows.Count} 句 · {sections.Count} 个小节 · 本章备注 {notes.Count} 条");

            builder.AppendLine();
            builder.AppendLine("【小节结构】");
            if (sections.Count == 0)
            {
                builder.AppendLine("  （这一章还没有句子）");
            }
            else
            {
                foreach (var (section, firstRowIndex) in sections)
                {
                    var preview = NormalizeSingleLineText(rows[firstRowIndex].Get("Tesxt"));
                    if (preview.Length > 26)
                    {
                        preview = preview[..26] + "…";
                    }

                    builder.AppendLine(
                        $"  {StorySectionDisplay.FormatName(section)} · 第 {firstRowIndex + 1} 句起 · "
                        + (preview.Length == 0 ? "（空句）" : preview));
                }
            }

            builder.AppendLine();
            builder.AppendLine("【本章备注】");
            if (notes.Count == 0)
            {
                builder.AppendLine("  （这一章还没有备注）");
            }
            else
            {
                const int maxNotes = 20;
                foreach (var note in notes.Take(maxNotes))
                {
                    builder.AppendLine($"  {(note.Done ? "[已处理] " : string.Empty)}{note.HeaderText}：{note.Text}");
                }

                if (notes.Count > maxNotes)
                {
                    builder.AppendLine($"  …还有 {notes.Count - maxNotes} 条（见剧情编辑器的备注栏）");
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// 小节是**连续行块**，所以"段号变了就是新的一节"就够认出来 ——
        /// 不用去问分支服务，参考层不该为了列个目录去算分支。
        /// </summary>
        private static List<(int Section, int FirstRowIndex)> CollectStorySections(
            StoryDocument document,
            IReadOnlyList<StoryRow> rows)
        {
            var sections = new List<(int Section, int FirstRowIndex)>();
            int? currentSection = null;
            for (var index = 0; index < rows.Count; index++)
            {
                var section = document.RowSections.TryGetValue(rows[index].StableId, out var value) ? value : 1;
                if (currentSection == section)
                {
                    continue;
                }

                currentSection = section;
                sections.Add((section, index));
            }

            return sections;
        }

        private List<StoryTodoNote> LoadChapterTodoNotes(ChapterInfo chapter)
        {
            return StoryTodoStore
                .Load(_currentProject?.Path)
                .Where(note => string.Equals(note.ChapterCode, chapter.Code, StringComparison.OrdinalIgnoreCase))
                .OrderBy(note => note.RowNumber)
                .ToList();
        }

        // ── 浮层本体 ────────────────────────────────────────────────────

        private void ShowReferenceOverlay(string title, string text)
        {
            ReferenceOverlayTitleText.Text = title;
            ReferenceOverlayTextBlock.Text = text;
            ReferenceOverlayHost.Visibility = Visibility.Visible;
            ReferenceOverlayHost.Focus(FocusState.Programmatic);
        }

        private void HideReferenceOverlay()
        {
            if (ReferenceOverlayHost.Visibility != Visibility.Visible)
            {
                return;
            }

            ReferenceOverlayHost.Visibility = Visibility.Collapsed;
            // 文本清掉：不然下次打开前那一瞬会闪出上一次的内容。
            ReferenceOverlayTextBlock.Text = string.Empty;
        }

        private void ReferenceOverlayCloseButton_Click(object sender, RoutedEventArgs e)
        {
            HideReferenceOverlay();
        }

        /// <summary>点卡片本身不关（免得选文本时手一抖就关了）；点卡片外的暗底才关。</summary>
        private void ReferenceOverlayHost_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (ReferenceEquals(e.OriginalSource, ReferenceOverlayHost))
            {
                HideReferenceOverlay();
            }
        }

        private void ReferenceOverlayCard_Tapped(object sender, TappedRoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void ReferenceOverlayHost_RightTapped(object sender, RightTappedRoutedEventArgs e)
        {
            e.Handled = true;
            HideReferenceOverlay();
        }

        private void ReferenceOverlayHost_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == VirtualKey.Escape)
            {
                HideReferenceOverlay();
                e.Handled = true;
            }
        }
    }
}
