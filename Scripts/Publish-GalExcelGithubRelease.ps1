# GitHub 单通道入口（实现见 GalExcelPublish.ps1）。

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [string]$Notes = '',
    [string]$StagingDirectory = '',
    [string]$ProjectRoot = '',
    [string]$GithubRepository = 'kirito0000001/TFACStorybox',
    [string]$GithubBranch = 'main',
    [string]$GithubAccessToken = '',
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64',
    [switch]$DryRun,
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
exit (Invoke-ZdGithubPublish @PSBoundParameters)
