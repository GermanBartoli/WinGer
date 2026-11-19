param([string]$Destino = "C:\Cosas\Proyectos\release\WinGer\App")
$ErrorActionPreference = 'Stop'

$rutaAbsoluta = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Destino)
$rutaDestino = $rutaAbsoluta.TrimEnd('\', '/')
if ($rutaDestino -eq [IO.Path]::GetPathRoot($rutaAbsoluta).TrimEnd('\', '/')) {
    throw 'Elegí una carpeta de publicación distinta de la raíz.'
}
$carpetaPadre = [IO.Directory]::GetParent($rutaDestino)
$null = [IO.Directory]::CreateDirectory($carpetaPadre.FullName)

$sha = [Security.Cryptography.SHA256]::Create()
try {
    $nombreBloqueo = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($rutaDestino.ToUpperInvariant())))
} finally { $sha.Dispose() }
$bloqueo = [Threading.Mutex]::new($false, "WinGer.Publicar.$nombreBloqueo")
$trabajo = Join-Path $carpetaPadre.FullName ('.winger-publicar-' + [guid]::NewGuid().ToString('N'))
$salida = Join-Path $trabajo 'nueva'
$anterior = Join-Path $trabajo 'anterior'
$bloqueado = $false
$publicado = $false

try {
    try { $null = $bloqueo.WaitOne() } catch [Threading.AbandonedMutexException] { }
    $bloqueado = $true

    $existente = Get-Item -LiteralPath $rutaDestino -Force -ErrorAction SilentlyContinue
    if ($existente) {
        if (!$existente.PSIsContainer -or ($existente.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "El destino debe ser una carpeta normal: $rutaDestino"
        }
        if ((Get-ChildItem -LiteralPath $rutaDestino -Force | Select-Object -First 1) -and
            !(Test-Path -LiteralPath (Join-Path $rutaDestino 'WinGer.exe') -PathType Leaf)) {
            throw "El destino contiene archivos y no es una publicación de WinGer: $rutaDestino"
        }
    }

    $null = [IO.Directory]::CreateDirectory($trabajo)
    dotnet publish "$PSScriptRoot\..\src\WinGer\WinGer.csproj" -c Release -p:Platform=x64 -r win-x64 --self-contained true -o $salida
    if ($LASTEXITCODE -ne 0) { throw 'Falló la publicación de WinGer.' }
    if (!(Test-Path -LiteralPath (Join-Path $salida 'WinGer.exe') -PathType Leaf)) {
        throw 'La publicación no produjo WinGer.exe.'
    }

    if ($existente) { [IO.Directory]::Move($rutaDestino, $anterior) }
    try {
        [IO.Directory]::Move($salida, $rutaDestino)
    } catch {
        if ($existente) { [IO.Directory]::Move($anterior, $rutaDestino) }
        throw
    }
    $publicado = $true
} finally {
    try {
        if (Test-Path -LiteralPath $trabajo) {
            if (!$publicado -and (Test-Path -LiteralPath $anterior)) {
                Write-Warning "La publicación anterior quedó guardada en $anterior."
            } else {
                $resuelto = Get-Item -LiteralPath $trabajo -Force
                if ($resuelto.FullName -ne $trabajo -or ($resuelto.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                    throw "No se pudo verificar la carpeta temporal: $trabajo"
                }
                Remove-Item -LiteralPath $trabajo -Recurse -Force
            }
        }
    } finally {
        try {
            if ($bloqueado) { $bloqueo.ReleaseMutex() }
        } finally { $bloqueo.Dispose() }
    }
}

Write-Host "Ejecutable disponible en $rutaDestino\WinGer.exe"
