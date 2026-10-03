param([string]$CompilerPath, [string]$ReleasePath, [string]$Version = '1.0.0')
$ErrorActionPreference = 'Stop'
if (!$CompilerPath) {
    $candidates = @((Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'), (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'), (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'))
    $CompilerPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if (!$CompilerPath -or !(Test-Path -LiteralPath $CompilerPath -PathType Leaf)) { throw 'Install Inno Setup or supply -CompilerPath pointing to ISCC.exe.' }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must have three numeric parts, such as 1.0.0.' }
if (!$ReleasePath) { $ReleasePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts\publish\win-x64' }
$releaseRoot = (Resolve-Path -LiteralPath $ReleasePath).Path
foreach ($required in @('MergeDesk.App.exe','MergeDesk.App.dll','LICENSE.txt','THIRD-PARTY-NOTICES.md','coreclr.dll','e_sqlite3.dll','HelpAssets\MergeDesk-Walkthrough.mp4')) {
    if (!(Test-Path -LiteralPath (Join-Path $releaseRoot $required) -PathType Leaf)) { throw "Incomplete published release: missing $required" }
}
& $CompilerPath '/Q' "/DReleasePath=$releaseRoot" "/DAppVersion=$Version" (Join-Path $PSScriptRoot 'MergeDesk.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed (exit $LASTEXITCODE)." }
Write-Output "Built: $(Join-Path $PSScriptRoot "MergeDesk-Setup-$Version-x64.exe")"
Write-Output 'The installer has not been run.'
