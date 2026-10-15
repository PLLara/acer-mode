# Publica o Acer Mode 2 e troca o exe em uso. A troca precisa de elevação, porque o app
# antigo roda como administrador. O exe antigo vira AcerMode.legacy.exe (volta: renomear de novo).
#   pwsh -File tools\publish.ps1            # publica e troca
#   pwsh -File tools\publish.ps1 -NoSwap    # só publica em publish\
param([switch]$NoSwap)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
Set-Location $root

dotnet publish src/AcerMode.App -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
if ($LASTEXITCODE) { throw "dotnet publish falhou" }
if ($NoSwap) { return }

$target = Join-Path $root 'AcerMode.exe'
Get-Process AcerMode -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $target } | Stop-Process -Force
Start-Sleep -Milliseconds 800
if (Test-Path $target) {
    if (Test-Path (Join-Path $root 'AcerMode.legacy.exe')) { Remove-Item $target } else { Rename-Item $target 'AcerMode.legacy.exe' }
}
Copy-Item (Join-Path $root 'publish\AcerMode.exe') $target
Start-Process $target -ArgumentList '--tray'
