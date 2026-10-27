<#
.SYNOPSIS
  Construit le paquet de déploiement DeeKeep pour le serveur Linux (ARM64 par défaut).

.DESCRIPTION
  1. Compile le client Angular en production et le place dans wwwroot de la passerelle.
  2. Publie chaque microservice pour la cible (dépendant du runtime .NET partagé :
     les six processus partagent ainsi les pages du framework en mémoire).
  3. Assemble deploy/out/deekeep-<version>-<rid>.tar.gz avec les unités systemd,
     les scripts et le modèle de configuration.

.EXAMPLE
  ./deploy/publish.ps1                 # linux-arm64 (Raspberry Pi)
  ./deploy/publish.ps1 -Runtime linux-x64
#>
param(
    [string]$Runtime = "linux-arm64",
    [string]$Version = (Get-Date -Format "yyyy.MM.dd-HHmm"),
    [switch]$SkipWeb
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$env:PATH = "C:\Program Files\dotnet;C:\Program Files\nodejs;$env:PATH"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = "1"
$env:DOTNET_NOLOGO = "1"

$stage = Join-Path $root "deploy\out\deekeep-$Version-$Runtime"
if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force $stage | Out-Null

$wwwroot = Join-Path $root "src\Gateway\DeeKeep.Gateway\wwwroot"
if (-not $SkipWeb) {
    Write-Host "== Client Angular (production)"
    Push-Location (Join-Path $root "web")
    try {
        if (-not (Test-Path node_modules)) { npm ci }
        npx ng build --configuration production
        if ($LASTEXITCODE -ne 0) { throw "Échec du build Angular" }
    } finally { Pop-Location }
    if (Test-Path $wwwroot) { Remove-Item -Recurse -Force $wwwroot }
    Copy-Item -Recurse (Join-Path $root "web\dist\web\browser") $wwwroot
}

$services = "Gateway", "Catalog", "Library", "Downloader", "Quality", "Storage"
foreach ($svc in $services) {
    Write-Host "== $svc ($Runtime)"
    $project = if ($svc -eq "Gateway") { "src\Gateway\DeeKeep.Gateway" } else { "src\Services\DeeKeep.$svc" }
    dotnet publish (Join-Path $root $project) -c Release -r $Runtime --self-contained false `
        -p:PublishReadyToRun=true -p:DebugType=none -p:Version=1.0.0 -p:InformationalVersion=$Version `
        -o (Join-Path $stage "app\$svc")
    if ($LASTEXITCODE -ne 0) { throw "Échec de la publication de $svc" }
}

Copy-Item -Recurse (Join-Path $root "deploy\systemd") (Join-Path $stage "systemd")
Copy-Item -Recurse (Join-Path $root "deploy\bin") (Join-Path $stage "bin")
Copy-Item -Recurse (Join-Path $root "deploy\config") (Join-Path $stage "config")
Copy-Item (Join-Path $root "deploy\install.sh") $stage
Copy-Item (Join-Path $root "docs\DEPLOIEMENT.md") $stage -ErrorAction SilentlyContinue
Set-Content -Path (Join-Path $stage "VERSION") -Value $Version -Encoding ascii

# Les scripts shell doivent garder des fins de ligne Unix.
Get-ChildItem $stage -Recurse -Include *.sh, *.service, *.timer, *.target, Caddyfile | ForEach-Object {
    $text = [IO.File]::ReadAllText($_.FullName) -replace "`r`n", "`n"
    [IO.File]::WriteAllText($_.FullName, $text, (New-Object System.Text.UTF8Encoding($false)))
}

$archive = "$stage.tar.gz"
Push-Location (Split-Path $stage)
try { tar -czf $archive (Split-Path $stage -Leaf) } finally { Pop-Location }
Write-Host ""
Write-Host "Paquet prêt : $archive"
Write-Host "Sur le serveur : tar xzf $(Split-Path $archive -Leaf) && cd $(Split-Path $stage -Leaf) && sudo ./install.sh"
