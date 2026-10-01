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

Cuentas para tu gráfica de **12 GB** (Windows y el escritorio ya usan unos 0,5–1 GB):

- **Ahora (dos modelos):** el chat ocupa ≈ 5,2 + 2,4 ≈ 7,6 GB y la visión ≈ 6 GB más, así que juntos se van a ~13,6 GB y no caben a la vez. Con cada captura Ollama tiene que sacar el chat, cargar la visión y, al terminar, volver a cargar el chat. Son varios segundos de disco a VRAM cada vez, y se ven en `load … ms`. Carvis ya lo lleva lo mejor posible: descarga la visión nada más responder y recarga el chat en segundo plano.
- **Con `qwen3-vl:8b` para todo:** ≈ 6,1 + 2,4 ≈ 8,5 GB, siempre el mismo modelo. Cabe entero en 12 GB con margen (unos 2,5 GB libres), las capturas no cambian nada de lo que hay cargado, y se pueden usar herramientas en el mismo turno que la imagen (por ejemplo, "mira este error y apúntalo en mis tareas"). En gráficas de 8 GB iría justo; ahí ayudaría `OLLAMA_FLASH_ATTENTION=1` con `OLLAMA_KV_CACHE_TYPE=q8_0`, que deja la caché en ~1,2 GB.

**Mi recomendación para tu PC:** pasar a `qwen3-vl:8b` como modelo único, pero solo después de comprobar que con las herramientas acierta igual que `qwen3:8b`. Es lo único que no puedo medir aquí, y si una captura va más rápida pero falla en "pon un recordatorio", no compensa. En la v1.1 no cambio el modelo predeterminado. Carvis ya está preparado: si pones el mismo nombre en *Modelo de chat* y *Modelo de visión*, nunca descarga el modelo después de una imagen y mantiene las herramientas (hay test). Para decidirlo:

1. `ollama pull qwen3-vl:8b` (necesita una versión reciente de Ollama).
2. `dotnet run --project tests/Carvis.Evals -- --model qwen3:8b --report qwen3.md` y lo mismo con `--model qwen3-vl:8b --report qwen3-vl.md`.
3. Si `qwen3-vl` saca un porcentaje igual o mayor en casi todas las categorías y `Perf: … uses … GB` dice 100 % en la GPU, ponlo en los dos campos de Ajustes → Modelo. Si pierde más de un 5 % en alguna categoría importante (recordatorios, documentos, música), quédate con dos modelos.

Fuente de los tamaños: [biblioteca de Ollama, qwen3-vl](https://ollama.com/library/qwen3-vl/tags).
