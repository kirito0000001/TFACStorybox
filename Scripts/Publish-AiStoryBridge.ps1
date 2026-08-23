param(
    [string]$Runtime = 'win-x64',
    [string]$OutputDirectory = 'D:\TFAC-AI剧情外接程序'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'Tools\TFAC.AiStoryBridge\TFAC.AiStoryBridge.csproj'
$output = [IO.Path]::GetFullPath($OutputDirectory)
[void](New-Item -ItemType Directory -Force -Path $output)

dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --output $output `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PublishTrimmed=false `
    -p:DebugSymbols=false `
    -p:DebugType=None
if ($LASTEXITCODE -ne 0) {
    throw "AI story bridge publish failed with exit code $LASTEXITCODE"
}

$exePath = Join-Path $output 'TFAC-AI剧情外接程序.exe'
if (!(Test-Path -LiteralPath $exePath)) {
    throw "Published executable is missing: $exePath"
}

& $exePath --help
if ($LASTEXITCODE -ne 0) {
    throw "Published executable smoke test failed with exit code $LASTEXITCODE"
}

Write-Host "AI story bridge published: $exePath"
