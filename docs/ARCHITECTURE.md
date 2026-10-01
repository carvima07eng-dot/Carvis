# Arquitectura de Carvis

## Proyectos

| Proyecto | Qué contiene | Depende de |
|---|---|---|
| `Carvis.Core` (net8.0) | Todo lo que no es interfaz ni Windows: chat, herramientas, permisos, documentos, almacenamiento, recordatorios, voz y visión (contratos y lógica) | OllamaSharp, Microsoft.Data.Sqlite, sqlite-vec, PdfPig, OpenXml |
| `Carvis.Windows` | Implementaciones Win32/COM/WinMM: ventanas, apps del menú Inicio, papelera, OCR, volumen, energía, audio, capturas | Core, NAudio.WinMM |
| `Carvis.Voice` | Reconocimiento de voz con Whisper.net (Vulkan → CPU) | Core |
| `Carvis.App` | Avalonia (MVVM con CommunityToolkit.Mvvm), bandeja, atajos (SharpHook), notificaciones, capturas, actualizaciones (Velopack) | todos |
| `tests/Carvis.Tests` | Tests xUnit del Core y de Windows | Core, Windows |
| `tests/Carvis.App.Tests` | Tests de interfaz con Avalonia headless (Skia) | App |
| `tests/Carvis.Evals` | Casos reales contra Ollama para medir si el modelo elige bien las herramientas | Core |

El Core registra implementaciones portables con `TryAdd` (`AddCarvisCore`); `AddCarvisWindows` y `AddCarvisVoice` las sustituyen con `Replace`. Así el Core y sus tests funcionan en cualquier sistema.

## Flujo de una pregunta

```
MainWindowViewModel.SendAsync
  └─ ChatService.SendAsync(ChatInput)
       ├─ CompressHistoryAsync      resume turnos antiguos si el contexto pasa del 45 %
       ├─ ToolSelector.SelectAsync  ≤10 herramientas: palabras clave, frases, embeddings y turno anterior
       ├─ BuildRequestAsync         un único mensaje de sistema: prompt + adjuntos + proveedores de contexto
       │                            (RAG con fuentes numeradas, sistema, memoria) + guía de herramientas + resumen
       ├─ IChatModelClient.StreamAsync   (OllamaChatModelClient; qwen2.5vl si hay imágenes)
       │     ThinkTagFilter separa el razonamiento
       └─ si hay tool calls → ToolExecutor.RunAsync
             Prepare → ToolPolicy (¿confirmar?) → IToolConfirmation (tarjeta en la UI)
             → ITool.ExecuteAsync → ActionJournal (deshacer) → FollowUpCalls (pasos de rutinas)
           …y vuelve al modelo con los resultados (máximo MaxToolSteps pasos)
```

Los eventos (`TextDelta`, `ToolStarted`, `ToolFinished`, `SourcesAttached`, `StatsReported`…) llegan a la vista en streaming. Al terminar, `TurnCommitted` guarda el turno en SQLite.

## Seguridad de las acciones

- `ToolRisk`: `Read` (nunca pregunta), `Low` (abrir algo), `Modify` (cambiar archivos), `Dangerous` (borrar, sobrescribir, PowerShell, apagar: siempre pregunta).
- `ToolPolicy` decide según los ajustes, lo aprobado «en esta sesión» y si en el turno ha entrado **contenido externo** (archivos, webs, capturas, salida de scripts): entonces todo lo que no sea lectura pide confirmación, para que un texto no pueda ordenar acciones.
- `PathPolicy` limita las rutas a las carpetas permitidas y prohíbe Windows, Archivos de programa, AppData y los datos de Carvis.
- `ActionJournal` registra cada acción con su forma de deshacerla (mover de vuelta, quitar lo creado, restaurar lo reemplazado desde la papelera).

## Añadir una herramienta

1. Crea una clase en `src/Carvis.Core/Tools/<Tema>/` que implemente `ITool` y márcala con `[BuiltInTool]` (se registra sola por reflexión). Sus dependencias llegan por el constructor.

```csharp
[BuiltInTool]
public sealed class DiceTool : ITool
{
    public string Name => "tirar_dado";                         // snake_case, en español
    public string Description => "Tira un dado de N caras.";   // el modelo decide con esto
    public string Category => "calculos";                       // categorías de ToolSelector
    public JsonObject Parameters { get; } = Object(Optional("caras", Integer("Número de caras (6 por defecto)")));

    public ToolPreview Preview(ToolArguments arguments, ToolContext context) =>
        new($"Tirar un dado de {arguments.Int("caras", 6, 2, 1000)} caras", ToolRisk.Read);

    public Task<ToolResult> ExecuteAsync(ToolArguments arguments, ToolContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(ToolResult.Ok($"Ha salido un {Random.Shared.Next(1, arguments.Int("caras", 6, 2, 1000) + 1)}."));
}
```

