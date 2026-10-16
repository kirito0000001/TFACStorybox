$ErrorActionPreference = 'Stop'

# 同步落点的中文名：检查项标题、对比表类别、缺目录提示都该说人话（「背景图」「道具图片」…），
# 而不是一屏英文相对路径。映射表收在 UnrealSyncPathPolicy 里，两边共用一份。
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
    (Join-Path $root 'Services\UnrealSync\UnrealSyncPathPolicy.cs') `
    'GetDestinationDisplayName[\s\S]*?DestinationDisplayNames[\s\S]*?\(BackgroundFolder, "背景图"\)[\s\S]*?\(PropImageFolder, "道具图片"\)[\s\S]*?\(VoiceFolder, "剧情语音"\)[\s\S]*?\(AssetMaterialRoot, "素材总目录"\)' `
    'The destination display names must live next to the landing-point constants.'

# 长前缀必须排在父目录前面，否则 AssetMaterial 会把所有子目录吃掉。
Require-Pattern `
    (Join-Path $root 'Services\UnrealSync\UnrealSyncPathPolicy.cs') `
    '\(BackgroundFolder, "背景图"\)[\s\S]*?\(AssetMaterialRoot, "素材总目录"\)' `
    'Child folders must be matched before their parent in the display-name table.'

Require-Pattern `
    (Join-Path $root 'Services\UnrealSync\UnrealSyncPreflightCheck.cs') `
    'CheckFolder[\s\S]*?GetDestinationDisplayName\(relative\)[\s\S]*?\n\s+relative,' `
    'A folder check must show the Chinese name as its title and keep the relative path on the second line.'

Require-Pattern `
    (Join-Path $root 'Services\UnrealSync\UnrealSyncPreflightCheck.cs') `
    'DetailOverride: exists \? \$"目录已经存在：\{diskPath\}" : \$"没有这个目录：\{diskPath\}"' `
    'A folder check must report the disk path on the detail line.'

Require-Pattern `
    (Join-Path $root 'MainWindow.UnrealSync.cs') `
    'GetUnrealSyncDestinationDisplayName[\s\S]*?return UnrealSyncPathPolicy\.GetDestinationDisplayName\(destination\);' `
    'The window must delegate to the shared display-name table instead of keeping its own copy.'

Require-Pattern `
    (Join-Path $root 'MainWindow.UnrealSync.cs') `
    'return "背景图";' `
    'The duplicated display-name table in the window must stay removed.' -NotMatch

Require-Pattern `
    (Join-Path $root 'MainWindow.UnrealSync.Runner.cs') `
    'GetDestinationDisplayName\(relative\)[\s\S]*?（\{relative\}）' `
    'The missing-folder warning must name each folder in Chinese and keep its relative path.'

# 卡片第二行：期望类型只在第三行**没说类型**的时候才附上 ——
# 合规时第三行就是「资产类型：X」，再说一遍是重复（2026-10-16 用户指出）。
Require-Pattern `
    (Join-Path $root 'Services\UnrealSync\UnrealSyncCheckItem.cs') `
    'IsCompliant && !string\.IsNullOrWhiteSpace\(ActualType\)[\s\S]*?return summary;' `
    'The expected type must be dropped from the summary line exactly when the detail line already reports it.'

Require-Pattern `
    (Join-Path $root 'Services\UnrealSync\UnrealSyncCheckItem.cs') `
    '· 名称 \{CorrectName\}\{type\}' `
    'The old unconditional "type on the summary line" shape must stay removed.' -NotMatch

if ($issues.Count -gt 0) {
    $issues | ForEach-Object { Write-Host "  · $_" -ForegroundColor Red }
    Write-Error "$($issues.Count) 个检查未通过（明细见上面列出的条目）。"
    exit 1
}

