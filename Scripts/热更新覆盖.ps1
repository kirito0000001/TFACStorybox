# TFAC剧情箱-轮椅版 —— 热更新覆盖器（客户端交付物，设计 §9）
#
# 谁在什么时候跑它：
#   工具箱下载并校验完更新包之后，把本脚本**从安装目录复制到**
#   %LOCALAPPDATA%\GalExcleTools\UpdateRunners\ 再从那里启动 ——
#   运行期间不持有安装目录里的任何句柄（§9.3 的实测结论：只要有一个进程的 CWD
#   停在安装目录里，整个目录就没法改名）。启动后工具箱**不主动退出**，
#   而是等本脚本写出 READY 信号（预检全过）才退 —— 这是 §9.9 的握手。
#
# 交付形态（改这个文件之前先看这三条）：
#   · 必须存成 **UTF-8 with BOM**（Windows PowerShell 5.1 按 BOM 判断编码，
#     没有 BOM 时中文会乱码）；
#   · 只用 Windows PowerShell 5.1 就有的语法（**没有**三元运算符 `?:` / `??` / `-notin` 的新重载）；
#   · 脚本逻辑刻意与版本无关：只认包内 update-package.json + update-whitelist.json。
#
# 退出码（照社区成熟更新器的约定，§9.2）：
#   0 成功 / 1 参数错误（含"增量包与 -CurrentVersion 不符"）/ 2 文件复制失败 / 3 SHA-256 校验失败
#   4 解压失败 / 5 文件替换失败（多半是文件被占用）/ 6 其它错误

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][int]$AppProcessId,
    [Parameter(Mandatory = $true)][string]$InstallDir,
    [Parameter(Mandatory = $true)][string]$PackagePath,
    [Parameter(Mandatory = $true)][string]$ExpectedSha256,
    [Parameter(Mandatory = $true)][string]$TargetVersion,
    [Parameter(Mandatory = $true)][string]$ReadySignalPath,
    [string]$ToolboxStableKey = 'GalExcleTools',

    # 源实现有这个开关，本工程按 O3 已定案「不做整目录备份」（§9.5）——
    # 保留参数只为兼容调用方，传了也只记一行日志。
    [switch]$KeepBackup,

    # 工作根目录：日志 / staging / 待清理清单都在这下面。默认是用户级目录，
    # 回归用例会把它指到临时目录，免得污染真实环境。
    [string]$WorkingRoot = '',

    # Windows App SDK 运行时安装器（WindowsAppRuntimeInstall.exe）。不给就只**检查**不安装：
    # 检查不过就中止更新 —— 绝不把一台"没有运行时"的机器升到框架依赖版本（新版会起不来）。
    [string]$RuntimeInstallerPath = '',

    # 自动化用：跳过运行时检查（只在明确知道自己在做什么时用）。
    [switch]$SkipRuntimeCheck,

    # 本机当前版本。增量包必须拿它和包内 baseVersion 对齐；不传就会被闸门拒绝（这是有意的）。
    [string]$CurrentVersion = '',

    # 自动化用（回归用例 / 演练）：不等人按回车、结束时不起新 exe。
    # 手工排障时不要传这两个，人肉看的窗口就该停住。
    [switch]$NoPause,
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$script:ExitCode = 6
$script:TranscriptStarted = $false
$script:Sha256Cache = @{}

function New-ZdFailure {
    param([int]$Code, [string]$Message)
    $exception = New-Object System.Exception($Message)
    $exception.Data['ZdExitCode'] = $Code
    return $exception
}

function Write-ZdStep {
    param([int]$Percent, [string]$Text)
    Write-Host ('[{0,3}%] {1}' -f $Percent, $Text) -ForegroundColor Cyan
    Write-Progress -Activity '热更新覆盖' -Status $Text -PercentComplete $Percent
}

function Write-ZdNote {
    param([string]$Text)
    Write-Host ('       ' + $Text)
}

function Get-ZdFileSha256 {
    param([string]$Path)

    if ($script:Sha256Cache.ContainsKey($Path)) {
        return $script:Sha256Cache[$Path]
    }

    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $stream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        try {
            $bytes = $sha.ComputeHash($stream)
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $sha.Dispose()
    }

    $text = ([System.BitConverter]::ToString($bytes) -replace '-', '').ToLowerInvariant()
    $script:Sha256Cache[$Path] = $text
    return $text
}

function Get-ZdFreeSpaceBytes {
    param([string]$Path)

    $full = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetPathRoot($full)
    $drive = New-Object System.IO.DriveInfo($root)
    return $drive.AvailableFreeSpace
}

# ─────────────────────────────────────────────────────────────────────────────
# 白名单（§9.6）：与 C# 侧 Services/Update/UpdateWhitelist.cs 读同一份 JSON
# ─────────────────────────────────────────────────────────────────────────────
function Read-ZdWhitelist {
    param([string]$ScriptRoot, [string]$InstallDirectory)

    $candidates = @(
        (Join-Path $ScriptRoot 'update-whitelist.json'),
        (Join-Path (Join-Path $InstallDirectory 'Scripts') 'update-whitelist.json')
    )

    foreach ($candidate in $candidates) {
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            continue
        }

        # ★ 一定用 File.ReadAllText + UTF8：WinPS 5.1 的 Get-Content 默认按 ANSI 解码，
        #   带中文的 JSON 会被读成乱码（§21.5 第 11 条）。
        $text = [System.IO.File]::ReadAllText($candidate, [System.Text.Encoding]::UTF8)
        $json = $text | ConvertFrom-Json

        $set = [ordered]@{
            Extensions             = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
            Directories            = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
            ExcludedDirectories    = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
            ExcludedExtensions     = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
            ExcludedNameFragments  = @()
            ExcludedNameSuffixes   = @()
            Path                   = $candidate
        }

        foreach ($item in @($json.extensions)) { [void]$set.Extensions.Add([string]$item) }
        foreach ($item in @($json.directories)) { [void]$set.Directories.Add([string]$item) }
        foreach ($item in @($json.excludedDirectories)) { [void]$set.ExcludedDirectories.Add([string]$item) }
        foreach ($item in @($json.excludedExtensions)) { [void]$set.ExcludedExtensions.Add([string]$item) }
        $set.ExcludedNameFragments = @($json.excludedNameFragments | ForEach-Object { [string]$_ })
        $set.ExcludedNameSuffixes = @($json.excludedNameSuffixes | ForEach-Object { [string]$_ })

        if ($set.Extensions.Count -eq 0) {
            throw (New-ZdFailure 1 "白名单 $candidate 里没有任何扩展名，拒绝用一个空白名单去删文件。")
        }

        return $set
    }

    throw (New-ZdFailure 1 ('找不到更新白名单 update-whitelist.json（找过：' + ($candidates -join '，') + '）。'))
}

