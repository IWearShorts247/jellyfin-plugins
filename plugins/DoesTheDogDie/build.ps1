# Builds the plugin and assembles a ready-to-install plugin folder under dist\.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$version = '1.0.0.0'
$outDir = Join-Path $root "dist\DoesTheDogDie_$version"

dotnet publish (Join-Path $root 'Jellyfin.Plugin.DoesTheDogDie') -c Release -o (Join-Path $root 'dist\publish')
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

New-Item -ItemType Directory -Force $outDir | Out-Null
Copy-Item (Join-Path $root 'dist\publish\Jellyfin.Plugin.DoesTheDogDie.dll') $outDir -Force
Copy-Item (Join-Path $root 'meta.json') $outDir -Force

Write-Host "`nPlugin folder ready: $outDir"
