# AI 剧情修补模式实施计划

> 实施时按测试驱动顺序完成，每个大阶段独立提交。

## 目标

在现有 V1 `append` 续写协议之外增加 `patch-empty`，通过剧情行 `StableId` 只补齐既有空字段。V1 首批支持 `TalkChar/Tesxt`；V2/V3 沿用同一补丁结构扩展字段白名单。

## 文件边界

- `Models/AiStoryFillModels.cs`：任务选项、修补来源、预览和结果模型。
- `Services/AiStoryFillService.cs`：公共导出、预览、导入分派以及原有追加模式。
- `Services/AiStoryFillService.Patch.cs`：修补来源生成、字段白名单、逐行基线校验和事务写入。
- `Views/AiStoryFillDialogContentFactory.cs`：任务类型、协议版本、修补字段和差异预览。
- `MainWindow.Workbench.AiStoryFill.cs`：把页面配置传给服务，不承载协议规则。
- `Scripts/Test-AiStoryFillService.ps1`：修补导出、冲突、重复导入、非目标字段保护。
- `Scripts/Test-AiStoryFillUi.ps1`：入口和配置界面的源码回归检查。

## 阶段一：协议与内核

- [ ] 先增加失败测试：`patch-empty` 导出必须生成包含 `StableId`、上下文和行基线的 `source.jsonl`。
- [ ] 先增加失败测试：结果只能补导出时允许的空字段，已有值不得覆盖。
- [ ] 先增加失败测试：无关素材字段变化不冲突，`TalkChar/Tesxt` 基线变化只阻止对应行。
- [ ] 先增加失败测试：确认导入后行数和顺序不变，创建备份并记录同包导入状态。
- [ ] 实现模型和独立修补服务文件，使上述测试通过。
- [ ] 运行专项测试、Debug 构建并提交。

## 阶段二：界面

- [ ] 先更新 UI 源码测试，要求任务类型包含“续写章节/补齐已有内容”。
- [ ] 配置页在修补模式显示“当前说话人/剧情文本”字段选择，默认只选当前说话人。
- [ ] 续写模式保留原稿起点；修补模式禁用该输入并由工具箱生成来源行。
- [ ] 预览按“行号、正文、原值、新值、原稿段落、状态”显示。
- [ ] 运行专项测试、Debug 构建并提交。

## 阶段三：收尾

- [ ] 更新外接程序规范中的两种任务模式和 JSONL 示例。
- [ ] 运行 `Test-AiStoryFillService.ps1`、`Test-AiStoryFillUi.ps1`、`Test-SourceHealth.ps1 -Build`。
- [ ] 检查工作树和提交记录，不启动程序，由 AX 负责启动。
