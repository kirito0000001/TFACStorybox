# 完整发布编排：凭据预检 → Gitee（0-50%）→ GitHub（50-100%），同进程顺序调用。

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$Notes = '',
    [string]$StagingDirectory = '',
    [string]$ProjectRoot = '',
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',
    [switch]$DryRun,
    [switch]$ForceRebuild,
    [switch]$IncrementalBuild,
    [int64]$PartSizeBytes = 0,
    [switch]$ExcludeAiRuntime,
    [switch]$AllowStubUpdater,

    [switch]$SkipGitee,
    [switch]$SkipGithub,
    [string]$GiteeRepository = 'xiaojie578/TFACStorybox',
    [string]$GiteeBranch = 'main',
    [string]$GiteeAccessToken = '',
    [string]$GithubRepository = 'kirito0000001/TFACStorybox',
    [string]$GithubBranch = 'main',
    [string]$GithubAccessToken = '',
    [int]$KeepReleasesWithAssets = 3,
    [switch]$SkipPrune,
    [switch]$KeepOldReleaseRecords,
    [switch]$SkipSelfCheck,
    [switch]$TolerateSelfCheckFailure
 )
# ★ `[CmdletBinding()]` 的脚本 / 函数，参数绑定发生在 `$PSScriptRoot` 赋值**之前**，
#    所以 param 默认值里读 `$PSScriptRoot` 只会拿到空串（Join-Path 直接抛错）。
#    改成在函数体里兜底解析：行为与「默认值就是工程根」一致，
#    且在 -File / 点源 / 函数调用三种路径下都成立。
if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    $ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}
. (Join-Path $PSScriptRoot 'GalExcelPublish.ps1')
function Resolve-ZdGithubToken {
    # 顺序：显式参数 > 环境变量（Process→User→Machine）> `gh auth token`。
    param([string]$Explicit)
    if (![string]::IsNullOrWhiteSpace($Explicit)) { return $Explicit.Trim() }
    foreach ($name in @('GALEXCEL_GITHUB_TOKEN', 'GITHUB_TOKEN', 'GH_TOKEN')) {
        foreach ($scope in @('Process', 'User', 'Machine')) {
            $value = [Environment]::GetEnvironmentVariable($name, $scope)
            if (![string]::IsNullOrWhiteSpace($value)) { return $value.Trim() }
        }
    }
    $gh = Get-Command 'gh.exe' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -ne $gh) {
        $token = (& $gh.Source auth token 2>$null)
        if (![string]::IsNullOrWhiteSpace($token)) { return $token.Trim() }
    }
    return ''
}
function Get-ZdGiteeTokenSource {
    foreach ($name in @('GALEXCEL_GITEE_TOKEN', 'GITEE_TOKEN', 'GITEE_ACCESS_TOKEN')) {
        foreach ($scope in @('Process', 'User', 'Machine')) {
            $value = [Environment]::GetEnvironmentVariable($name, $scope)
            if (![string]::IsNullOrWhiteSpace($value)) { return $name }
        }
    }
    return ''
}
$exitCode = 0
try {
    if ($SkipGitee -and $SkipGithub) { throw '两条通道都被 -SkipGitee / -SkipGithub 跳过了，没有可做的事。' }
    $root = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd([char[]]'\/')
    $targetVersion = $Version.TrimStart('v')
    $staging = if ([string]::IsNullOrWhiteSpace($StagingDirectory)) {
        Join-Path $root (Join-Path 'artifacts\release' "v$targetVersion")
    }
    else { [IO.Path]::GetFullPath($StagingDirectory).TrimEnd([char[]]'\/') }
    Write-Host "== 完整发布 TFAC剧情箱-轮椅版 v$targetVersion ==" -ForegroundColor Cyan
    Write-Host "   工程根  ：$root"
    Write-Host "   产物目录：$staging"
    Write-Host "   通道    ：$(if ($SkipGitee) { 'GitHub' } elseif ($SkipGithub) { 'Gitee' } else { 'Gitee + GitHub' })"
    $githubToken = ''
    if (!$DryRun) {
        if (!$SkipGitee) {
            $source = Get-ZdGiteeTokenSource
            $hasExplicit = ![string]::IsNullOrWhiteSpace($GiteeAccessToken)
            if ([string]::IsNullOrWhiteSpace($source) -and !$hasExplicit) {
                throw ('缺少 Gitee 令牌：请设置 GALEXCEL_GITEE_TOKEN（或 GITEE_TOKEN /' +
                    ' GITEE_ACCESS_TOKEN），或传 -GiteeAccessToken；也可以加 -SkipGitee 只发 GitHub。')
            }
            Write-Host "Gitee 令牌 ：$(if ($hasExplicit) { '来自 -GiteeAccessToken' } else { "来自环境变量 $source" })"
        }
        if (!$SkipGithub) {
            $githubToken = Resolve-ZdGithubToken -Explicit $GithubAccessToken
            if ([string]::IsNullOrWhiteSpace($githubToken)) {
                throw ('缺少 GitHub 令牌：请设置 GALEXCEL_GITHUB_TOKEN（或 GITHUB_TOKEN / GH_TOKEN）、' +
                    '先执行 `gh auth login`，或传 -GithubAccessToken；也可以加 -SkipGithub 只发 Gitee。')
            }
            Write-Host 'GitHub 令牌：已就绪'
        }
    }
    if (!$SkipGitee) {
        Set-ZdProgressRange -From 0 -To $(if ($SkipGithub) { 100 } else { 50 })
        Write-Host ''
        Write-Host '===== Gitee 通道 =====' -ForegroundColor Cyan
        $code = Invoke-ZdGiteePublish -Version $targetVersion -ProjectRoot $root -StagingDirectory $staging `
            -Runtime $Runtime -Notes $Notes -DryRun:$DryRun `
            -GiteeRepository $GiteeRepository -GiteeBranch $GiteeBranch -GiteeAccessToken $GiteeAccessToken `
            -KeepReleasesWithAssets $KeepReleasesWithAssets -SkipPrune:$SkipPrune `
            -KeepOldReleaseRecords:$KeepOldReleaseRecords -ForceRebuild:$ForceRebuild `
            -IncrementalBuild:$IncrementalBuild -PartSizeBytes $PartSizeBytes `
            -ExcludeAiRuntime:$ExcludeAiRuntime -AllowStubUpdater:$AllowStubUpdater `
            -SkipSelfCheck:$SkipSelfCheck -TolerateSelfCheckFailure:$TolerateSelfCheckFailure
        if ($code -ne 0) { throw "Gitee 通道失败（退出码 $code）。已传成功的附件不会重传，修好上面那个原因后原样重跑即可。" }
    }
    if (!$SkipGithub) {
        Set-ZdProgressRange -From $(if ($SkipGitee) { 0 } else { 50 }) -To 100
        Write-Host ''
        Write-Host '===== GitHub 通道 =====' -ForegroundColor Cyan
        $code = Invoke-ZdGithubPublish -Version $targetVersion -ProjectRoot $root -StagingDirectory $staging `
            -Runtime $Runtime -DryRun:$DryRun -GithubRepository $GithubRepository -GithubBranch $GithubBranch `
            -GithubAccessToken $githubToken -SkipSelfCheck:$SkipSelfCheck `
            -TolerateSelfCheckFailure:$TolerateSelfCheckFailure
        if ($code -ne 0) { throw "GitHub 通道失败（退出码 $code）。已传成功的附件不会重传，修好上面那个原因后原样重跑即可。" }
    }
    Set-ZdProgressRange -From 0 -To 100
    Write-ZdProgress -Stage 'completed' -Percent 100 -Message "完整发布完成：v$targetVersion"
    Write-Host ''
    Write-Host "完整发布完成：v$targetVersion" -ForegroundColor Green
    if (!$SkipGitee) {
        Write-Host ("  Gitee ：https://gitee.com/$GiteeRepository/releases/tag/v$targetVersion" +
            "（附件只保留最近 $KeepReleasesWithAssets 个 Release）")
    }
    if (!$SkipGithub) {
        Write-Host "  GitHub：https://github.com/$GithubRepository/releases/tag/v$targetVersion（旧版本永久保留）"
    }
}
catch {
    $exitCode = 1
    $message = [string]$_.Exception.Message
    $redactions = [Collections.Generic.List[string]]::new()
    $redactions.Add($GiteeAccessToken)
    $redactions.Add($GithubAccessToken)
    if (![string]::IsNullOrWhiteSpace($githubToken)) { $redactions.Add($githubToken) }
    foreach ($name in @('GALEXCEL_GITEE_TOKEN', 'GITEE_TOKEN', 'GITEE_ACCESS_TOKEN',
            'GALEXCEL_GITHUB_TOKEN', 'GITHUB_TOKEN', 'GH_TOKEN')) {
        foreach ($scope in @('Process', 'User', 'Machine')) {
            $value = [Environment]::GetEnvironmentVariable($name, $scope)
            if (![string]::IsNullOrWhiteSpace($value)) { $redactions.Add($value) }
        }
    }
    foreach ($secret in $redactions) {
        if ($secret.Length -ge 8) { $message = $message.Replace($secret, '<token>') }
    }
    $payload = [ordered]@{ type = 'progress'; stage = 'failed'; percent = 100; message = ('完整发布失败：' + $message) }
    [Console]::Out.WriteLine('::axtools ' + ($payload | ConvertTo-Json -Compress))
    [Console]::Error.WriteLine('完整发布失败：' + $message)
    [Console]::Error.WriteLine($_.ScriptStackTrace)
}
exit $exitCode
