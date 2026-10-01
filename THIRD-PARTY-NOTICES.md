# Software de terceros

Carvis se distribuye con licencia MIT (ver [LICENSE](LICENSE)). Incluye o descarga los siguientes componentes; cada uno conserva su licencia y los textos completos están en sus repositorios.

## Compatibilidad

- Todo lo que va **dentro del instalador** es MIT, BSD, Apache-2.0, OFL o dominio público, salvo **libuiohook** (dentro de SharpHook), que es **LGPL-3.0**. Es compatible: se distribuye como una DLL aparte (`uiohook.dll`) que se puede sustituir, y el código de Carvis es público.
- **Piper, espeak-ng y las voces no se distribuyen**: los descarga el usuario desde Ajustes y Piper se ejecuta como un programa aparte. Por eso su licencia (Piper ahora es GPL-3.0 en su nuevo repositorio; espeak-ng es GPL-3.0) no afecta a la de Carvis.
- **Voces**: `es_ES-davefx-medium` (CC0) y `es_ES-sharvard-medium` (CC BY 3.0, pide atribución) están afinadas a partir de la voz inglesa *lessac*, cuyo conjunto de datos (Blizzard 2013) solo permite investigación y uso no comercial. Para uso personal no hay problema; **para un uso comercial no se deberían usar**. `es_MX-claude-high` declara Apache-2.0.

## Incluidos en el instalador

| Componente | Versión | Licencia |
|---|---|---|
| [.NET](https://github.com/dotnet/runtime) (runtime autocontenido) | 8.0 | MIT |
| [Avalonia](https://github.com/AvaloniaUI/Avalonia) (+ Desktop, Fluent, Skia, HarfBuzz) | 11.3.22 | MIT |
| [Inter](https://github.com/rsms/inter) (fuente, vía Avalonia.Fonts.Inter) | 4.x | SIL Open Font License 1.1 |
| [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) | 8.4.2 | MIT |
| [OllamaSharp](https://github.com/awaescher/OllamaSharp) | 5.5.0 | MIT |
| [Microsoft.Extensions.*](https://github.com/dotnet/runtime) (DI, Logging, AI abstractions) | 10.0.12 | MIT |
| [Microsoft.Data.Sqlite](https://github.com/dotnet/efcore) / [SQLitePCLRaw](https://github.com/ericsink/SQLitePCL.raw) | 10.0.12 | MIT / Apache-2.0 |
| [SQLite](https://sqlite.org) | 3.x | Dominio público |
| [sqlite-vec](https://github.com/asg017/sqlite-vec) | 0.1.7-alpha.2.1 | MIT o Apache-2.0 |
| [System.Security.Cryptography.ProtectedData](https://github.com/dotnet/runtime) | 10.0.12 | MIT |
| [SharpHook](https://github.com/TolikPylypchuk/SharpHook) / libuiohook | 8.0.0 | MIT / LGPL-3.0* |
| [Markdig](https://github.com/xoofx/markdig) | 1.4.0 | BSD-2-Clause |
| [PdfPig](https://github.com/UglyToad/PdfPig) | 0.1.16 | Apache-2.0 |
| [DocumentFormat.OpenXml](https://github.com/dotnet/Open-XML-SDK) | 3.5.1 | MIT |
| [Whisper.net](https://github.com/sandrohanea/whisper.net) (+ runtimes CPU y Vulkan, basados en whisper.cpp/ggml) | 1.9.1 | MIT |
| [NAudio](https://github.com/naudio/NAudio) (Core y WinMM) | 2.2.1 | MIT |
| [Velopack](https://github.com/velopack/velopack) | 1.2.161 | MIT |
| [FluentAvaloniaUI](https://github.com/amwx/FluentAvalonia) | 2.x | MIT |
| [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) (iconos) | — | MIT |
| [Serilog](https://github.com/serilog/serilog) (+ Extensions.Logging, Sinks.File) | 4.x | Apache-2.0 |
| [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) | — | MIT |

\* SharpHook es MIT y distribuye libuiohook 1.3, que es LGPL-3.0.

## Descargados aparte (solo si lo pides)

| Componente | Licencia |
|---|---|
| [Ollama](https://github.com/ollama/ollama) (lo instalas tú) | MIT |
| Modelo `qwen3:8b` (Alibaba Qwen) | Apache-2.0 |
| Modelo `qwen2.5vl:7b` (Alibaba Qwen) | Apache-2.0 |
| Modelo `nomic-embed-text` (Nomic AI) | Apache-2.0 |
| Modelos de [Whisper](https://github.com/openai/whisper) en formato ggml ([whisper.cpp](https://github.com/ggerganov/whisper.cpp)) | MIT |
| [Piper](https://github.com/rhasspy/piper) (ejecutable, espeak-ng y onnxruntime incluidos) | MIT (espeak-ng: GPL-3.0; onnxruntime: MIT) |
| Voces de [piper-voices](https://huggingface.co/rhasspy/piper-voices) | La de cada voz (`MODEL_CARD`); ver la nota de compatibilidad sobre *lessac* |

## Solo para desarrollo (no se distribuyen)

xUnit (Apache-2.0), Microsoft.NET.Test.Sdk (MIT), Avalonia.Headless.XUnit (MIT).