2. `Preview` valida los argumentos (lanza `ToolArgumentException` con un mensaje que el modelo entienda) y dice el riesgo. No debe tener efectos.
3. Añade frases típicas en `ToolKeywords.ByTool` (sin tildes, en minúsculas; `*` al final = cualquier terminación) para que el selector la ofrezca.
4. Si cambia algo, devuelve `JournalEntryId` desde `IActionJournal.Record*` para poder deshacerlo; si devuelve texto de fuera (archivos, webs), pon `ContainsExternalContent = true`.
5. Si depende de algo que puede no estar (Internet, Windows), implementa `IConditionalTool`.
6. Tests en `tests/Carvis.Tests` y, si quieres medir al modelo, un caso en `tests/Carvis.Evals/cases.json`.

## Plugins

Con **Ajustes → Permisos → Plugins** activado, Carvis carga al arrancar las DLL de `%AppData%\Carvis\plugins`. Cada clase pública que implemente `ITool` se registra; su constructor puede pedir servicios de Carvis (`IPathPolicy`, `IActionJournal`, `IShell`…). Para crear uno: una biblioteca `net8.0` que referencie `Carvis.Core.dll` (sin copiarla al directorio del plugin). Un plugin se ejecuta con los permisos del usuario: instala solo los que entiendas. Opcionalmente implementa `IHasKeywords` para dar frases al selector.

## Datos

- `%AppData%\Carvis\settings.json`: solo lo que difiere de `appsettings.json` (fusión con `System.Text.Json`; las listas se reemplazan). `SettingsValidator` corrige valores imposibles y `SettingsApplier` copia los cambios en caliente.
- `%LocalAppData%\Carvis\carvis.db` (SQLite, WAL, migraciones por `user_version`): conversaciones, mensajes, memoria, diario, documentos y trozos (`chunks_vec` con sqlite-vec y `chunks_fts` con FTS5), notas, tareas, recordatorios y rutinas. Los textos personales se cifran con DPAPI (`ContentProtector`).
- `%LocalAppData%\Carvis\models`: modelos de Whisper y Piper. `logs`, `trash` (papelera propia fuera de Windows) y `temp`.
- La app instalada vive en `%LocalAppData%\CarvisApp` (Velopack), separada de los datos.

## Documentos (RAG)

`DocumentIndexer` recorre las carpetas (incremental por tamaño/fecha y hash), `DocumentTextExtractor` elige el lector, `TextChunker` corta en trozos de ~1000 caracteres con solapamiento respetando párrafos, `OllamaEmbeddingService` calcula los vectores (con los prefijos de nomic) y `DocumentIndex` guarda y busca: vectores y palabras por separado, unidos con *reciprocal rank fusion* y un umbral de similitud. `RagContextProvider` añade los trozos relevantes numerados; las fuentes viajan a la UI.

## Voz

`VoiceAssistant` orquesta: `IAudioInput` (16 kHz mono) → `VoiceActivityDetector` (energía con suelo de ruido adaptativo, pre-roll de 300 ms) → `ISpeechToText` (Whisper; el modelo tiny para la palabra de activación) → `SpeechFilters` (quita alucinaciones típicas, detecta «Carvis») → evento `CommandHeard`. Para hablar, `SpeechSession` recibe el texto en streaming, `SpeechChunker` lo corta en frases limpias y `PiperTextToSpeech` (un proceso de Piper que se queda abierto) sintetiza la siguiente mientras suena la actual.

## Visión

`ScreenCaptureService` (App) oculta la ventana, captura con GDI (`ScreenGrabber`), deja elegir zona (`RegionSelectWindow`) y devuelve un PNG. Las imágenes van en `ChatInput.Images`; `ChatService` usa entonces `VisionModel` con `VisionKeepAlive` (0: se descarga al responder), recarga después el modelo de chat en segundo plano y no reenvía imágenes antiguas. Si `VisionModel` y `ChatModel` son el mismo modelo multimodal, no se descarga nada. Más detalles en [PERFORMANCE.md](PERFORMANCE.md). `ver_pantalla` hace lo mismo como herramienta mediante `IVisionService`.

## Publicación

- CI (`.github/workflows/ci.yml`, Windows): compila con avisos como errores, tests, formato y un instalador de prueba como artefacto.
- Release (`release.yml`): al subir una etiqueta `v*`, publica autocontenido para win-x64 con ReadyToRun (sin single-file: sqlite-vec y Whisper cargan DLL nativas del disco), empaqueta con `vpk` (Velopack: Setup.exe, paquete completo y deltas) y lo sube a GitHub Releases, de donde la app se actualiza.
