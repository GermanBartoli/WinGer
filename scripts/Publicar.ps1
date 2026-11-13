param([string]$Destino = "$PSScriptRoot\..\artifacts\WinGer-win-x64")
$ErrorActionPreference = 'Stop'
dotnet publish "$PSScriptRoot\..\src\WinGer\WinGer.csproj" -c Release -p:Platform=x64 -r win-x64 --self-contained true -o $Destino
if ($LASTEXITCODE -ne 0) { throw 'Falló la publicación de WinGer.' }
Write-Host "Ejecutable disponible en $Destino\WinGer.exe"
