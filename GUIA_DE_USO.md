# Guía de uso de WinGer

1. Descargá el ZIP **win-x64** desde [Releases](https://github.com/GermanBartoli/WinGer/releases).
2. Extraé **todo el contenido** en una carpeta permanente y abrí `WinGer.exe`. La distribución incluye .NET y Windows App SDK.
3. En **Configuración**, elegí cómo consultar al buscar actualizaciones: **VTAI**, **Web experimental** o **Desactivado**.
4. Pulsá **Buscar actualizaciones**. Cada tarjeta muestra el programa, sus versiones y los resultados por fuente.
5. Para instalar, pulsá **Actualizar** y revisá la confirmación. Las advertencias y los errores aparecen en pantalla.

**Requisitos:** Windows 10 1809 o posterior / Windows 11, x64. WinGet y vt-cli se instalan por separado y deben estar disponibles en PATH. Reiniciá WinGer si los instalaste mientras estaba abierto. Las funciones de Microsoft Store requieren una versión compatible de la tienda.

La consulta API y la actualización verificada requieren tu clave clásica de VirusTotal en **Configuración**. VTAI usa su propio token, que WinGer registra una sola vez y protege en Windows; también admite importar uno existente. El repositorio no incluye credenciales.

Para actualizar WinGer, cerralo desde **bandeja → Salir** y reemplazá los archivos de su carpeta. La configuración vive fuera de esa carpeta. El ejecutable de esta vista previa no tiene firma de editor; cada release incluye el SHA-256 del ZIP.

[Preguntas frecuentes](PREGUNTAS_FRECUENTES.md) · [Documentación completa](README.md)
