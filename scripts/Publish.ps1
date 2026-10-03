$ErrorActionPreference = 'Stop'
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    $release = Join-Path (Get-Location) 'artifacts/publish/win-x64'
    & dotnet publish src/MergeDesk.App/MergeDesk.App.csproj -c Release -r win-x64 --self-contained true -o $release
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    foreach ($file in @('README.md','USER-GUIDE.md','START-HERE.txt')) { Copy-Item -LiteralPath $file -Destination $release -Force }
    New-Item -ItemType Directory (Join-Path $release 'samples') -Force | Out-Null
    Get-ChildItem -LiteralPath samples | Copy-Item -Destination (Join-Path $release 'samples') -Recurse -Force
    Compress-Archive -Path $release -DestinationPath artifacts/MergeDesk-Windows-x64.zip -Force
    Write-Output 'Portable ZIP: artifacts/MergeDesk-Windows-x64.zip'
} finally { Pop-Location }
