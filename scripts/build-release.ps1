param(
    [string]$Version = "1.1.0"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $root "MedalShadowSync\MedalShadowSync.csproj"
$publishDir = Join-Path $root "publish"
$releasesDir = Join-Path $root "dist\releases"

Write-Host "Publishing reMedal v$Version..."
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
New-Item -ItemType Directory -Path $releasesDir -Force | Out-Null

dotnet publish $csproj -c Release -r win-x64 --self-contained true -p:Version=$Version -p:DebugType=none -p:DebugSymbols=false -o $publishDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Packaging Velopack installer & update bundle..."
vpk pack --packId reMedal --packTitle "reMedal" --packVersion $Version --packDir $publishDir --mainExe reMedal.exe --runtime win-x64 --outputDir $releasesDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Done! Artifacts in $releasesDir :"
Get-ChildItem $releasesDir | Select-Object Name, @{N='SizeMB';E={[math]::Round($_.Length/1MB,1)}} | Format-Table -AutoSize
