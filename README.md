# Carvis

Asistente de IA **local** para Windows, estilo Jarvis. Se abre con **Alt+Espacio**, responde en streaming con un modelo que corre en tu propio PC ([Ollama](https://ollama.com)) y puede **actuar en el PC**: archivos, programas, ventanas, volumen, recordatorios, rutinas, documentos, voz y capturas de pantalla. Sin APIs de pago y sin enviar tus datos a Internet.

![Carvis](docs/screenshot.png)

## Qué hace

- **Chat** con formato (Markdown, código resaltado, enlaces), historial guardado y cifrado, búsqueda de conversaciones, regenerar y editar.
- **Acciones en el PC** (61 herramientas): crear, mover, copiar, renombrar, comprimir, ordenar y buscar archivos; abrir y cerrar programas, colocar ventanas; volumen, multimedia, brillo, modo oscuro, bloquear o apagar; portapapeles; PowerShell. Todo lo que cambia algo se confirma y se puede **deshacer**.
- **Tus documentos**: indexa carpetas (PDF, Word, Excel, PowerPoint, texto, imágenes con OCR) y responde citando el archivo y la página.
- **Recordatorios, temporizadores y alarmas**, también repetidos y con rutinas programadas ("cada lunes a las 9, modo estudio").
- **Rutinas**: varias acciones con una sola orden ("modo estudio").
- **Utilidades exactas**: calculadora, unidades, fechas y zonas horarias, notas y tareas.
- **Voz** (opcional): pulsa un atajo o di «Carvis», habla, y te contesta en voz alta. Whisper y Piper, en local.
- **Visión**: captura una zona de la pantalla (o pega una imagen) y pregúntale qué ve; usa `qwen2.5vl`.
- **Internet solo si lo activas**: tiempo, divisas y búsqueda con un SearXNG propio.

## Instalar

1. Instala [Ollama](https://ollama.com/download) (el asistente de primer arranque también puede instalarlo con winget).
2. Descarga `CarvisApp-win-Setup.exe` de la [última versión](https://github.com/carvima07eng-dot/Carvis/releases/latest) y ejecútalo. No necesita permisos de administrador.
3. Al abrirse, el asistente de primer arranque comprueba Ollama, descarga los modelos (`qwen3:8b` y `nomic-embed-text`), te pregunta qué carpetas quieres que lea y prueba el atajo.

Carvis se actualiza solo desde GitHub Releases (se puede desactivar en Ajustes → Privacidad).

> El instalador no está firmado digitalmente: Windows SmartScreen puede avisar la primera vez («Más información» → «Ejecutar de todas formas»).

### Requisitos

- Windows 10 u 11 de 64 bits.
- Una gráfica NVIDIA con 8 GB o más (probado para una RTX 5070 de 12 GB). Sin gráfica funciona, pero mucho más lento.
- Unos 7 GB de disco para los modelos de chat (más 0,5–2 GB si activas la voz y 6 GB para el modelo de visión).

## Uso rápido

| Acción | Cómo |
|---|---|
| Mostrar u ocultar | **Alt+Espacio**, el botón de la barra de tareas o el icono de la bandeja |
| Hablar | **Ctrl+Alt+Espacio** o el micrófono (activa la voz en Ajustes → Voz) |
| Capturar una zona de la pantalla | **Ctrl+Alt+S** o el botón de captura |
| Enviar / nueva línea | **Enter** / **Shift+Enter** |
| Nueva conversación · historial · ajustes | **Ctrl+N** · **Ctrl+H** · **Ctrl+,** |
| Comandos | `/ayuda`, `/nueva`, `/historial`, `/modelo`, `/memoria`, `/acciones`, `/gpu`, `/liberar` |

Ejemplos: «créame una carpeta en el escritorio llamada Clase», «ordena mis descargas por tipo», «abre Spotify y ponlo a la izquierda», «recuérdame mañana a las 9 entregar la práctica», «¿qué dicen mis apuntes de redes sobre TCP?», «¿cuánto es el 21 % de 350?», «¿qué error sale en mi pantalla?».

Más detalle en el [manual de usuario](docs/MANUAL.md). Si algo falla, mira [solución de problemas](docs/TROUBLESHOOTING.md).

## Desarrollo

Requiere el [SDK de .NET 8](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
git clone https://github.com/carvima07eng-dot/Carvis.git
cd Carvis
dotnet run --project src/Carvis.App
dotnet test
```

```
src/Carvis.App      Interfaz Avalonia (MVVM), bandeja, atajos globales, capturas, actualizaciones
src/Carvis.Core     Chat, herramientas, permisos, documentos (RAG), almacenamiento, voz, visión
src/Carvis.Windows  Implementaciones de Windows: ventanas, audio, volumen, papelera, OCR, energía
src/Carvis.Voice    Reconocimiento de voz con Whisper.net (Vulkan o CPU)
tests/              Tests del Core, tests de interfaz (Avalonia headless) y evals con Ollama real
```

- Arquitectura, cómo añadir una herramienta o un plugin: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
- Evals del uso de herramientas con el modelo real: `dotnet run --project tests/Carvis.Evals`
- Publicar una versión: crea la etiqueta `vX.Y.Z` y súbela (`git tag v1.0.0 && git push origin v1.0.0`); el workflow *Release* genera el instalador y lo publica.
- Historial de cambios: [CHANGELOG.md](CHANGELOG.md) · Licencias de terceros: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)

© 2026 Carlos Vidal Marín.
