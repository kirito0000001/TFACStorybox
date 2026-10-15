$ErrorActionPreference = 'Stop'

# TODO 备注的定位键：以前认「章节 + 行号」，插一句 / 删一句之后备注就贴到别的句子上；
# 现在认 StableId，行号只当显示。这个脚本钉住"身份优先 + 老文件读得回来"。
$root = Split-Path -Parent $PSScriptRoot
$issues = [System.Collections.Generic.List[string]]::new()

function Require-Pattern([string]$path, [string]$pattern, [string]$message, [switch]$NotMatch) {
    $matched = (Test-Path -LiteralPath $path) -and
        ((Get-Content -Raw -Encoding UTF8 -LiteralPath $path) -match $pattern)
    if ($NotMatch) { $matched = -not $matched }
    if (-not $matched) {
        $issues.Add($message)
    }
}

Require-Pattern `
    (Join-Path $root 'Models\StoryTodoModels.cs') `
    'public string StableId \{ get; init; \} = string\.Empty;' `
    'Todo notes must carry a stable row id (init-only, so old files stay readable).'

Require-Pattern `
    (Join-Path $root 'MainWindow.Story.Todo.cs') `
    'FindStoryTodoIndex[\s\S]*?HasStoryTodoStableId\(note\)[\s\S]*?!HasStoryTodoStableId\(note\)[\s\S]*?note\.RowNumber == rowNumber' `
    'Todo lookup must prefer the stable row id and only fall back to the row number for unmigrated notes.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Story.Todo.cs') `
    'ApplyStoryTodoTextToCurrentRow[\s\S]*?FindStoryTodoIndex\(chapterCode, stableId, rowNumber\)[\s\S]*?StableId = stableId' `
    'Writing a todo note must resolve and store the stable row id.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Story.Todo.cs') `
    'FindStoryRowIndexByStableId\(note\.StableId\)[\s\S]*?HasStoryTodoStableId\(note\)' `
    'Jumping to a todo note must resolve the row by id instead of trusting a stale row number.'

Require-Pattern `
    (Join-Path $root 'MainWindow.Story.Todo.cs') `
    'MigrateStoryTodoStableIdsForCurrentChapter' `
    'Old row-number-only notes must be migrated to stable ids when their chapter is opened.'

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Error $_ }
    exit 1
}

$assemblyPath = Join-Path $root 'bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\TFAC剧情箱-轮椅版.dll'
if (Test-Path -LiteralPath $assemblyPath) {
    $assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path $assemblyPath))
    $noteType = $assembly.GetType('GalExcleTools.StoryTodoNote', $true)
    $storeType = $assembly.GetType('GalExcleTools.Services.StoryTodoStore', $true)
    if ($null -eq $noteType.GetProperty('StableId')) {
        Write-Host '(skip todo persistence reflection: 当前 DLL 早于本次改动，重新编译后会做「老文件读回 + 新字段落盘」这两项。)'
    }
    else {
        $tempRoot = Join-Path $env:TEMP ('gal-todo-' + [guid]::NewGuid().ToString('N'))
        $toolsFolder = Join-Path $tempRoot 'Tools'
        $todoPath = Join-Path $toolsFolder 'story-todos.json'
        try {
            New-Item -ItemType Directory -Path $toolsFolder -Force | Out-Null

            # 老文件（只有行号、没有 StableId）：必须读得回来，而且读出来是空串 ——
            # 空串就是"还没迁移"的标记，正好走行号兜底那条老路。
            $legacyJson = @'
{
  "Version": 1,
  "Notes": [
    {
      "ChapterCode": "WHK-M1",
      "RowNumber": 3,
      "Section": 2,
      "Text": "老备注",
      "Done": false,
      "UpdatedAt": "2026-01-01T00:00:00+08:00"
    }
  ]
}
'@
            [System.IO.File]::WriteAllText($todoPath, $legacyJson, [System.Text.UTF8Encoding]::new($false))
            $loaded = @($storeType.GetMethod('Load').Invoke($null, @($tempRoot)))
            if ($loaded.Count -ne 1 -or
                $loaded[0].RowNumber -ne 3 -or
                $loaded[0].Text -ne '老备注' -or
                $loaded[0].StableId -ne '') {
                throw 'A legacy todo file without StableId must load as an empty id (migration marker).'
            }

            # 再存一次：新文件要写 StableId 字段，版本号为 2。
            # 用强类型的 List<StoryTodoNote> 传进去 —— 别让 PowerShell 去猜 IEnumerable<T> 的转换。
            $noteListType = [System.Collections.Generic.List``1].MakeGenericType($noteType)
            $noteList = [System.Activator]::CreateInstance($noteListType)
            foreach ($item in $loaded) { [void]$noteList.Add($item) }
            $saved = $storeType.GetMethod('Save').Invoke($null, @($tempRoot, $noteList))
            $savedText = Get-Content -Raw -Encoding UTF8 -LiteralPath $todoPath
            if (-not $saved -or $savedText -notmatch '"StableId"' -or $savedText -notmatch '"Version":\s*2') {
                throw 'Saving todo notes must persist the StableId field and version 2.'
            }

            # 新格式读回来要保住身份（这是"备注跟着句子走"的前提）。
            $modernJson = @'
{
  "Version": 2,
  "Notes": [
    {
      "ChapterCode": "WHK-M1",
      "RowNumber": 9,
      "Section": 4,
      "Text": "新备注",
      "Done": true,
      "UpdatedAt": "2026-01-02T00:00:00+08:00",
      "StableId": "0123456789abcdef0123456789abcdef"
    }
  ]
}
'@
            [System.IO.File]::WriteAllText($todoPath, $modernJson, [System.Text.UTF8Encoding]::new($false))
            $reloaded = @($storeType.GetMethod('Load').Invoke($null, @($tempRoot)))
            if ($reloaded.Count -ne 1 -or
                $reloaded[0].StableId -ne '0123456789abcdef0123456789abcdef' -or
                -not $reloaded[0].Done) {
                throw 'Reloading a modern todo file must keep the stable row id and the done flag.'
            }
        }
        finally {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Write-Host 'Story todo-note checks passed.'
