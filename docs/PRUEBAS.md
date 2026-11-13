# Pruebas de WinGer sin usar el escritorio

Ejecutar desde la raíz:

```powershell
./scripts/Probar.ps1
```

El comando ejecuta las pruebas, publica la aplicación y comprueba los recursos del ejecutable. No abre WinGer ni mueve el cursor. Genera `artifacts/pruebas/informe.md`, incluido el resultado si falla. Podés seguir usando la computadora.

Para incluir además la comprobación real de firma de un archivo temporal sin ejecutar:

```powershell
./scripts/Probar.ps1 -IntegracionLocal
```

Para comprobar también las dependencias reales de OLE DB, sin descargar ni instalar:

```powershell
./scripts/Probar.ps1 -IntegracionLocal -IntegracionDependencias
```

Comprueba Visual C++ x86 y x64 ya instalados. Esta opción depende del inventario de la PC; la suite predeterminada usa respuestas simuladas.

## Cobertura

| Área | Comprobación automática | Límite |
| --- | --- | --- |
| API y vt-cli | Respuestas válidas, errores HTTP, estadísticas incompletas, identidad del hash, cache y selección de consultas faltantes sin repetir informes existentes. | No consume la cuenta real. |
| VTAI | Registro simulado, token, respuestas, cuotas, Retry-After y cancelación. | No registra identidades reales. |
| Tres resultados | Independencia, cambios de versión/hash, colores, avisos, persistencia y notificaciones a la interfaz. | No mide distribución visual. |
| Web | Interpretación de evidencia, origen, hash, motores, respuestas incompletas y ausencia explícita «Item not found» con origen y hash correctos. | Comprueba pausa, reanudación, preferencia persistente y colores. No abre VirusTotal ni verifica inicio de sesión, cookies reales o votos. |
| Actualización | Formatos, firmas admitidas, vigencia, integridad, herencia de desinstalación previa y rechazo sin confirmación antes de tocar archivos o procesos. | No instala software ni prueba UAC/asistentes. |
| Dependencias | Manifiestos genéricos, x86/x64, ID exacto, versiones mínimas, bloques vacíos, herencia, errores de consulta, cancelación y cambio de inventario entre comprobaciones. | No instala requisitos faltantes ni activa características de Windows. |
| Datos locales | Ajustes, historial, archivos dañados y borrado de cache conservando el resto. | Usa directorios temporales, no tus datos. |
| Procesos | stdout/stderr, código de salida, ocultación de clave ficticia y cancelación de un hijo. | Solo ejecuta el propio runner y herramientas de desarrollo. |
| Interfaz | Persistencia de posición/tamaño/maximizado, minimización y límites de monitores; modelo de estado y eventos; compilación del XAML y presencia de recursos. | No prueba clics, foco, bandeja ni geometría. |
| Distribución | EXE, DLL, PRI, XBF y tamaños del ICO. | No garantiza funcionamiento en todas las versiones de Windows. |
| Microsoft Store | Compilación de su pantalla. | Consulta real e instalación de la tienda no están simuladas por esta suite. |

## Automatización en GitHub

El workflow `verificar.yml` ejecuta la misma suite al hacer push a main o abrir un pull request. Conserva el informe Markdown y la distribución como artefactos. Un fallo bloquea la verificación; no se oculta con un resultado de éxito.

## Qué sigue requiriendo una sesión de prueba

La interfaz visual, las verificaciones web, el Administrador de credenciales y los instaladores externos deben probarse aparte. Para hacerlo sin ocupar tu computadora se necesita una VM o una sesión de escritorio dedicada; la suite actual no afirma cubrir esos comportamientos.

## Versión 0.3.6

206 verificaciones: historial independiente persistente, migración sin duplicados, exclusión de instalaciones sin confirmar, conservación de selección y caché, explicación del análisis antiguo y habilitación de subida solo ante ausencia explícita de informe. Compilación y recursos Windows x64 correctos.

Prueba de escritorio: ambas pestañas del historial, diez actualizaciones previas recuperadas, 18 pendientes, selección conservada al reiniciar, icono de WinDbg y botón de subida habilitado. La selección y transmisión reales a VirusTotal no se ejercitan para evitar compartir archivos como parte de las pruebas.
