$ErrorActionPreference = 'Stop'
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    & dotnet restore MergeDesk.sln
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    & dotnet build MergeDesk.sln -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    foreach ($suite in @('MergeDesk.Tests','MergeDesk.EditorTests','MergeDesk.DraftTests')) {
        & dotnet run --project "tests/$suite" -c Release --no-build
        if ($LASTEXITCODE -ne 0) { throw "Failed: $suite" }
    }
    & dotnet run --project tests/MergeDesk.DraftTests -c Release --no-build -- --help artifacts/help-check
    if ($LASTEXITCODE -ne 0) { throw 'Help checks failed.' }
} finally { Pop-Location }
