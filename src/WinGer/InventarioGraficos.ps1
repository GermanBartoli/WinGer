$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
$apps = @{}
function Agregar($nombre, $ruta) {
    if ($ruta -and -not $apps.ContainsKey($ruta)) {
        $apps[$ruta] = @{ Nombre = $nombre; Ruta = $ruta }
    }
}

$shell = New-Object -ComObject Shell.Application
foreach ($item in $shell.NameSpace('shell:AppsFolder').Items()) {
    $ruta = $item.ExtendedProperty('System.Link.TargetParsingPath')
    if ($item.Path -match '^[\w.-]+_[a-z0-9]+![\w.-]+$') {
        Agregar $item.Name $item.Path
    } elseif ($ruta -like '*.exe' -and [IO.File]::Exists($ruta) -and
        $ruta -notmatch '\\(update|unins[^\\]*|msiexec|rundll32|cmd|wscript|chrome_proxy|setup)\.exe$') {
        Agregar $item.Name $ruta
    }
}

$accesos = New-Object -ComObject WScript.Shell
foreach ($escritorio in @([Environment]::GetFolderPath('Desktop'), [Environment]::GetFolderPath('CommonDesktopDirectory'))) {
    foreach ($archivo in (Get-ChildItem -LiteralPath $escritorio -Filter '*.lnk' -ErrorAction SilentlyContinue)) {
        $ruta = $accesos.CreateShortcut($archivo.FullName).TargetPath
        if ($ruta -like '*.exe' -and [IO.File]::Exists($ruta) -and $ruta -notmatch '\\(cmd|powershell|pwsh|update|rundll32|wscript|msiexec)\.exe$') { Agregar $archivo.BaseName $ruta }
    }
}

$steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath
if ($steam) {
    $bibliotecas = @($steam)
    $indice = Join-Path $steam 'steamapps\libraryfolders.vdf'
    if ([IO.File]::Exists($indice)) {
        $bibliotecas += [regex]::Matches([IO.File]::ReadAllText($indice), '"path"\s*"([^"]+)"') |
            ForEach-Object { $_.Groups[1].Value.Replace('\\','\') }
    }
    foreach ($biblioteca in ($bibliotecas | Sort-Object -Unique)) {
        if (![IO.Directory]::Exists($biblioteca)) { continue }
        $carpeta = Join-Path $biblioteca 'steamapps'
        foreach ($manifiesto in (Get-ChildItem -LiteralPath $carpeta -Filter 'appmanifest_*.acf' -ErrorAction SilentlyContinue)) {
            $datos = [IO.File]::ReadAllText($manifiesto.FullName)
            $dir = [regex]::Match($datos, '"installdir"\s*"([^"]+)"').Groups[1].Value
            if (!$dir -or $dir -match '[/\\]' -or $dir -eq '..') { continue }
            $nombre = [regex]::Match($datos, '"name"\s*"([^"]+)"').Groups[1].Value
            $base = Join-Path $carpeta "common\$dir"
            $archivos = @(Get-ChildItem -LiteralPath $base -File -Filter '*.exe' -ErrorAction SilentlyContinue |
                Where-Object { $_.BaseName -notmatch 'crash|redist|unins|setup|install|report|anti.?cheat|updater|protected_game|helper|bootstrap|server|launcher' })
            $principal = @($archivos | Where-Object { $_.BaseName -eq $dir -or $_.BaseName -eq $nombre })
            if ($archivos.Count -eq 1) { $principal = $archivos }
            $principal += @(Get-ChildItem -Path (Join-Path ([WildcardPattern]::Escape($base)) '*\Binaries\Win64\*-Shipping.exe') -File -ErrorAction SilentlyContinue)
            foreach ($exe in $principal) { Agregar $nombre $exe.FullName }
        }
    }
}
# Algunos lanzadores abren el ejecutable que se encuentra en la carpeta de versión.
foreach ($app in @($apps.Values)) {
    if ($app.Ruta -notlike '*.exe') { continue }
    $base = [IO.Path]::GetDirectoryName($app.Ruta)
    $nombreExe = [IO.Path]::GetFileName($app.Ruta)
    $version = Get-ChildItem -LiteralPath $base -Directory -Filter 'app-*' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^app-\d+(\.\d+){1,3}$' -and [IO.File]::Exists([IO.Path]::Combine($_.FullName, $nombreExe)) } |
        Sort-Object { [version]$_.Name.Substring(4) } -Descending | Select-Object -First 1
    if ($version) { Agregar $app.Nombre ([IO.Path]::Combine($version.FullName, $nombreExe)) }
}
ConvertTo-Json -InputObject @($apps.Values) -Compress
