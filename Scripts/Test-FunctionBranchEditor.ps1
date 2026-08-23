$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$models = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'Models\AssetLibraryModels.cs')
$dialog = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'Views\EditorDialogContentFactory.cs')
$loading = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $root 'MainWindow.AssetLibrary.Loading.cs')

if ($models -notmatch 'FunctionEditorInput[\s\S]*?MainCode[\s\S]*?BranchMode[\s\S]*?List<FunctionBranch>') {
    throw 'Function editor input must carry main code and branch records.'
}
if ($dialog -notmatch 'Header = "主函数代号"[\s\S]*?实际代号[\s\S]*?备注[\s\S]*?FunctionBranch') {
    throw 'Function editor must expose branch code and remark rows.'
}
if ($loading -notmatch 'input\.MainCode[\s\S]*?input\.BranchMode[\s\S]*?input\.Branches') {
    throw 'Function create/edit must persist normalized branch input.'
}
if ($dialog -match '选项备注') {
    throw 'Legacy choice-note editing must not remain in the function catalog editor.'
}

Write-Host 'Function branch-editor checks passed.'
