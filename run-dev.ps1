<#
.SYNOPSIS
  Lance DeeKeep en développement : les 5 microservices, la passerelle et le client Angular.
  Configuration : config/dev/appsettings.json (base = COPIE dans .data/, jamais la base réelle).
  Ctrl+C arrête tout.
#>
param([switch]$NoWeb)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$env:PATH = "C:\Program Files\dotnet;C:\Program Files\nodejs;$env:PATH"
$env:DOTNET_NOLOGO = "1"
$env:DEEKEEP_CONFIG = Join-Path $root "config\dev\appsettings.json"
$env:ASPNETCORE_ENVIRONMENT = "Development"

if (-not (Test-Path (Join-Path $root ".data\deekeep_cache.db"))) {
    Write-Warning "Pas de base de développement : copiez %APPDATA%\DeeKeep\deekeep_cache.db dans .data\ (sauvegarde SQLite, pas une copie à chaud du fichier)."
}

dotnet build (Join-Path $root "DeeKeep.slnx") -v quiet
if ($LASTEXITCODE -ne 0) { throw "Échec de la compilation" }

$processes = @()
$projects = "src\Services\DeeKeep.Catalog", "src\Services\DeeKeep.Library", "src\Services\DeeKeep.Downloader",
            "src\Services\DeeKeep.Quality", "src\Services\DeeKeep.Storage", "src\Gateway\DeeKeep.Gateway"
try {
    foreach ($p in $projects) {
        $processes += Start-Process dotnet -ArgumentList "run", "--no-build", "--project", (Join-Path $root $p) -PassThru -NoNewWindow
    }
    if (-not $NoWeb) {
        Push-Location (Join-Path $root "web")
        try { npx ng serve --host 0.0.0.0 } finally { Pop-Location }
    } else {
        Write-Host "Passerelle : http://127.0.0.1:5000  (Ctrl+C pour arrêter)"
        Wait-Process -Id ($processes | ForEach-Object Id)
    }
} finally {
    foreach ($proc in $processes) { if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force -ErrorAction SilentlyContinue } }
}
