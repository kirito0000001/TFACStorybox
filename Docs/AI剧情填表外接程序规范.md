# AI 剧情填表外接程序规范

## 1. 目标

为剧情工具箱增加一条安全的外接填表链路：

```text
Word 原稿 -> 外接程序 -> JSONL 结果包 -> 剧情卡右键预览 -> 合并到 StoryDocument
```

第一版只支持在指定锚点后追加剧情，不覆盖、不删除已有剧情行。

AI 和外接程序只负责剧情文本与说话人。素材、函数、语音、立绘、小节路线和其他字段均由剧情工具箱管理。

## 2. 职责边界

### 剧情工具箱

- 从章节卡导出 AI 填表任务。
- 生成目标章节、锚点、文件哈希和说话人词典。
- 读取外接程序产生的结果包。
- 校验章节、锚点、哈希、记录 ID、状态和字段范围。
- 显示逐句导入预览。
- 确认后创建备份并原子保存 StoryDocument。
- 保存已导入原稿 ID，防止重复追加。

### 外接程序

- 读取任务包和 Word 原稿。
- 按 Word 段落生成稳定原稿 ID。
- 提取角色提示、原始文本和上下文。
- 生成 `source.jsonl`。
- 接收并校验 AI 填写的 `result.jsonl`。
- 不直接修改 StoryDocument、CSV 或 Unreal 资产。

### AI

- 只填写 `TalkChar` 和 `Tesxt`。
- 不填写或推断素材、函数、语音、立绘和小节结构。
- 对制作备注输出 `skip`。
- 对无法可靠判断的说话人输出 `review`，不强行猜测。

## 3. 任务包结构

```text
TFAC-AI-Story-WHK-M3-00/
├─ manifest.json
├─ speaker-catalog.json
├─ source.jsonl
└─ result.jsonl
```

工具箱导出时先生成 `manifest.json` 和 `speaker-catalog.json`。外接程序补充 `source.jsonl`，AI 填写 `result.jsonl`。

所有文件使用 UTF-8，JSON 中文不得强制转为 `\uXXXX`。

## 4. manifest.json

```json
{
  "schemaVersion": 1,
  "packageType": "TFAC.AIStoryFill",
  "packageId": "唯一 GUID",
  "generatedAt": "ISO-8601 时间",
  "chapterCode": "WHK-M3-00",
  "chapterName": "第三章",
  "mode": "append",
  "anchorRowId": "当前最后一句 StableId",
  "anchorRowIndex": 322,
  "storyDocumentSha256": "导出时 story.document.json 的 SHA-256",
  "sourceStartParagraph": 1241,
  "allowedFields": ["TalkChar", "Tesxt"]
}
```

规则：

- `chapterCode` 必须和右键选择的章节一致。
- `mode` 第一版只能是 `append`。
- `anchorRowId` 必须仍存在。
- `storyDocumentSha256` 不一致时禁止静默导入，要求重新导出任务。
- `allowedFields` 必须严格等于 `TalkChar/Tesxt`。

## 5. speaker-catalog.json

```json
{
  "schemaVersion": 1,
  "characters": [
    { "name": "夏疏影", "code": "Xia" },
    { "name": "神秘少女", "code": "Unknown1" },
    { "name": "绮", "code": "Qi" }
  ],
  "customSpeakers": ["梦凪", "相机店老板", "杂货店老板"],
  "narrationValue": ""
}
```

有角色卡的人物优先输出英文代号。没有角色卡的临时人物使用原稿中文名。旁白使用空字符串。

## 6. source.jsonl

每个物理行是一条 JSON 记录：

```json
{"id":"docx-p1241","order":1241,"raw":"旁白：据说是工作忙……","speakerHint":"旁白","textHint":"据说是工作忙……","contextBefore":["..."],"contextAfter":["..."]}
```

要求：

- `id` 在任务包中唯一且稳定。
- `order` 保持 Word 原稿顺序。
- 上下文只用于判断，不直接写入剧情。
- 每批建议包含 50-80 条记录，前后各附 3-5 条上下文。

## 7. result.jsonl

```json
{"id":"docx-p1241","TalkChar":"","Tesxt":"据说是工作忙……","status":"include","confidence":"high"}
{"id":"docx-p1247","TalkChar":null,"Tesxt":null,"status":"skip","confidence":"high"}
{"id":"docx-p1287","TalkChar":null,"Tesxt":null,"status":"review","confidence":"low"}
```

状态：

- `include`：允许进入导入预览。
- `skip`：制作备注或明确不进入剧情的段落。
- `review`：必须人工确认，第一版禁止自动导入。

`include` 记录要求：

- `Tesxt` 非空。
- `TalkChar` 必须是字符串，旁白使用 `""`。
- 不允许出现协议之外的剧情字段。

## 8. 章节卡交互

章节卡右键增加：

```text
AI 剧情填表
```

打开管理弹窗：

- 导出填表任务。
- 合并导入结果。
- 查看导入记录。

导入预览至少显示：

- 原稿 ID 与顺序。
- 说话人。
- 剧情文本。
- include/skip/review/duplicate 状态。
- 将执行的操作。

只有没有阻断错误时才允许“确认合并”。

## 9. 合并规则

1. 重新读取最新 StoryDocument。
2. 校验章节代号、锚点和文件哈希。
3. 读取已导入 ID 记录。
4. `skip` 不写入。
5. `review` 阻止确认。
6. 已导入 ID 标记为 duplicate，不重复写入。
7. 每个 `include` 创建新的 StoryRow。
8. 仅设置 `TalkChar` 与 `Tesxt`。
9. 其他字段使用 StoryDocument 默认值。
10. 新行归入锚点所在小节。
11. 按 result 顺序插入锚点之后。
12. 重新编号 Name 与 RowSections。
13. 保存前备份 `story.document.json`。
14. StoryDocument 保存成功后写入导入记录。

## 10. 导入记录

章节目录新增：

```text
ai-story-import-state.json
```

```json
{
  "schemaVersion": 1,
  "packages": [
    {
      "packageId": "GUID",
      "importedAt": "ISO-8601 时间",
      "sourceIds": ["docx-p1241", "docx-p1242"]
    }
  ]
}
```

该文件只保存导入来源，不参与 Unreal 同步。

## 11. 错误与安全要求

- 章节不匹配：拒绝导入。
- 锚点不存在：拒绝导入。
- StoryDocument 哈希变化：拒绝导入并要求重新导出。
- ID 重复：阻止或标记 duplicate。
- JSONL 某行无法解析：指出文件行号。
- 未登记角色允许作为自定义说话人，但必须在预览中标记。
- 导入过程不得修改 CSV；CSV 仍只在同步 Unreal 时生成。
- 外接程序不得直接访问 Unreal 资产。

## 12. 第一版验收标准

- 能从章节卡导出任务清单。
- 能读取合法 JSONL 结果包。
- 能发现章节、锚点和哈希错误。
- 能识别 skip/review/duplicate。
- 能预览所有待追加文本。
- 确认后只修改 TalkChar/Tesxt。
- 取消后 StoryDocument 哈希不变。
- 重复导入同一结果包不会产生重复行。
- 导入前存在可恢复备份。
