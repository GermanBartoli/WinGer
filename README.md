# WinGer

Panel de escritorio para Windows con **C# · .NET 10 · WinUI 3**, orientado a actualizaciones con WinGet y comprobaciones con VirusTotal.

## Primera versión · 0.1.0

- Consulta las actualizaciones disponibles mediante WinGet, sin instalar programas.
- Centro VirusTotal: consulta de hashes MD5, SHA-1 y SHA-256 por API.
- Calcula el SHA-256 de archivos locales sin subirlos.
- Integración independiente con **vt-cli**: consulta de hashes, subida con confirmación y consulta por ID de análisis.
- Historial local de las últimas 200 operaciones.
- Temas del sistema, oscuro y claro.
- Opción para minimizar a la barra de tareas al cerrar con la cruz; salida definitiva desde Configuración.
- Clave protegida en el Administrador de credenciales de Windows.

La API y vt-cli consultan el mismo servicio de VirusTotal; no son dos motores antivirus distintos. Consultar un hash no sube el archivo. Subirlo puede compartir su contenido con terceros, por lo que WinGer pide confirmación cada vez. Cero detecciones no garantiza seguridad.

## Ejecutar

Descargá el ZIP de la [release](https://github.com/GermanBartoli/WinGer/releases), extraé **todo su contenido** y abrí `WinGer.exe`. La distribución x64 incluye .NET y Windows App SDK. No requiere una instalación global de .NET. Requiere Windows 10 1809 o posterior, o Windows 11, x64.

WinGet y vt-cli se instalan por separado y deben estar disponibles en PATH. Reiniciá WinGer después de instalar esas herramientas. La consulta por API no necesita vt-cli. Guardá tu propia clave de VirusTotal en Configuración; el repositorio no incluye ninguna.

El ejecutable de esta vista previa no tiene firma de editor. La publicación incluye un archivo SHA-256 para verificar la integridad del ZIP.

## Desarrollo

Con .NET SDK 10 y herramientas de compilación de Windows:

```powershell
dotnet build src/WinGer/WinGer.csproj -c Release -p:Platform=x64
dotnet run --project tests/WinGer.Pruebas -c Release
./scripts/Publicar.ps1
```

- `src/WinGer`: aplicación nativa, navegación, modelo de vista del estado y configuración de credenciales.
- `src/WinGer.Core`: ejecución de procesos, hashes, cliente HTTP y almacenamiento local.
- `tests/WinGer.Pruebas`: verificaciones sin servicios externos ni dependencias de pruebas adicionales.
- `scripts/Publicar.ps1`: publicación autónoma para Windows x64.

Configuración e historial se guardan en `%LOCALAPPDATA%\WinGer`. La clave se almacena con el recurso `WinGer.VirusTotal` en Windows; vt-cli la recibe como variable de entorno de su proceso, nunca como argumento ni en el historial.

## Alcance pendiente

Esta entrega es una base funcional, no un reemplazo completo de UniGetUI. La salida de WinGet se presenta como texto; la selección de paquetes, exclusiones propias y actualización de programas están pendientes. Antes de habilitar instalaciones, se validará la descarga, la firma, el hash, las dependencias y que el archivo ejecutado sea exactamente el comprobado. No hay instalaciones automáticas, icono de bandeja ni tareas programadas.

La API se prueba con respuestas simuladas (incluidos errores 401, 403, 404 y 429). No se suben archivos durante las pruebas. Las credenciales reales y las subidas requieren validación manual con una cuenta del usuario.

## Autoría

Creado por **German Bartoli**, con colaboración de **Codex**. Proyecto independiente inspirado en el tipo de experiencia de [UniGetUI](https://github.com/Devolutions/UniGetUI); esta versión no incorpora su código ni sus recursos.
