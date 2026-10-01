# Software de terceros

Carvis incluye o descarga los siguientes componentes. Cada uno conserva su licencia; los textos completos están en sus repositorios.

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
| [SharpHook](https://github.com/TolikPylypchuk/SharpHook) / libuiohook | 8.0.0 | MIT / GPL-3.0 con excepción de enlace* |
| [Markdig](https://github.com/xoofx/markdig) | 1.4.0 | BSD-2-Clause |
| [PdfPig](https://github.com/UglyToad/PdfPig) | 0.1.16 | Apache-2.0 |
| [DocumentFormat.OpenXml](https://github.com/dotnet/Open-XML-SDK) | 3.5.1 | MIT |
| [Whisper.net](https://github.com/sandrohanea/whisper.net) (+ runtimes CPU y Vulkan, basados en whisper.cpp/ggml) | 1.9.1 | MIT |
| [NAudio](https://github.com/naudio/NAudio) (Core y WinMM) | 2.2.1 | MIT |
| [Velopack](https://github.com/velopack/velopack) | 1.2.161 | MIT |

\* SharpHook distribuye libuiohook; consulta su licencia en el repositorio de SharpHook.

## Descargados aparte (solo si lo pides)

| Componente | Licencia |
|---|---|
| [Ollama](https://github.com/ollama/ollama) (lo instalas tú) | MIT |
| Modelo `qwen3:8b` (Alibaba Qwen) | Apache-2.0 |
| Modelo `qwen2.5vl:7b` (Alibaba Qwen) | Apache-2.0 |
| Modelo `nomic-embed-text` (Nomic AI) | Apache-2.0 |
| Modelos de [Whisper](https://github.com/openai/whisper) en formato ggml ([whisper.cpp](https://github.com/ggerganov/whisper.cpp)) | MIT |
| [Piper](https://github.com/rhasspy/piper) (ejecutable, espeak-ng y onnxruntime incluidos) | MIT (espeak-ng: GPL-3.0; onnxruntime: MIT) |
| Voces de [piper-voices](https://huggingface.co/rhasspy/piper-voices) | La de cada voz, indicada en su `MODEL_CARD` |

## Solo para desarrollo (no se distribuyen)

xUnit (Apache-2.0), Microsoft.NET.Test.Sdk (MIT), Avalonia.Headless.XUnit (MIT).