$assemblyPath = Join-Path $root 'bin\x64\Debug\net8.0-windows10.0.19041.0\win-x64\TFAC剧情箱-轮椅版.dll'
if (Test-Path -LiteralPath $assemblyPath) {
    $assembly = [System.Reflection.Assembly]::LoadFrom((Resolve-Path $assemblyPath))
    $policyType = $assembly.GetType('GalExcleTools.Services.UnrealSyncPathPolicy', $true)
    $displayNameMethod = $policyType.GetMethod('GetDestinationDisplayName')
    if ($null -eq $displayNameMethod) {
        Write-Host '(skip naming reflection: 当前 DLL 早于本次改动，重新编译后会核对「每个落点都有中文名」这一项。)'
    }
    else {
        $folders = @($policyType.GetProperty('ExpectedContentFolders').GetValue($null))
        $expectedNames = @{
            'AssetMaterial'                                 = '素材总目录'
            'AssetMaterial/ImageS/PlotUse/Background'       = '背景图'
            'AssetMaterial/ImageS/PlotUse/ItemImage'        = '道具图片'
            'AssetMaterial/Sound/BGM/PlotUse'               = 'BGM'
            'AssetMaterial/Sound/Scene_Effect'              = '环境音与特殊音效'
            'AssetMaterial/Sound/Voice'                     = '剧情语音'
            'AssetMaterial/Lustration_Image'                = '立绘素材'
            'AssetMaterial/LustrationPreview_Image'         = '小预览'
            'AssetMaterial/ExcelTexts'                      = '剧情表'
            'BaseC/EXActor'                                 = 'MusicActor 蓝图目录'
            'UIWidget/PlotUIs'                              = '剧情 UI 蓝图目录'
        }

        foreach ($relative in $folders) {
            $name = $displayNameMethod.Invoke($null, @([string]$relative))
            if ([string]::IsNullOrWhiteSpace($name)) {
                throw "落点没有中文名：$relative"
            }
            if ($name.Contains('/')) {
                throw "中文名不该是一条路径：$relative -> $name"
            }
            if ($name -eq $relative) {
                throw "中文名和英文路径一样，等于没换：$relative"
            }
            if (-not $expectedNames.ContainsKey($relative)) {
                throw "新增落点 `$relative` 没有对应的中文名断言 —— 加上它，别让它悄悄显示英文。"
            }
            if ($expectedNames[$relative] -ne $name) {
                throw "落点中文名对不上：$relative -> $name（期望 $($expectedNames[$relative])）"
            }
        }

        if ($folders.Count -ne $expectedNames.Count) {
            throw "落点数（$($folders.Count)）和中文名断言数（$($expectedNames.Count)）不一致。"
        }

        # 两种形态必须给出同一个名字：检查项传的是相对路径，对比表传的是对象路径。
        foreach ($relative in $folders) {
            $objectPath = "/Game/$relative"
            if ($displayNameMethod.Invoke($null, @([string]$relative)) -ne
                $displayNameMethod.Invoke($null, @([string]$objectPath))) {
                throw "相对路径与对象路径给出了不同的名字：$relative"
            }
        }

        # 语音底下带章节：要给到章节号，否则光看"剧情语音"不知道是哪一章。
        $voiceName = $displayNameMethod.Invoke($null, @('/Game/AssetMaterial/Sound/Voice/WHK-M1-00'))
        if ($voiceName -notmatch 'WHK-M1-00' -or $voiceName -notmatch '语音') {
            throw "章节语音应当带上章节号，实际：$voiceName"
        }

        # 认不出来就退回叶子名，别把整条 /Game 路径甩出来。
        if ($displayNameMethod.Invoke($null, @('/Game/SomethingElse/LeafName')) -ne 'LeafName') {
            throw '认不出的落点应当退回叶子名。'
        }
    }

    # ── 卡片那三行怎么写（2026-10-16：合规时第二行不许再重复报类型）──
    $itemType = $assembly.GetType('GalExcleTools.Services.UnrealSyncCheckItem', $true)
    $kindEnum = $assembly.GetType('GalExcleTools.Services.UnrealSyncCheckKind', $true)
    $severityEnum = $assembly.GetType('GalExcleTools.Services.UnrealSyncCheckSeverity', $true)
    $replaceEnum = $assembly.GetType('GalExcleTools.Services.UnrealSyncCheckReplaceTarget', $true)
    $itemCtor = $itemType.GetConstructors()[0]

    function New-CheckItem(
        [string]$DisplayName,
        [string]$ExpectedPath,
        [string]$ActualPath,
        [bool]$Compliant,
        [string]$Problem,
        [string]$ExpectedType,
        [string]$ActualType,
        [string]$Kind) {
        return $itemCtor.Invoke(@(
            $DisplayName, $ExpectedPath, $ActualPath, $Compliant, $Problem, $ExpectedType, $ActualType,
            [System.Enum]::Parse($severityEnum, 'Error'),
            [System.Enum]::Parse($replaceEnum, 'None'),
            [System.Enum]::Parse($kindEnum, $Kind),
            ''))
    }

    # 用**行为**当门槛，而不是猜版本：老 DLL 里合规资产第二行仍带着「· 类型 X」。
    $probe = New-CheckItem '测试资产' '/Game/X/Y.Y' 'D:\Content\X\Y.uasset' $true '' 'WidgetBlueprint' 'WidgetBlueprint' 'Asset'
    if ($probe.ExpectedSummaryText -match '类型') {
        Write-Host '(skip check-item line reflection: 当前 DLL 还没有「合规时第二行不重复报类型」这条改动，重新编译后再跑。)'
    }
    else {
        # ① 合规且类型读到了：第二行不再重复，第三行报类型（用户指出的那处重复）。
        if ($probe.ExpectedSummaryText -ne '建在 /Game/X · 名称 Y') {
            throw "合规资产的第二行不对：$($probe.ExpectedSummaryText)"
        }
        if ($probe.DetailText -ne '资产类型：WidgetBlueprint') {
            throw "合规资产的第三行应当报类型，实际：$($probe.DetailText)"
        }

        # ② 不合规（类型不对）：第二行**必须**留着期望类型，否则没法跟第三行的实际类型对着看。
        $mismatch = New-CheckItem '测试资产' '/Game/X/Y.Y' 'D:\Content\X\Y.uasset' $false '类型不对' 'WidgetBlueprint' 'Blueprint' 'Asset'
        if ($mismatch.ExpectedSummaryText -notmatch '类型 WidgetBlueprint') {
            throw "不合规资产的第二行必须留着期望类型，实际：$($mismatch.ExpectedSummaryText)"
        }
        if ($mismatch.DetailText -notmatch 'Blueprint') {
            throw "不合规资产的第三行应当报实际类型，实际：$($mismatch.DetailText)"
        }

        # ③ 合规但类型读不到：第三行只说"资产已经存在。"，期望类型这时还得靠第二行。
        $unreadable = New-CheckItem '测试资产' '/Game/X/Y.Y' 'D:\Content\X\Y.uasset' $true '' 'WidgetBlueprint' '' 'Asset'
        if ($unreadable.ExpectedSummaryText -notmatch '类型 WidgetBlueprint') {
            throw "读不到类型的合规资产，第二行应当留着期望类型，实际：$($unreadable.ExpectedSummaryText)"
        }
        if ($unreadable.DetailText -ne '资产已经存在。') {
            throw "读不到类型的合规资产，第三行应当是「资产已经存在。」，实际：$($unreadable.DetailText)"
        }

        # ④ 目录那一类没有类型可言：第二行就是路径。
        $folder = New-CheckItem '背景图' 'AssetMaterial/ImageS/PlotUse/Background' '' $false '目录不存在' '' '' 'Directory'
        if ($folder.ExpectedSummaryText -ne 'AssetMaterial/ImageS/PlotUse/Background') {
            throw "目录项的第二行应当是相对路径，实际：$($folder.ExpectedSummaryText)"
        }
        if ($folder.DetailText -notmatch '没有这个目录') {
            throw "目录项的第三行应当说清磁盘实况，实际：$($folder.DetailText)"
        }
    }
}

Write-Host 'Unreal sync destination-naming checks passed.'