function Test-ZdMirrored {
    param([string]$RelativePath, $Whitelist)

    $normalized = $RelativePath.Replace('/', '\').Trim().TrimStart('\')
    if ($normalized.Length -eq 0) {
        return $false
    }

    foreach ($fragment in $Whitelist.ExcludedNameFragments) {
        if ($normalized.IndexOf($fragment, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $false
        }
    }

    foreach ($suffix in $Whitelist.ExcludedNameSuffixes) {
        if ($normalized.EndsWith($suffix, [StringComparison]::OrdinalIgnoreCase)) {
            return $false
        }
    }

    $segments = @($normalized.Split([char]'\', [StringSplitOptions]::RemoveEmptyEntries))
    if ($segments.Count -eq 0) {
        return $false
    }

    foreach ($segment in $segments) {
        if ($Whitelist.ExcludedDirectories.Contains($segment)) {
            return $false
        }
    }

    $extension = [System.IO.Path]::GetExtension($segments[$segments.Count - 1])
    if ($extension.Length -gt 1) {
        $bare = $extension.Substring(1)
        if ($Whitelist.ExcludedExtensions.Contains($bare)) {
            return $false
        }

        if ($Whitelist.Extensions.Contains($bare)) {
            return $true
        }
    }

    return ($segments.Count -gt 1) -and $Whitelist.Directories.Contains($segments[0])
}

# ─────────────────────────────────────────────────────────────────────────────
# 包内清单（§6.2 的五条规则）
# ─────────────────────────────────────────────────────────────────────────────

<#
.SYNOPSIS
读 JSON 对象上的一个属性；**属性不存在时返回默认值，不抛异常**。

.DESCRIPTION
本脚本开了 `Set-StrictMode -Version 2.0`（见文件开头），而严格模式下访问一个
**不存在的属性**会直接抛「在此对象上找不到属性"xxx"」—— 于是像这样一行
    $isDelta = [bool]$manifest.delta
在「整包」清单上就炸：整包**本来就没有** delta 字段
（契约 §8.2 明写"delta 段只在增量包里出现"）。

实测后果（2026-10-08）：整包走覆盖器时在**解压那一步**直接失败、退出码 6，
而错误文案是「在此对象上找不到属性 delta」—— 和"更新包本身有问题"长得一模一样，
极难回溯到严格模式上。

$obj.PSObject.Properties[name] 在严格模式下**不会**因为属性缺失而抛，所以走它。
契约 §3 第 2 条要求解析时按"字段可能不存在"处理，这里把它落到实现上。
#>
function Get-ZdJsonProperty {
    param(
        $Object,
        [string]$Name,
        $Default = $null
    )

    if ($null -eq $Object) { return $Default }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $Default }
    return $property.Value
}

function Resolve-ZdPackageRoot {
    param([string]$StagingRoot, [string]$Key, [string]$Version)

    $candidates = @(Get-ChildItem -LiteralPath $StagingRoot -Recurse -File -Filter 'update-package.json' |
        Sort-Object @{ Expression = { $_.FullName.Length } })
    $problems = New-Object System.Collections.Generic.List[string]

    foreach ($candidate in $candidates) {
        try {
            $text = [System.IO.File]::ReadAllText($candidate.FullName, [System.Text.Encoding]::UTF8)
            $manifest = $text | ConvertFrom-Json
        }
        catch {
            $problems.Add(($candidate.FullName + ' → 读不出来：' + $_.Exception.Message))
            continue
        }

        $manifestKey = [string](Get-ZdJsonProperty $manifest 'toolboxStableKey' '')
        if ($manifestKey -ne $Key) {
            $problems.Add(($candidate.FullName + ' → toolboxStableKey=' + $manifestKey + '（期望 ' + $Key + '）'))
            continue
        }

        # 清单字段一律走 Get-ZdJsonProperty（同一条规矩：严格模式下直接点缺失的字段会抛）。
        $manifestVersion = [string](Get-ZdJsonProperty $manifest 'version' '')
        if ($manifestVersion -ne $Version) {
            $problems.Add(($candidate.FullName + ' → version=' + $manifestVersion + '（期望 ' + $Version + '）'))
            continue
        }

        $entryExe = [string](Get-ZdJsonProperty $manifest 'entryExe' '')
        if ([string]::IsNullOrWhiteSpace($entryExe)) {
            $problems.Add(($candidate.FullName + ' → 没有 entryExe'))
            continue
        }

        # ★ 增量包**通常不含主程序 exe**（那一版 exe 没变就不会进增量），所以这个"文件必须在包里"
        #   的检查只对整包成立；增量包只要求 entryExe 字段写着（用来映射包根与安装目录的关系）。
        $isDeltaPackage = [bool](Get-ZdJsonProperty $manifest 'delta' $false)
        $entryPath = $null
        if (!$isDeltaPackage) {
            # ★ 只按「文件名」找同目录下的条目（两边必须一致，§8.2 第 6 条的告警）。
            $entryPath = Join-Path $candidate.DirectoryName $entryExe
            if (-not (Test-Path -LiteralPath $entryPath -PathType Leaf)) {
                $problems.Add(($candidate.FullName + ' → 同目录下没有 entryExe 指名的文件：' + $entryExe))
                continue
            }

            if ((Get-Item -LiteralPath $entryPath).Length -le 0) {
                $problems.Add(($candidate.FullName + ' → entryExe 是空文件：' + $entryExe))
                continue
            }
        }

        return [pscustomobject]@{
            ManifestPath = $candidate.FullName
            ScriptRoot   = $candidate.DirectoryName
            EntryExePath = $entryPath
            EntryExeName = $entryExe
            Version      = $manifestVersion
            Runtime      = [string](Get-ZdJsonProperty $manifest 'runtime' '')
            WasdkRuntime = [string](Get-ZdJsonProperty $manifest 'windowsAppSdkRuntime' '')
            IsDelta      = [bool](Get-ZdJsonProperty $manifest 'delta' $false)
            BaseVersion  = [string](Get-ZdJsonProperty $manifest 'baseVersion' '')
            DeltaFiles   = @(Get-ZdJsonProperty $manifest 'files' @())
            DeltaDelete  = @(Get-ZdJsonProperty $manifest 'delete' @())
        }
    }

    if ($problems.Count -gt 0) {
        $detail = [string]::Join('；', $problems.ToArray())
    }
    else {
        $detail = '一个 update-package.json 都没找到'
    }

    throw (New-ZdFailure 4 ('从更新包里找不到本工具的包清单（update-package.json）：' + $detail))
}

# ─────────────────────────────────────────────────────────────────────────────
# 动作集合（§9.6「准备」段）—— 与 C# 侧 UpdateApplyPlan.Compute 同一套判定
# ─────────────────────────────────────────────────────────────────────────────
function Get-ZdApplyPlan {
    # ★ delta 模式（2026-10-06 增量更新）：包内只有"变化/新增"的文件，
    #   所以**绝不能**按镜像语义把"安装目录里有、包里没有"的文件当成待删除 ——
    #   那会把没变的文件全删掉。待删除项只认包内显式给的清单，而且照样先过镜像白名单。
    param(
        [string]$InstallDirectory,
        [string]$StagingDirectory,
        $Whitelist,
        [string]$Version,
        [switch]$DeltaMode,
        [AllowEmptyCollection()][string[]]$DeltaDelete = @()
    )

    $installFiles = @{}
    Get-ChildItem -LiteralPath $InstallDirectory -Recurse -File -Force -ErrorAction SilentlyContinue | ForEach-Object {
        $relative = $_.FullName.Substring($InstallDirectory.Length).TrimStart('\')
        $installFiles[$relative] = $_
    }

    $stagingFiles = @{}
    Get-ChildItem -LiteralPath $StagingDirectory -Recurse -File -Force -ErrorAction SilentlyContinue | ForEach-Object {
        $relative = $_.FullName.Substring($StagingDirectory.Length).TrimStart('\')
        $stagingFiles[$relative] = $_
    }

    $toReplace = New-Object System.Collections.Generic.List[object]
    $toDelete = New-Object System.Collections.Generic.List[object]
    $skipped = 0

    foreach ($relative in $stagingFiles.Keys) {
        if (-not (Test-ZdMirrored -RelativePath $relative -Whitelist $Whitelist)) {
            $skipped++
            continue
        }

        $newFile = $stagingFiles[$relative]
        if (-not $installFiles.ContainsKey($relative)) {
            $toReplace.Add([pscustomobject]@{ RelativePath = $relative; NewPath = $newFile.FullName })
            continue
        }

        $oldFile = $installFiles[$relative]
        $same = $false
        if ($oldFile.Length -eq $newFile.Length) {
            $oldHash = Get-ZdFileSha256 -Path $oldFile.FullName
            $newHash = Get-ZdFileSha256 -Path $newFile.FullName
            $same = ($oldHash -eq $newHash)
        }

        if ($same) {
            $skipped++
            continue
        }

        $toReplace.Add([pscustomobject]@{ RelativePath = $relative; NewPath = $newFile.FullName })
    }

    if ($DeltaMode) {
        foreach ($relative in $DeltaDelete) {
            if ([string]::IsNullOrWhiteSpace($relative)) { continue }
            if (-not (Test-ZdMirrored -RelativePath $relative -Whitelist $Whitelist)) {
                Write-ZdNote ('删除清单里有白名单外的项，已忽略：' + $relative)
                $skipped++
                continue
            }
            if ($stagingFiles.ContainsKey($relative)) {
                # 同一个文件既在删除清单又在包里 —— 以"装新的"为准，不删。
                continue
            }
            $toDelete.Add([pscustomobject]@{ RelativePath = $relative; NewPath = $null })
        }
    }
    else {
        foreach ($relative in $installFiles.Keys) {
            if (-not (Test-ZdMirrored -RelativePath $relative -Whitelist $Whitelist)) {
                $skipped++
                continue
            }

            if ($stagingFiles.ContainsKey($relative)) {
                continue
            }

            $toDelete.Add([pscustomobject]@{ RelativePath = $relative; NewPath = $null })
        }
    }

    return [pscustomobject]@{
        ToReplace = $toReplace
        ToDelete  = $toDelete
        Skipped   = $skipped
        OldSuffix = '.old-' + $Version
    }
}

function Assert-ZdDeltaPrecondition {
    # 增量包的前置闸门（两道，缺一不可）：
    #   ① 本机版本必须正好是包内声明的基准版 —— 否则"差异集"根本不成立；
    #   ② 每个"变化文件"的旧内容指纹（baseSha256）必须与本机一致 ——
    #      否则覆盖上去就是一个半新半旧的混合体，这是最难查的坏法。
    # 任何一条不过都**立刻中止、一个文件都不动**（客户端随后会回落到整包下载）。
    param([string]$CurrentVersion, $PackageRoot, [string]$InstallDirectory)

    if (!$PackageRoot.IsDelta) { return }

    if ([string]::IsNullOrWhiteSpace($PackageRoot.BaseVersion)) {
        throw (New-ZdFailure 1 '这是一个增量包，但里面没写基准版本（baseVersion），拒绝执行。')
    }
    if ([string]::IsNullOrWhiteSpace($CurrentVersion)) {
        throw (New-ZdFailure 1 ('这是相对 v' + $PackageRoot.BaseVersion +
                ' 的增量包，但调用方没有传 -CurrentVersion，无法确认本机版本，拒绝执行。'))
    }
    if ($CurrentVersion.Trim() -ne $PackageRoot.BaseVersion.Trim()) {
        throw (New-ZdFailure 1 ('本机版本 v' + $CurrentVersion + ' 不是增量包的基准版 v' +
                $PackageRoot.BaseVersion + '，请改用完整更新包。'))
    }

    $mismatch = New-Object System.Collections.Generic.List[string]
    $checked = 0
    foreach ($entry in $PackageRoot.DeltaFiles) {
        # ★ 这两个字段一律走 Get-ZdJsonProperty：**契约 §8.2 明写「新增文件没有 baseSha256」**，
        #   而本脚本开着 Set-StrictMode 2.0 —— 直接写 $entry.baseSha256 会在"包里带新增文件"
        #   的那种包上抛「在此对象上找不到属性"baseSha256"」（v2.2.0 的增量包里有 9 个新增的
        #   ffmpeg 文件，2026-10-08 实测就挂在解压那一步、退出码 6、一个文件都没动）。
        #   这和上面 delta 字段踩的是同一个坑，别再直接点清单里的可选字段。
        $relative = [string](Get-ZdJsonProperty $entry 'path' '')
        if ([string]::IsNullOrWhiteSpace($relative)) { continue }
        $expected = [string](Get-ZdJsonProperty $entry 'baseSha256' '')
        if ([string]::IsNullOrWhiteSpace($expected)) { continue }
        $local = Join-Path $InstallDirectory $relative
        if (!(Test-Path -LiteralPath $local -PathType Leaf)) {
            $mismatch.Add($relative + '（本机没有这个文件）')
            continue
        }
        $actual = Get-ZdFileSha256 -Path $local
        $checked++
        if (![string]::Equals($actual, $expected, [StringComparison]::OrdinalIgnoreCase)) {
            $shortActual = $actual.Substring(0, [Math]::Min(12, $actual.Length))
            $shortExpected = $expected.Substring(0, [Math]::Min(12, $expected.Length))
            $mismatch.Add($relative + '（本机 ' + $shortActual + '… / 包内基准 ' + $shortExpected + '…）')
        }
    }

    if ($mismatch.Count -gt 0) {
        $head = [string]::Join('；', @($mismatch | Select-Object -First 5))
        $tail = ''
        if ($mismatch.Count -gt 5) { $tail = '（共 ' + $mismatch.Count + ' 项）' }
        throw (New-ZdFailure 3 ('增量包与本机不一致，已中止（没有覆盖任何文件）：' + $head + $tail +
                '。请改用完整更新包。'))
    }

    Write-ZdNote ('增量前置校验通过：基准 v' + $PackageRoot.BaseVersion + '，核对旧内容 ' + $checked + ' 个文件。')
}

function Invoke-ZdOverlay {
    # 两阶段替换 + 回滚。★ 入口 exe 特殊对待：
    #   · 它**不参与腾位改名**，而是先"拷贝备份"到 *.old-<ver>，再在原位直接覆盖 ——
    #     这样任何时刻磁盘上都有一个可启动的 exe（断电/被硬杀也不会留下"双击没反应"的安装目录）；
    #   · 落位阶段它排在最前，让程序尽早可启动。
    #   回滚统一走 Invoke-ZdRollback（逐项重试、跑完不中断、失败的记清单）。
    param(
        [string]$InstallDirectory,
        $Plan,
        [string]$PendingCleanupPath,
        [string]$EntryExeRelative = '',
        [string]$LogDirectory = ''
    )

    $moved = New-Object System.Collections.Generic.List[object]
    $backedUp = New-Object System.Collections.Generic.List[object]
    $copied = New-Object System.Collections.Generic.List[string]

    $entryAction = $null
    $agingOrder = New-Object System.Collections.Generic.List[object]
    foreach ($action in $Plan.ToReplace) {
        if (![string]::IsNullOrWhiteSpace($EntryExeRelative) -and $action.RelativePath -eq $EntryExeRelative) {
            $entryAction = $action
        }
        else {
            [void]$agingOrder.Add($action)
        }
    }
    foreach ($action in $Plan.ToDelete) { [void]$agingOrder.Add($action) }
    if ($null -ne $entryAction) { [void]$agingOrder.Add($entryAction) }

    $stage = '腾位'
    $error = $null
    try {
        # 阶段 1：整批腾位（同卷改名；入口 exe 用拷贝备份，绝不改名）
        foreach ($action in $agingOrder) {
            $original = Join-Path $InstallDirectory $action.RelativePath
            $aged = $original + $Plan.OldSuffix

            # ★ 包里新增、安装目录里本来没有的文件：没有旧件要腾位，直接跳过。
            #   少了这个判断，「包里有任何一个新文件」都会让整批腾位在第一处抛
            #   FileNotFoundException → 整次更新白做（真实升级恰恰每次都会带新文件）。
            if (-not (Test-Path -LiteralPath $original -PathType Leaf)) {
                continue
            }

            if (Test-Path -LiteralPath $aged) {
                Remove-Item -LiteralPath $aged -Force
            }

            if (($null -ne $entryAction) -and ($action.RelativePath -eq $entryAction.RelativePath)) {
                Copy-Item -LiteralPath $original -Destination $aged -Force
                $backedUp.Add([pscustomobject]@{ Original = $original; Aged = $aged })
            }
            else {
                [System.IO.File]::Move($original, $aged)
                $moved.Add([pscustomobject]@{ Original = $original; Aged = $aged })
            }
        }

        # 阶段 2：整批落位（入口 exe 排最前）
        $stage = '落位'
        $copyOrder = New-Object System.Collections.Generic.List[object]
        if ($null -ne $entryAction) { [void]$copyOrder.Add($entryAction) }
        foreach ($action in $Plan.ToReplace) {
            if ($null -ne $entryAction -and $action.RelativePath -eq $entryAction.RelativePath) { continue }
            [void]$copyOrder.Add($action)
        }

        foreach ($action in $copyOrder) {
            $target = Join-Path $InstallDirectory $action.RelativePath
            $directory = [System.IO.Path]::GetDirectoryName($target)
            if (-not (Test-Path -LiteralPath $directory)) {
                [void][System.IO.Directory]::CreateDirectory($directory)
            }

            [System.IO.File]::Copy($action.NewPath, $target, $true)
            $copied.Add($target)
        }
    }
    catch {
        $error = $_
    }

    if ($null -ne $error) {
        $summary = '整批' + $stage + '失败：' + $error.Exception.Message
        $rollbackFailures = Invoke-ZdRollback -InstallDirectory $InstallDirectory -Plan $Plan `
            -Moved $moved -BackedUp $backedUp -Copied $copied

        if ($rollbackFailures.Count -eq 0) {
            # 回滚干净了：没有东西要修，把一键修复脚本收掉（留着反而误导用户去回退）。
            Remove-ZdRepairHelper -InstallDirectory $InstallDirectory
            throw (New-ZdFailure 5 ($summary + '，已完整回滚到更新前。'))
        }

        $logPath = ''
        if (![string]::IsNullOrWhiteSpace($LogDirectory)) {
            try {
                [void][System.IO.Directory]::CreateDirectory($LogDirectory)
                $logPath = Join-Path $LogDirectory ('回滚失败-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')
                [System.IO.File]::WriteAllLines($logPath, $rollbackFailures.ToArray(), (New-Object System.Text.UTF8Encoding($true)))
            }
            catch {
                $logPath = ''
            }
        }

        $detail = '自动回滚有 ' + $rollbackFailures.Count + ' 项没完成。'
        if (![string]::IsNullOrWhiteSpace($logPath)) { $detail = $detail + '清单：' + $logPath }
        throw (New-ZdFailure 5 ($summary + '，' + $detail + [Environment]::NewLine +
                '  请双击安装目录里的「修复更新.cmd」收尾（它会把所有 ' + $Plan.OldSuffix + ' 文件改名回原样）。'))
    }

    # 收尾：**本轮**产生的 *.old-{版本} 一个都不删 —— 它们就是回滚窗口（§9.5）。
    # 「新版装上去但起不来」是最危险的形态，它必须落在窗口内：把后缀去掉就能退回更新前。
    # 什么时候清？客户端在**下一轮启动**早期清（那时"新版跑得起来"已经被真实验证过一次，§9.5 / §14）。
    # 这里只负责把路径记进待清理清单。★ 这一条**不能**改成"当场删"：当场删掉就等于没有回滚窗口，
    # 新版起不来时用户手上只剩「重新下载上一版」这一条路。
    $kept = New-Object System.Collections.Generic.List[string]
    $allActions = New-Object System.Collections.Generic.List[object]
    foreach ($action in $Plan.ToReplace) { [void]$allActions.Add($action) }
    foreach ($action in $Plan.ToDelete) { [void]$allActions.Add($action) }
    foreach ($action in $allActions) {
        $aged = (Join-Path $InstallDirectory $action.RelativePath) + $Plan.OldSuffix
        if (Test-Path -LiteralPath $aged) {
            $kept.Add($aged)
        }
    }

    if ($kept.Count -gt 0) {
        try {
            $existing = @()
            if (Test-Path -LiteralPath $PendingCleanupPath) {
                $existing = @([System.IO.File]::ReadAllLines($PendingCleanupPath, [System.Text.Encoding]::UTF8))
            }

            $all = New-Object System.Collections.Generic.List[string]
            foreach ($line in $existing) { if (-not [string]::IsNullOrWhiteSpace($line)) { $all.Add($line.Trim()) } }
            foreach ($line in $kept) { if (-not $all.Contains($line)) { $all.Add($line) } }
            [System.IO.File]::WriteAllLines($PendingCleanupPath, $all.ToArray(), (New-Object System.Text.UTF8Encoding($true)))
        }
        catch {
            Write-ZdNote ('待清理清单写不进去（不影响本次更新，只是旧文件会一直留在盘上）：' + $_.Exception.Message)
        }
    }

    return [pscustomobject]@{
        Replaced = $Plan.ToReplace.Count
        Deleted  = $Plan.ToDelete.Count
        Skipped  = $Plan.Skipped
        Kept     = $kept.Count
    }
}

function Wait-ZdMainProcessExit {
    param([int]$ProcessIdValue, [int]$TimeoutMilliseconds)

    if ($ProcessIdValue -le 0) {
        return
    }

    $process = Get-Process -Id $ProcessIdValue -ErrorAction SilentlyContinue
    if ($null -eq $process) {
        # 已经退出了。回归用例就是走这条路（传一个立刻退出的进程）。
        return
    }

    if (-not $process.WaitForExit($TimeoutMilliseconds)) {
        throw (New-ZdFailure 6 '主程序未能在 30 秒内退出，已中止更新。')
    }
}

function Test-ZdRuntimePresent {
    # 运行时按"家族 + 最低版本"判：写 2.0 就找 Microsoft.WindowsAppRuntime.2，版本 ≥ 2.0.0.0。
    # ★ 不能要求恰好 2.0.x —— 实测机器上装的是 2.2/2.3/2.4/2.5，一样能让引用 2.0.1 的应用起来。
    param([string]$Release)

    # ★ 家族名有两种形态（实测）：1.x 带次版本（Microsoft.WindowsAppRuntime.1.8），
    #   2.x 只带主版本（Microsoft.WindowsAppRuntime.2，靠版本号区分 2.2/2.3/2.5）。
    #   两个候选都试，任一个版本够了就算在位。
    $parts = $Release.Split('.')
    $families = @(
        ('Microsoft.WindowsAppRuntime.' + $Release),
        ('Microsoft.WindowsAppRuntime.' + $parts[0])
    )
    $minimum = New-Object System.Version(($Release + '.0'))
    foreach ($family in $families) {
        try {
            $packages = @(Get-AppxPackage -Name $family -ErrorAction SilentlyContinue)
        }
        catch {
            continue
        }
        foreach ($package in $packages) {
            if (([System.Version]$package.Version) -ge $minimum) {
                return $true
            }
        }
    }
    return $false
}

function Install-ZdRuntime {
    # 用官方安装器静默安装（--quiet 从安装器源码核实：-q/--quiet，且 quiet 模式不需要提权）。
    param([string]$Release, [string]$InstallerPath, [string]$WorkingDirectory)

    if ([string]::IsNullOrWhiteSpace($InstallerPath) -or !(Test-Path -LiteralPath $InstallerPath -PathType Leaf)) {
        $url = 'https://aka.ms/windowsappsdk/' + $Release + '/latest/windowsappruntimeinstall-x64.exe'
        $target = Join-Path $WorkingDirectory 'windowsappruntimeinstall-x64.exe'
        Write-ZdNote ('正在下载 Windows App Runtime 安装器：' + $url)
        try {
            $curl = Get-Command 'curl.exe' -ErrorAction Stop
            & $curl.Source '--fail' '--silent' '--show-error' '--location' '--output' $target $url
            if ($LASTEXITCODE -ne 0) { throw ('curl 退出码 ' + $LASTEXITCODE) }
        }
        catch {
            throw (New-ZdFailure 6 ('下载运行时安装器失败：' + $_.Exception.Message + '。请手动安装后再重试：' + $url))
        }
        $InstallerPath = $target
    }

    Write-ZdNote ('正在静默安装 Windows App Runtime ' + $Release + '：' + $InstallerPath)
    $process = Start-Process -FilePath $InstallerPath -ArgumentList '--quiet', '--force' -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw (New-ZdFailure 6 ('运行时安装器返回 ' + $process.ExitCode + '（0 才是成功）。'))
    }
}

function Invoke-ZdWithRetry {
    # 文件锁基本都在秒级释放：失败就 300ms 后重试，最多 5 次。
    param([scriptblock]$Action, [int]$Attempts = 5, [int]$DelayMs = 300)

    $last = $null
    for ($i = 1; $i -le $Attempts; $i++) {
        try {
            & $Action
            return $null
        }
        catch {
            $last = $_
            if ($i -lt $Attempts) { Start-Sleep -Milliseconds $DelayMs }
        }
    }
    return $last
}

function Write-ZdRepairHelper {
    # 在腾位**之前**就把"一键修复"放进安装目录：脚本被硬杀/断电时，用户双击它就能回到更新前。
    # 更新成功后调用方立刻把它删掉（所以它只在危险窗口里存在）。
    param([string]$InstallDirectory, [string]$Version)

    $scriptPath = Join-Path $InstallDirectory '修复更新.ps1'
    $cmdPath = Join-Path $InstallDirectory '修复更新.cmd'
    $marker = '.old-' + $Version

    $body = @'
param([string]$Root = $PSScriptRoot, [string]$Suffix = '__SUFFIX__')
$ErrorActionPreference = 'Continue'
$failed = 0
Get-ChildItem -LiteralPath $Root -Recurse -File -Force -ErrorAction SilentlyContinue |
    Where-Object { $_.Name.EndsWith($Suffix, [System.StringComparison]::OrdinalIgnoreCase) } |
    ForEach-Object {
        $target = $_.FullName.Substring(0, $_.FullName.Length - $Suffix.Length)
        for ($i = 1; $i -le 5; $i++) {
            try {
                if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Force -ErrorAction Stop }
                [System.IO.File]::Move($_.FullName, $target)
                Write-Host ('已恢复：' + $target) -ForegroundColor Green
                break
            }
            catch {
                if ($i -eq 5) {
                    $failed++
                    Write-Host ('恢复失败：' + $_.FullName + '（' + $_.Exception.Message + '）') -ForegroundColor Red
                }
                else { Start-Sleep -Milliseconds 300 }
            }
        }
    }
if ($failed -eq 0) { Write-Host '已全部恢复到更新前。' -ForegroundColor Green }
else { Write-Host ('有 ' + $failed + ' 个文件没恢复，请手动把后缀 ' + $Suffix + ' 去掉。') -ForegroundColor Yellow }
'@
    $body = $body.Replace('__SUFFIX__', $marker)
    [System.IO.File]::WriteAllText($scriptPath, $body, (New-Object System.Text.UTF8Encoding($true)))

    $cmd = '@echo off' + [Environment]::NewLine +
        'powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0修复更新.ps1"' + [Environment]::NewLine +
        'pause' + [Environment]::NewLine
    [System.IO.File]::WriteAllText($cmdPath, $cmd, [System.Text.Encoding]::ASCII)
    return @($scriptPath, $cmdPath)
}

function Remove-ZdRepairHelper {
    param([string]$InstallDirectory)
    foreach ($name in @('修复更新.ps1', '修复更新.cmd')) {
        $path = Join-Path $InstallDirectory $name
        try { if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force } } catch { }
    }
}

function Invoke-ZdRollback {
    # 回滚必须跑完：每个文件都重试，失败的**记下来**（不中断），最后统一报给调用方。
    param([string]$InstallDirectory, $Plan, $Moved, $BackedUp, $Copied)

    $failed = New-Object System.Collections.Generic.List[string]

    foreach ($target in $Copied) {
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { continue }
        $problem = Invoke-ZdWithRetry -Action { Remove-Item -LiteralPath $target -Force }
        if ($null -ne $problem) {
            $failed.Add('删不掉本轮落位的新文件：' + $target + '（' + $problem.Exception.Message + '）')
        }
    }

    for ($i = $Moved.Count - 1; $i -ge 0; $i--) {
        $record = $Moved[$i]
        if (-not (Test-Path -LiteralPath $record.Aged -PathType Leaf)) { continue }
        if (Test-Path -LiteralPath $record.Original -PathType Leaf) {
            $problem = Invoke-ZdWithRetry -Action { Remove-Item -LiteralPath $record.Original -Force }
            if ($null -ne $problem) {
                $failed.Add('原名位置清不掉：' + $record.Original + '（' + $problem.Exception.Message + '）')
                continue
            }
        }
        $problem = Invoke-ZdWithRetry -Action { [System.IO.File]::Move($record.Aged, $record.Original) }
        if ($null -ne $problem) {
            $failed.Add('改不回原名：' + $record.Aged + ' → ' + $record.Original + '（' + $problem.Exception.Message + '）')
        }
    }

    foreach ($record in $BackedUp) {
        if (-not (Test-Path -LiteralPath $record.Aged -PathType Leaf)) { continue }
        $problem = Invoke-ZdWithRetry -Action { [System.IO.File]::Copy($record.Aged, $record.Original, $true) }
        if ($null -ne $problem) {
            $failed.Add('入口 exe 的备份拷不回原位：' + $record.Aged + ' → ' + $record.Original + '（' + $problem.Exception.Message + '）')
            continue
        }
        # 拷回来了，备份本身要删掉：它是"拷贝"出来的（腾位阶段没改名），留着就是一份 .old- 残留。
        # 删不掉不算回滚失败（内容已经恢复）——留一句话，下一轮启动的清理会把它收走。
        $problem = Invoke-ZdWithRetry -Action { Remove-Item -LiteralPath $record.Aged -Force }
        if ($null -ne $problem) {
            Write-ZdNote ('入口 exe 的备份删不掉（内容已恢复，可手工删）：' + $record.Aged)
        }
    }

    # ★ 这个**前导逗号是必需的**，不是手滑：
    # PowerShell 从函数返回集合时会把枚举**展开** —— 空列表展开成"什么都没有"，
    # 调用方拿到 $null，于是 `$rollbackFailures.Count` 在本脚本的
    # `Set-StrictMode -Version 2.0` 下直接抛「在此对象上找不到属性 Count」。
    #
    # 实测后果（2026-10-08）：「腾位失败」「落位失败」这两条回滚用例全部变成退出码 6
    # （兜底码）而不是 5，因为抛的是这个属性错、不是 New-ZdFailure 5。
    # 也就是说**回滚成功时报的却是"更新失败且原因不明"** —— 最不该出错的那条路上出错。
    # `,$x` 用数组包一层，阻止展开，返回值就永远是那个 List。
    return ,$failed
}

# ─────────────────────────────────────────────────────────────────────────────
# 主流程（§9.2）
# ─────────────────────────────────────────────────────────────────────────────
$installRoot = [System.IO.Path]::GetFullPath($InstallDir).TrimEnd('\')
$packageFull = [System.IO.Path]::GetFullPath($PackagePath)
if ([string]::IsNullOrWhiteSpace($WorkingRoot)) {
    $WorkingRoot = Join-Path $env:LOCALAPPDATA 'GalExcleTools'
}

$stagingRoot = Join-Path $WorkingRoot 'UpdateStaging'
$logDirectory = Join-Path $WorkingRoot 'UpdateLogs'
$pendingCleanup = Join-Path $WorkingRoot 'PendingCleanup.txt'

try {
    # ── 8% 参数预检 ──────────────────────────────────────────────────────────
    Write-ZdStep -Percent 8 -Text '正在检查更新参数...'
    Write-ZdNote ('安装目录：' + $installRoot)
    Write-ZdNote ('更新包：' + $packageFull)
    Write-ZdNote ('目标版本：v' + $TargetVersion + '（' + $ToolboxStableKey + '）')
    if ($KeepBackup) {
        Write-ZdNote '注意：本工程不做整目录备份（设计 §9.5），-KeepBackup 只是个兼容参数。'
    }

    if (-not (Test-Path -LiteralPath $installRoot -PathType Container)) {
        throw (New-ZdFailure 1 ('安装目录不存在：' + $installRoot))
    }

    if (-not (Test-Path -LiteralPath $packageFull -PathType Leaf)) {
        throw (New-ZdFailure 1 ('更新包不存在：' + $packageFull))
    }

    $signalDirectory = [System.IO.Path]::GetDirectoryName([System.IO.Path]::GetFullPath($ReadySignalPath))
    if (-not (Test-Path -LiteralPath $signalDirectory -PathType Container)) {
        [void][System.IO.Directory]::CreateDirectory($signalDirectory)
    }

    if (-not (Test-Path -LiteralPath $logDirectory -PathType Container)) {
        [void][System.IO.Directory]::CreateDirectory($logDirectory)
    }

    try {
        Start-Transcript -Path (Join-Path $logDirectory ('Update-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '.log')) -Force | Out-Null
        $script:TranscriptStarted = $true
    }
    catch {
        Write-ZdNote ('日志转录起不来（不影响更新，但这次失败就没有日志可查）：' + $_.Exception.Message)
    }

    try {
        $signalProbe = Join-Path $signalDirectory ('zd-write-probe-' + [Guid]::NewGuid().ToString('N') + '.tmp')
        [System.IO.File]::WriteAllText($signalProbe, 'probe', [System.Text.Encoding]::UTF8)
        Remove-Item -LiteralPath $signalProbe -Force
    }
    catch {
        throw (New-ZdFailure 1 ('信号文件目录不可写：' + $signalDirectory))
    }

    $packageSize = (Get-Item -LiteralPath $packageFull).Length
    $freeSpace = Get-ZdFreeSpaceBytes -Path $installRoot
    $required = [long]$packageSize * 3
    if ($freeSpace -lt $required) {
        throw (New-ZdFailure 1 ('磁盘空间不足：需要约 ' + [Math]::Round($required / 1MB, 1) + ' MB，可用 ' + [Math]::Round($freeSpace / 1MB, 1) + ' MB。'))
    }

    # ── 18% SHA-256 复核 ─────────────────────────────────────────────────────
    Write-ZdStep -Percent 18 -Text '正在复核更新包 SHA-256...'
    $actualSha = Get-ZdFileSha256 -Path $packageFull
    $expectedSha = ($ExpectedSha256 -replace '\s', '').ToLowerInvariant()
    if ($actualSha -ne $expectedSha) {
        throw (New-ZdFailure 3 ('SHA-256 mismatch: 实际 ' + $actualSha + ' / 期望 ' + $expectedSha))
    }

    Write-ZdNote ('SHA-256 一致：' + $actualSha.Substring(0, 16) + '...')

    # ── 34% 解压到 staging ───────────────────────────────────────────────────
    Write-ZdStep -Percent 34 -Text '正在解压更新包...'
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $zip = [System.IO.Compression.ZipFile]::OpenRead($packageFull)
    try {
        foreach ($entry in $zip.Entries) {
            $name = $entry.FullName.Replace('/', '\')
            $illegal = ($name -match '(^|\\)\.\.(\\|$)') -or ($name -match '^[A-Za-z]:') -or $name.StartsWith('\')
            if ($illegal) {
                throw (New-ZdFailure 4 ('更新包内含有非法路径条目：' + $entry.FullName))
            }
        }
    }
    finally {
        $zip.Dispose()
    }

    if (Test-Path -LiteralPath $stagingRoot) {
        Remove-Item -LiteralPath $stagingRoot -Recurse -Force
    }

    [void][System.IO.Directory]::CreateDirectory($stagingRoot)
    [System.IO.Compression.ZipFile]::ExtractToDirectory($packageFull, $stagingRoot)

    $packageRoot = Resolve-ZdPackageRoot -StagingRoot $stagingRoot -Key $ToolboxStableKey -Version $TargetVersion
    Write-ZdNote ('包清单：' + $packageRoot.ManifestPath)
    Write-ZdNote ('主程序：' + $packageRoot.EntryExeName)

    # 入口 exe 在安装目录里的相对路径（包根可能在 zip 里多一层目录，按 Substring 映射回安装目录）。
    # ⚠️ PS 5.1 跑在 .NET Framework 上，**没有** [IO.Path]::GetRelativePath（那是 .NET Core 才有的）。
    $entryExeRelative = ''
    if (![string]::IsNullOrWhiteSpace($packageRoot.EntryExePath)) {
        $entryExeRelative = $packageRoot.EntryExePath.Substring($stagingRoot.Length).TrimStart('\')
    }

    # 增量包的两道前置闸门（版本对齐 + 旧内容指纹）在这里就过掉：
    # 过不了就中止，主程序甚至还没退出，一个文件都不会动。
    if ($packageRoot.IsDelta) {
        Write-ZdNote ('这是增量包：基准 v' + $packageRoot.BaseVersion + '，变化 ' + $packageRoot.DeltaFiles.Count +
            ' 个，待删除 ' + $packageRoot.DeltaDelete.Count + ' 个。')
    }
    Assert-ZdDeltaPrecondition -CurrentVersion $CurrentVersion -PackageRoot $packageRoot -InstallDirectory $installRoot

    $whitelist = Read-ZdWhitelist -ScriptRoot $PSScriptRoot -InstallDirectory $installRoot
    Write-ZdNote ('白名单：' + $whitelist.Path)

    # ── 运行前置（2026-10-06）：框架依赖的包要求目标机器先有 Windows App Runtime ──
    # 没有就**中止更新**：绝不把一台没有运行时的机器升到框架依赖版本（新版会起不来）。
    # 放在 READY 之前 —— 主程序还没退出，用户看到的只是"这次更新没做"。
    if (!$SkipRuntimeCheck -and ![string]::IsNullOrWhiteSpace($packageRoot.WasdkRuntime)) {
        if (Test-ZdRuntimePresent -Release $packageRoot.WasdkRuntime) {
            Write-ZdNote ('Windows App Runtime ' + $packageRoot.WasdkRuntime + ' 已在位。')
        }
        else {
            Write-ZdNote ('这台机器没有 Windows App Runtime ' + $packageRoot.WasdkRuntime + '，新版本起不来。')
            if ([string]::IsNullOrWhiteSpace($RuntimeInstallerPath)) {
                throw (New-ZdFailure 6 ('缺少 Windows App Runtime ' + $packageRoot.WasdkRuntime +
                        '，已中止更新（没有覆盖任何文件）。' + [Environment]::NewLine +
                        '  先装运行时再重试：https://aka.ms/windowsappsdk/' + $packageRoot.WasdkRuntime +
                        '/latest/windowsappruntimeinstall-x64.exe' + [Environment]::NewLine +
                        '  （也可以给本脚本传 -RuntimeInstallerPath 指向已下载的安装器，它会静默安装。）'))
            }
            [void][System.IO.Directory]::CreateDirectory($WorkingRoot)
            Install-ZdRuntime -Release $packageRoot.WasdkRuntime -InstallerPath $RuntimeInstallerPath -WorkingDirectory $WorkingRoot
            if (!(Test-ZdRuntimePresent -Release $packageRoot.WasdkRuntime)) {
                throw (New-ZdFailure 6 ('运行时安装器跑完了，但仍然检测不到 Windows App Runtime ' +
                        $packageRoot.WasdkRuntime + '，已中止更新。'))
            }
            Write-ZdNote '运行时已就位。'
        }
    }

    # ── ★ 写 READY：预检全过，才轮到主程序退出 ───────────────────────────────
    $readyText = 'READY' + [Environment]::NewLine +
        'version=' + $TargetVersion + [Environment]::NewLine +
        'updaterPid=' + $PID + [Environment]::NewLine +
        'at=' + (Get-Date -Format 'O') + [Environment]::NewLine
    [System.IO.File]::WriteAllText($ReadySignalPath, $readyText, (New-Object System.Text.UTF8Encoding($false)))
    Write-ZdNote ('已写出 READY 信号：' + $ReadySignalPath)

    # ── 55% 等主程序退出 ─────────────────────────────────────────────────────
    Write-ZdStep -Percent 55 -Text '正在等待主程序退出...'
    Wait-ZdMainProcessExit -ProcessIdValue $AppProcessId -TimeoutMilliseconds 30000

    # ── 60%~86% 两阶段整批替换 ───────────────────────────────────────────────
    Write-ZdStep -Percent 60 -Text '正在计算覆盖动作...'
    $plan = Get-ZdApplyPlan -InstallDirectory $installRoot -StagingDirectory $stagingRoot -Whitelist $whitelist `
        -Version $TargetVersion -DeltaMode:$packageRoot.IsDelta -DeltaDelete @($packageRoot.DeltaDelete)
    Write-ZdNote ('要替换 ' + $plan.ToReplace.Count + ' 个文件，要删除 ' + $plan.ToDelete.Count + ' 个（白名单外不动 ' + $plan.Skipped + ' 个）')

    Write-ZdStep -Percent 72 -Text '正在整批腾位并落位（两阶段替换）...'
    # 腾位之前先把"一键修复"放进安装目录：脚本被硬杀/断电时，用户双击它就能回到更新前。
    # 更新成功后立刻删掉（所以它只在危险窗口里存在）。
    [void](Write-ZdRepairHelper -InstallDirectory $installRoot -Version $TargetVersion)
    $result = Invoke-ZdOverlay -InstallDirectory $installRoot -Plan $plan -PendingCleanupPath $pendingCleanup `
        -EntryExeRelative $entryExeRelative -LogDirectory $logDirectory
    Remove-ZdRepairHelper -InstallDirectory $installRoot

    # ── 86% 清理 ─────────────────────────────────────────────────────────────
    Write-ZdStep -Percent 86 -Text '正在清理暂存目录...'
    try {
        if (Test-Path -LiteralPath $stagingRoot) {
            Remove-Item -LiteralPath $stagingRoot -Recurse -Force
        }
    }
    catch {
        Write-ZdNote ('暂存目录没删干净（不影响使用）：' + $_.Exception.Message)
    }

    # ── 100% 收尾 ────────────────────────────────────────────────────────────
    Write-ZdStep -Percent 100 -Text '更新完成。'
    # 快捷方式：本工程是绿色版 + 原地替换，exe 路径更新前后完全一样（§9.6 理由 ④），
    # 所以这里**没有**快捷方式要重建 —— 源实现那一步在本工程是空操作，写出来免得以后被误以为漏了。
    Write-ZdNote '程序路径没有变化，快捷方式无需重建。'
    Write-ZdNote ('替换 ' + $result.Replaced + ' 个文件，删除 ' + $result.Deleted + ' 个，跳过 ' + $result.Skipped + ' 个。')
    if ($result.Kept -gt 0) {
        Write-ZdNote ('更新前的 ' + $result.Kept + ' 个文件以 *.old-' + $TargetVersion + ' 留在原处（回滚用），下次启动时会自动清理。')
    }

    Write-Host ''
    Write-Host ('热更新已完成：' + $ToolboxStableKey + ' 现在是 v' + $TargetVersion + '。') -ForegroundColor Green
    Write-Host ('如果新版本起不来，把程序目录里的 *.old-' + $TargetVersion + ' 文件改名回原名（去掉 .old-' + $TargetVersion + ' 后缀）即可回到更新前。') -ForegroundColor Yellow

    if (-not $NoStart) {
        # ★ 起的是**安装目录里**那份新主程序：暂存目录在 86% 已经被删掉了，直接拿
        #   $packageRoot.EntryExePath（指向 staging）去 Start-Process 必然报「找不到文件」，
        #   结果把一次成功的覆盖变成退出码 6 + 一句「更新失败」（M4 真机演练实测踩到）。
        #   主程序在包里的位置相对包根算（包根可能在 zip 里多一层目录），映射回安装目录即可。
        #   ⚠️ 这里只能用 Substring：PS 5.1 跑在 .NET Framework 上，**没有** [IO.Path]::GetRelativePath
        #   （那是 .NET Core 才有的）——用了就是收尾时抛 MethodNotFound，一次成功的覆盖变成退出码 6。
        $relativeDir = [System.IO.Path]::GetDirectoryName(
            $packageRoot.ManifestPath.Substring($stagingRoot.Length).TrimStart('\'))
        $launchExe = if ([string]::IsNullOrEmpty($relativeDir)) {
            Join-Path $installRoot $packageRoot.EntryExeName
        }
        else {
            Join-Path (Join-Path $installRoot $relativeDir) $packageRoot.EntryExeName
        }
        # ★ 覆盖已经落好了：从这里往后出任何岔子都**不能**反过来说「更新失败」（用户会以为要重装），
        #   只提醒他自己打开 —— 实测 Start-Process 对「存在但不是可执行文件」的路径一样会抛。
        if (-not (Test-Path -LiteralPath $launchExe -PathType Leaf)) {
            Write-ZdNote ('覆盖已完成，但没找到新版本主程序，请手动打开：' + $launchExe)
        }
        else {
            try {
                Start-Process -FilePath $launchExe -WorkingDirectory $installRoot
            }
            catch {
                Write-ZdNote ('覆盖已完成，但起新版本失败（请手动打开）：' + $launchExe + ' —— ' + $_.Exception.Message)
            }
        }
    }

    $script:ExitCode = 0
}
catch {
    $code = 6
    if ($null -ne $_.Exception.Data['ZdExitCode']) {
        $code = [int]$_.Exception.Data['ZdExitCode']
    }

    Write-Host ''
    Write-Host ('更新失败：' + $_.Exception.Message) -ForegroundColor Red
    # 把出错位置也说出来（严格模式/属性缺失这类错，光看消息根本定位不到哪一行）。
    try {
        $line = $_.InvocationInfo.ScriptLineNumber
        $text = ('' + $_.InvocationInfo.Line).Trim()
        Write-Host ('出错位置：第 ' + $line + ' 行  ' + $text) -ForegroundColor Red
    }
    catch { }
    Write-Host '请保留此窗口内容，或手动去 Gitee 下载 Release 包覆盖程序目录。' -ForegroundColor Red
    Write-Host ('回滚指引：程序目录里带 .old-' + $TargetVersion + ' 后缀的文件就是更新前的原件，把后缀去掉即可恢复。') -ForegroundColor Yellow
    $script:ExitCode = $code
}
finally {
    Write-Progress -Activity '热更新覆盖' -Completed
    if ($script:TranscriptStarted) {
        try { Stop-Transcript | Out-Null } catch { }
    }

    if (-not $NoPause) {
        Write-Host '按回车键关闭此窗口...'
        [void][System.Console]::ReadLine()
    }
}

exit $script:ExitCode
