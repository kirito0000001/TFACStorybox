# TFAC AI 剧情外接程序

读取剧情工具箱导出的任务包和 Word 原稿，生成符合 V1 协议的 `source.jsonl/result.jsonl`。

## 生成结果

```powershell
TFAC-AI剧情外接程序.exe generate `
  --package "D:\TFAC-AI-Story-WHK-M2-00-..." `
  --docx "D:\原稿.docx"
```

- `append`：起始段落为 0 时，按 `chapterName` 自动找到章节标题，并在下一个章节标题前结束。
- `patch-empty`：读取工具箱生成的 `source.jsonl`，按正文与上下文对齐 Word 段落。
- 已登记角色输出角色英文代号，自定义说话人保留原值。
- 旁白、制作备注和无法确认的身份分别输出协议允许的 `include/skip/review`。
- 身份名称无法映射时使用 `review`，不会根据相似名字猜测。

默认拒绝覆盖已有非空结果。确实需要重新生成时使用 `--force`，程序会先保留 `.bak`：

```powershell
TFAC-AI剧情外接程序.exe generate --package "任务包" --docx "原稿.docx" --force
```

结果生成后，在剧情工具箱的章节卡右键打开“AI 剧情填表”，选择“导入填写结果”并选中任务包目录。

## 发布

```powershell
.\Scripts\Publish-AiStoryBridge.ps1
```

默认输出到 `D:\TFAC-AI剧情外接程序`。程序是自包含单 EXE，不要求目标机器预装 .NET。
