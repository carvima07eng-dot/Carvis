# Rendimiento

## Qué se mide

Carvis escribe estas líneas en el registro (`%LOCALAPPDATA%\Carvis\logs`). Empiezan por `Perf:` y solo llevan números, nunca lo que escribes:

| Línea | Qué mide |
|---|---|
| `window visible … ms after the process started` | Arranque: desde que se lanza el proceso hasta que la ventana se dibuja. Si arranca oculto, aparece `ready in the tray` |
| `window visible … ms after the hotkey` | Desde que pulsas Alt+Espacio hasta que la ventana está en pantalla. Pasa a `WRN` si tarda más de 300 ms |
| `first token after … ms (load …, prompt …)` | Tiempo hasta la primera palabra de cada respuesta, cuánto tardó Ollama en cargar el modelo y en leer el prompt, y la velocidad en tokens/s |
| `uses … GB of VRAM (… % on the GPU)` | Memoria de la gráfica que ocupa cada modelo cargado |

Para ver solo estas líneas en PowerShell:

```powershell
Select-String -Path "$env:LOCALAPPDATA\Carvis\logs\*.log" -Pattern "Perf:"
```

## Resultados en el entorno de pruebas

Se midió en Linux con Xvfb (sin gráfica, el dibujo lo hace la CPU) y un Ollama simulado. Así que vale para la parte de la interfaz, no para el modelo:

| Métrica | Debug | Release (ReadyToRun) | Objetivo |
|---|---|---|---|
| Arranque hasta ventana visible | 2,0–3,7 s | 1,55–1,65 s | — |
| Alt+Espacio → ventana visible | 5–15 ms | 2–27 ms | < 300 ms |

La ventana se crea al arrancar y luego solo se muestra o se oculta, por eso el atajo va tan rápido. En Windows real hay que sumar el gancho de teclado del sistema (unos pocos ms). El tiempo hasta el primer token y la VRAM dependen de tu gráfica y tienen que medirse en tu PC.

## Cómo se gestiona la gráfica

- **Modelo de chat siempre cargado.** `Ollama.KeepAlive` vale `-1` por defecto, así que la primera respuesta no espera a que se cargue el modelo. Si necesitas la gráfica para otra cosa (un juego, por ejemplo), escribe `/liberar` o pulsa *Liberar memoria*. También puedes poner `30m` en los ajustes.
- **Modelo de visión bajo demanda.** Solo se carga cuando mandas una imagen o una captura. Como `Ollama.VisionKeepAlive` vale `0`, Ollama lo descarga justo después de responder. Luego Carvis vuelve a cargar el modelo de chat en segundo plano para que la siguiente pregunta no tenga que esperar.
- **Nada pesado en el hilo de la interfaz.** La indexación va en segundo plano, igual que la búsqueda en documentos (vectores + palabras clave en SQLite) y la búsqueda del historial, que además espera a que dejes de escribir 150 ms.

## ¿Un solo modelo multimodal (Qwen3-VL)?

Tamaños en la biblioteca de Ollama, con la cuantización por defecto Q4_K_M:

| Modelo | Tamaño | Herramientas | Visión | Contexto máx. |
|---|---|---|---|---|
| `qwen3:8b` (chat actual) | 5,2 GB | sí | no | 40K |
| `qwen2.5vl:7b` (visión actual) | 6,0 GB | no | sí | 125K |
| `qwen3-vl:8b` | 6,1 GB | sí | sí | 256K |

Con el contexto de 16K que usa Carvis, la caché KV de un modelo de 8B en fp16 ocupa unos 2,4 GB (36 capas × 8 cabezas KV × 128 × 2 × 2 bytes × 16 384 tokens). Las cuentas de VRAM salen así:

- **Ahora (dos modelos):** chat ≈ 5,2 + 2,4 ≈ 7,6 GB. Visión ≈ 6 GB más. En una gráfica de 8 GB no caben los dos, así que cada captura obliga a cambiar de modelo: se descarga el chat, se carga la visión y luego se vuelve a cargar el chat. Eso son varios segundos de disco a VRAM cada vez, que se ven en `load … ms`.
- **Con `qwen3-vl:8b` para todo:** ≈ 6,1 + 2,4 ≈ 8,5 GB, siempre el mismo modelo. Las capturas no cambian nada de lo que hay cargado. En 8 GB va justo: puede quedar algo en la CPU. Se puede arreglar activando `OLLAMA_FLASH_ATTENTION=1` y `OLLAMA_KV_CACHE_TYPE=q8_0` (la caché baja a ~1,2 GB) o bajando el contexto a 8K.

**Mi recomendación:** no cambiar el modelo por defecto en la v1.1, pero dejarlo preparado. Carvis ya funciona con un solo modelo: si pones el mismo nombre en *Modelo de chat* y *Modelo de visión*, nunca lo descarga después de una imagen y mantiene las herramientas activas (hay test). Antes de hacerlo predeterminado:

1. `ollama pull qwen3-vl:8b` (necesita una versión reciente de Ollama).
2. `dotnet run --project tests/Carvis.Evals -- --model qwen3-vl:8b` y compararlo con `--model qwen3:8b`, mirando el porcentaje por categoría.
3. Si acierta igual o más con las herramientas y `Perf: … uses … GB` queda al 100 % en la GPU, cambiar el predeterminado. Para gráficas de 12 GB o más es casi seguro que compensa.

Fuente de los tamaños: [biblioteca de Ollama, qwen3-vl](https://ollama.com/library/qwen3-vl/tags).
