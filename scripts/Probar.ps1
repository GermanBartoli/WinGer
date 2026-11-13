param([switch]$IntegracionLocal, [string]$Informe = "$PSScriptRoot\..\artifacts\pruebas\informe.md")
$ErrorActionPreference = 'Stop'
$raiz = Split-Path $PSScriptRoot -Parent
$destinoInforme = [IO.Path]::GetFullPath($Informe)
New-Item -ItemType Directory -Path (Split-Path $destinoInforme -Parent) -Force | Out-Null
$salidaPruebas = @()
$estado = 'Falló'
$salidaBuild = @()
try {
    Push-Location $raiz
    $argumentos = @('run', '--project', 'tests/WinGer.Pruebas', '-c', 'Release')
    if ($IntegracionLocal) { $argumentos += @('--', '--integracion-firma') }
    $salidaPruebas = @(& dotnet @argumentos 2>&1 | ForEach-Object { "$_" })
    $codigoPruebas = $LASTEXITCODE
    $salidaPruebas | ForEach-Object { Write-Host $_ }
    if ($codigoPruebas -ne 0) { throw 'Fallaron las pruebas. Consultá el informe.' }
    $salidaBuild = @(& "$PSScriptRoot\Publicar.ps1" 2>&1 | ForEach-Object { "$_" })
    $publicacion = Join-Path $raiz 'artifacts\WinGer-win-x64'
    foreach ($recurso in @('WinGer.exe','WinGer.dll','WinGer.Core.dll','WinGer.pri','MainWindow.xbf','Assets\WinGer.ico')) {
        $ruta = Join-Path $publicacion $recurso
        if (!(Test-Path -LiteralPath $ruta) -or (Get-Item -LiteralPath $ruta).Length -eq 0) { throw "Falta recurso publicado: $recurso" }
    }
    $icono = [IO.File]::ReadAllBytes((Join-Path $publicacion 'Assets\WinGer.ico'))
    if ($icono[0] -ne 0 -or $icono[1] -ne 0 -or $icono[2] -ne 1 -or $icono[3] -ne 0 -or [BitConverter]::ToUInt16($icono,4) -lt 5) { throw 'El ICO no contiene los tamaños esperados.' }
    $estado = 'Correcto'
    Write-Host 'Publicación, recursos XAML e icono multirresolución correctos.'
}
finally {
    Pop-Location
    $cantidad = @($salidaPruebas | Where-Object { $_ -like 'Correcto:*' }).Count
    $resumen = @('# Informe de pruebas de WinGer', '', "- Estado: $estado", "- Fecha: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss zzz')", "- Verificaciones correctas: $cantidad", '- Ejecución sin cursor, ventanas de prueba, instalaciones ni consultas externas de VirusTotal.', '- Las respuestas HTTP son simuladas y los archivos temporales están aislados.', '', '## Límites', '', 'No verifica distribución visual, clics reales, UAC, inicio de sesión, bandeja, asistentes de terceros ni lectura de la web real. La publicación comprueba compilación y recursos, no reemplaza una prueba visual.', '', '## Resultados', '')
    $resumen += $salidaPruebas | ForEach-Object { '- ' + $_ }
    $resumen += @('', '## Compilación', '')
    $resumen += $salidaBuild | ForEach-Object { '- ' + $_ }
    [IO.File]::WriteAllText($destinoInforme, ($resumen -join "`r`n"), [Text.UTF8Encoding]::new($false))
    Write-Host "Informe: $destinoInforme"
}
