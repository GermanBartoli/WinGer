# WinGer

Panel de escritorio para Windows con **C# · .NET 10 · WinUI 3**, orientado a actualizaciones con WinGet y comprobaciones con VirusTotal.

## Versión 0.2.2

- Sección **Microsoft Store**: consulta real de actualizaciones e inventario con la herramienta oficial `store.exe`, sin instalar nada durante la consulta.
- Botón **Abrir Store para actualizar**: abre la página de descargas de la tienda; la instalación se completa allí y después puede consultarse de nuevo desde WinGer.
- Estado y errores visibles; los resultados de la tienda no se presentan como análisis de VirusTotal. No se requiere otra clave ni iniciar sesión en WinGer.
- Requiere una versión de Microsoft Store que incluya [Store CLI](https://blogs.windows.com/windowsdeveloper/2026/02/11/enhanced-developer-tools-on-the-microsoft-store/). Si no está disponible, se muestra el motivo y se conserva el acceso a la tienda.

## Correcciones de la versión 0.2.1

- Los bloqueos muestran automáticamente un mensaje con el motivo y sus detalles técnicos; cada tarjeta tiene un botón **Ver detalles / error**.
- Corrección de lectura del formato real de vt-cli (`_id` y atributos planos), conservando la comprobación del SHA-256.
- Opción **Actualizar igualmente** ante firma ausente o no comprobable, solo con hash coincidente e informe reciente sin detecciones; la advertencia permanece amarilla al finalizar.
- Instalación local de MSIX/AppX mediante Windows, que exige su firma y dependencias instaladas. No descarga dependencias ni fuerza el cierre de aplicaciones.

## Funciones

- Caché persistente de búsquedas, selección, filtros, hashes y resultados. Al reabrir se conservan las detecciones y su color, con la etiqueta «Guardado» y la fecha de búsqueda. No hace falta consultar de nuevo para ver el resultado; antes de instalar se verifica el archivo real.
- **Borrar caché**, en Configuración, limpia la sesión y los informes reutilizables; conserva ajustes, credenciales, historial e instaladores.
- Consulta automática **VTAI** al buscar actualizaciones, sin clave clásica ni subida de archivos. WinGer registra su propia conexión una sola vez y protege el token en Windows; también permite importar uno existente. No crea otra identidad automáticamente ante errores.
- **Leer web · experimental** importa las filas visibles y la fecha del informe en una ventana integrada. También puede seleccionarse como consulta automática en Configuración. No usa API internas ni elude verificaciones del sitio. Si no puede leer el informe, queda pendiente.
- Informes API y VTAI reutilizables durante una hora entre sesiones, separados por fuente y hash. La lectura web queda como evidencia informativa y no autoriza instalaciones.
- Tarjetas seleccionables, búsqueda, selección múltiple y menú con clic derecho por aplicación.
- Consulta previa a la descarga del hash publicado por WinGet: botón **Ver en VirusTotal · sin API** para abrir el informe existente en el navegador, sin clave ni consumo de tu cuota de API.
- Consulta opcional del informe existente por API para mostrar estados verde, rojo, amarillo y gris en cada tarjeta; no solicita análisis nuevos. Se espacian peticiones y se reutilizan durante una hora los resultados del mismo hash en disco.
- Descarga y verificación del archivo real al actualizar.
- Actualización del instalador comprobado: revalida hash, firma e informe de vt-cli antes de ejecutarlo y comprueba después la versión instalada.
- Centro VirusTotal: consulta de hashes MD5, SHA-1 y SHA-256 por API.
- Calcula el SHA-256 de archivos locales sin subirlos.
- Integración independiente con **vt-cli**: consulta de hashes, subida con confirmación y consulta por ID de análisis.
- Historial local de las últimas 200 operaciones.
- Temas del sistema, oscuro y claro.
- Opción para cerrar a la bandeja del sistema, junto al reloj: clic en el icono W para restaurar, menú derecho para abrir o salir.
- Instancia única: abrir de nuevo el acceso directo restaura la ventana existente.
- Clave protegida en el Administrador de credenciales de Windows, con indicador persistente de que ya está guardada.

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

Configuración, historial y descargas se guardan en `%LOCALAPPDATA%\WinGer`. La clave se almacena con el recurso `WinGer.VirusTotal` en Windows; vt-cli la recibe como variable de entorno de su proceso, nunca como argumento ni en el historial. Los instaladores se conservan para inspección; podés borrar la carpeta `Descargas` cuando no haya operaciones en curso, pero deberás analizarlos otra vez.

La integración de bandeja utiliza el componente NotifyIcon de Windows Forms; el panel sigue siendo WinUI 3. YamlDotNet interpreta los manifiestos que descarga WinGet.

## Alcance pendiente

Antes de descargar, verde significa que el informe del hash publicado tiene motores analizados, cero detecciones y cero sospechas, y fecha dentro de los últimos 30 días; el archivo todavía no está verificado. **Ver web** abre el navegador; **Leer web · experimental** importa la evidencia visible y mantiene amarillo cuando no hay detecciones. VTAI muestra detecciones y sospechas con su fuente. La instalación sigue requiriendo las comprobaciones con la clave clásica y vt-cli.

Para instalar se admiten MSI/WiX y EXE/Inno/Nullsoft/Burn sin dependencias declaradas ni desinstalación previa requerida. MSIX/AppX se instala con [Add-AppxPackage](https://learn.microsoft.com/en-us/powershell/module/appx/add-appxpackage?view=windows-11); Windows comprueba su firma y las dependencias presentes, y rechaza la operación si faltan. El hash del archivo real debe coincidir con el manifiesto exacto. La aprobación local vence después de una hora. Ningún estado verde representa una garantía de seguridad.

Una firma ausente o no comprobable permite continuar únicamente después de aceptar la advertencia de ese instalador. Una firma no confiable o con integridad inválida continúa bloqueada. Sin informe, con un informe antiguo, con dependencias no gestionadas o formatos no implementados, el programa queda pendiente en amarillo. Las detecciones y fallos de integridad impiden la instalación. WinGer no sube instaladores automáticamente ni omite comprobaciones fallidas.

Las actualizaciones requieren pulsar el botón y confirmar la selección. Se ejecuta el mismo archivo local bajo un bloqueo de escritura, no una nueva descarga de WinGet. Los EXE pueden mostrar el asistente del fabricante; los MSI se ejecutan sin reinicio automático. Windows puede pedir elevación. Cancelar detiene la cola, no un instalador iniciado. Un fabricante puede descargar componentes adicionales dentro de su propio asistente, fuera del análisis del instalador inicial.

La lectura de la lista de WinGet admite cabeceras en español e inglés y omite identificadores truncados o versiones desconocidas. No hay actualizaciones programadas ni soporte para otros gestores. La comprobación posterior de versión es conservadora: si no coincide exactamente, queda pendiente de confirmar.

La API se prueba con respuestas simuladas (incluidos errores 401, 403, 404 y 429), y existen verificaciones para manifiestos, reglas de aprobación y salida de vt-cli. Se puede ejecutar `dotnet run --project tests/WinGer.Pruebas -c Release -- --integracion-winget --integracion-firma` para comprobar la lista y firmas reales sin instalar ni subir archivos. Las instalaciones completas y las subidas requieren validación manual.

## Autoría

Creado por **German Bartoli**, con colaboración de **Codex**. Proyecto independiente inspirado en el tipo de experiencia de [UniGetUI](https://github.com/Devolutions/UniGetUI); esta versión no incorpora su código ni sus recursos.
