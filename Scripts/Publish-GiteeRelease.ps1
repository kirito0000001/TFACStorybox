param(
    [string]$ReleaseAssetRoot = "",

    [string]$TargetCommitish = "main"
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ReleaseAssetRoot)) {
    $ReleaseAssetRoot = Join-Path $repoRoot 'ReleaseAssets'
}

$token = [Environment]::GetEnvironmentVariable('GITEE_TOKEN')
if ([string]::IsNullOrWhiteSpace($token)) {
    throw '未设置 GITEE_TOKEN 环境变量。Token 不应写入脚本、JSON 或日志。'
}

$manifestPath = Join-Path $ReleaseAssetRoot 'toolbox-update.json'
if (!(Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "缺少更新清单：$manifestPath"
}

$manifest = Get-Content -Raw -Encoding UTF8 -LiteralPath $manifestPath | ConvertFrom-Json
if ([int]$manifest.schemaVersion -ne 1 -or [string]$manifest.toolboxStableKey -cne 'GalExcleTools') {
    throw 'toolbox-update.json 不是剧情工具箱更新清单。'
}

$version = [string]$manifest.version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw 'toolbox-update.json 缺少版本号。'
}

$assetPaths = @($manifestPath)
foreach ($asset in @($manifest.assets)) {
    $archivePath = Join-Path $ReleaseAssetRoot ([string]$asset.fileName)
    $hashPath = "$archivePath.sha256.txt"
    foreach ($path in @($archivePath, $hashPath)) {
        if (!(Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "缺少待发布附件：$path"
        }
        $assetPaths += $path
    }
}

$owner = 'xiaojie578'
$repo = 'TFACStorybox'
$apiRoot = "https://gitee.com/api/v5/repos/$owner/$repo/releases"
$tag = "v$version"
$headers = @{ Accept = 'application/json' }
$releases = Invoke-RestMethod -Method Get -Uri "$apiRoot?access_token=$([Uri]::EscapeDataString($token))&per_page=100" -Headers $headers
$release = @($releases) | Where-Object { [string]$_.tag_name -eq $tag } | Select-Object -First 1
$isPrerelease = [string]$manifest.channel -eq 'beta'
$releaseBody = [string]$manifest.releaseNotes

if ($null -eq $release) {
    $release = Invoke-RestMethod -Method Post -Uri $apiRoot -Headers $headers -Body @{
        access_token = $token
        tag_name = $tag
        name = "TFAC剧情箱-轮椅版 V$version"
        body = $releaseBody
        prerelease = $isPrerelease.ToString().ToLowerInvariant()
        target_commitish = $TargetCommitish
    }
}
else {
    $release = Invoke-RestMethod -Method Patch -Uri "$apiRoot/$($release.id)" -Headers $headers -Body @{
        access_token = $token
        name = "TFAC剧情箱-轮椅版 V$version"
        body = $releaseBody
        prerelease = $isPrerelease.ToString().ToLowerInvariant()
    }
}

$attachFilesUrl = "$apiRoot/$($release.id)/attach_files"
$existingAssets = Invoke-RestMethod -Method Get -Uri "$attachFilesUrl?access_token=$([Uri]::EscapeDataString($token))&per_page=100" -Headers $headers
foreach ($assetPath in $assetPaths) {
    $file = Get-Item -LiteralPath $assetPath
    $existing = @($existingAssets) | Where-Object { [string]$_.name -eq $file.Name } | Select-Object -First 1
    if ($null -ne $existing -and [long]$existing.size -eq $file.Length) {
        Write-Host "附件已存在且大小一致，跳过：$($file.Name)"
        continue
    }

    if ($null -ne $existing) {
        Invoke-RestMethod -Method Delete -Uri "$attachFilesUrl/$($existing.id)" -Headers $headers -Body @{ access_token = $token } | Out-Null
    }

    Write-Host "正在上传：$($file.Name)"
    Invoke-RestMethod -Method Post -Uri $attachFilesUrl -Headers $headers -Form @{
        access_token = $token
        file = $file
    } | Out-Null
}

Write-Host "Gitee Release 发布完成：$tag"
Write-Host "https://gitee.com/$owner/$repo/releases/tag/$tag"
