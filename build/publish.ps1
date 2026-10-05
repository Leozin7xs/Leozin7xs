# Gera publish\win-x64\WPSupportTools.exe (arquivo único, não exige .NET instalado).
# Uso (na raiz do repositório):  powershell -File build\publish.ps1
$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

dotnet test WPSupportTools.sln -c Release
dotnet publish src\WPSupportTools.App -p:PublishProfile=win-x64 -p:DebugType=none

Write-Host "`nExecutável gerado em: $(Resolve-Path publish\win-x64\WPSupportTools.exe)"
