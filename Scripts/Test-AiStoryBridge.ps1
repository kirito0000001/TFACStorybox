$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'Tools\TFAC.AiStoryBridge\TFAC.AiStoryBridge.csproj'
if (!(Test-Path -LiteralPath $project)) { throw 'AI story bridge project is missing.' }

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('tfac-ai-bridge-' + [guid]::NewGuid().ToString('N'))
try {
    $docxSource = Join-Path $testRoot 'docx-source'
    [void](New-Item -ItemType Directory -Force -Path (Join-Path $docxSource 'word'))
    @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>
<w:p><w:r><w:t>第二章 测试章节</w:t></w:r></w:p>
<w:p><w:r><w:t>梦凪：“你好”</w:t></w:r></w:p>
<w:p><w:r><w:t>旁白：天气很好。</w:t></w:r></w:p>
<w:p><w:r><w:t>神秘女人：“停下”</w:t></w:r></w:p>
<w:p><w:r><w:t>【制作备注】</w:t></w:r></w:p>
<w:p><w:r><w:t>第三章 下一章</w:t></w:r></w:p>
</w:body></w:document>
'@ | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $docxSource 'word\document.xml')
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $docxPath = Join-Path $testRoot 'story.docx'
    [IO.Compression.ZipFile]::CreateFromDirectory($docxSource, $docxPath)

    $catalog = @'
{"schemaVersion":1,"characters":[{"name":"夏疏影","code":"Xia"},{"name":"绮","code":"Qi"}],"customSpeakers":["梦凪","？"],"narrationValue":""}
'@
    $patchPackage = Join-Path $testRoot 'patch-package'
    [void](New-Item -ItemType Directory -Force -Path $patchPackage)
    @'
{"schemaVersion":1,"protocolVersion":1,"profile":"V1","packageType":"TFAC.AIStoryFill","packageId":"patch-test","chapterCode":"TEST-M2-00","chapterName":"第二章","mode":"patch-empty","sourceStartParagraph":0,"allowedFields":["TalkChar","Tesxt"],"patchFields":["TalkChar"]}
'@ | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $patchPackage 'manifest.json')
    $catalog | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $patchPackage 'speaker-catalog.json')
    @(
        '{"id":"row:a","stableId":"a","rowIndex":0,"section":1,"rowBaselineSha256":"A","TalkChar":"","Tesxt":"“你好”","previousText":"","nextText":"天气很好。"}',
        '{"id":"row:b","stableId":"b","rowIndex":1,"section":1,"rowBaselineSha256":"B","TalkChar":"","Tesxt":"天气很好。","previousText":"“你好”","nextText":"“停下”"}',
        '{"id":"row:c","stableId":"c","rowIndex":2,"section":1,"rowBaselineSha256":"C","TalkChar":"","Tesxt":"“停下”","previousText":"天气很好。","nextText":""}'
    ) | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $patchPackage 'source.jsonl')
    '' | Set-Content -NoNewline -Encoding UTF8 -LiteralPath (Join-Path $patchPackage 'result.jsonl')

    dotnet run --project $project --configuration Debug -- generate --package $patchPackage --docx $docxPath
    if ($LASTEXITCODE -ne 0) { throw "Patch generation failed with exit code $LASTEXITCODE" }
    $patchResults = @(Get-Content -Encoding UTF8 -LiteralPath (Join-Path $patchPackage 'result.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
    if ($patchResults.Count -ne 3 -or
        $patchResults[0].status -ne 'include' -or $patchResults[0].patch.TalkChar -ne '梦凪' -or $patchResults[0].sourceParagraph -ne 2 -or
        $patchResults[1].status -ne 'skip' -or
        $patchResults[2].status -ne 'review') {
        throw 'Patch result generation did not classify known speaker, narration, and unknown alias safely.'
    }

    dotnet run --project $project --configuration Debug -- generate --package $patchPackage --docx $docxPath
    if ($LASTEXITCODE -eq 0) { throw 'Bridge overwrote a non-empty result.jsonl without --force.' }

    $appendPackage = Join-Path $testRoot 'append-package'
    [void](New-Item -ItemType Directory -Force -Path $appendPackage)
    @'
{"schemaVersion":1,"protocolVersion":1,"profile":"V1","packageType":"TFAC.AIStoryFill","packageId":"append-test","chapterCode":"TEST-M2-00","chapterName":"第二章","mode":"append","sourceStartParagraph":0,"allowedFields":["TalkChar","Tesxt"],"patchFields":[]}
'@ | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $appendPackage 'manifest.json')
    $catalog | Set-Content -Encoding UTF8 -LiteralPath (Join-Path $appendPackage 'speaker-catalog.json')
    dotnet run --project $project --configuration Debug -- generate --package $appendPackage --docx $docxPath
    if ($LASTEXITCODE -ne 0) { throw "Append generation failed with exit code $LASTEXITCODE" }
    $appendSource = @(Get-Content -Encoding UTF8 -LiteralPath (Join-Path $appendPackage 'source.jsonl'))
    $appendResults = @(Get-Content -Encoding UTF8 -LiteralPath (Join-Path $appendPackage 'result.jsonl') | ForEach-Object { $_ | ConvertFrom-Json })
    if ($appendSource.Count -ne 4 -or
        $appendResults.Count -ne 4 -or
        $appendResults[0].id -ne 'docx-p2' -or
        $appendResults[0].TalkChar -ne '梦凪' -or
        $appendResults[1].TalkChar -ne '' -or
        $appendResults[2].status -ne 'review' -or
        $appendResults[3].status -ne 'skip') {
        throw 'Append generation did not detect chapter boundaries and classify paragraphs.'
    }
}
finally {
    if (Test-Path -LiteralPath $testRoot) { Remove-Item -LiteralPath $testRoot -Recurse -Force }
}

Write-Host 'AI story bridge checks passed.'
