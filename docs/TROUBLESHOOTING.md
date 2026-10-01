# Solución de problemas

**Los logs** están en `%LocalAppData%\Carvis\logs` (uno por día). Para incluir el texto de los mensajes, activa en Ajustes → Privacidad el modo depuración y vuelve a probar.

## Ollama

| Síntoma | Solución |
|---|---|
| «Sin conexión» abajo | Abre Ollama (icono en la bandeja) o ejecuta `ollama serve`. Carvis vuelve a intentarlo solo cada 20 s, o pulsa **Reintentar**. |
| «Falta el modelo» | `ollama pull qwen3:8b` o Ajustes → Modelo → Descargar. |
| La primera respuesta tarda mucho | Es la carga del modelo en la gráfica. Sube «Mantener el modelo cargado» (por ejemplo `2h` o `-1`). |
| Va muy lento / aviso «no cabe en la gráfica» | Escribe `/gpu`. Si sale menos del 100 % en GPU: cierra juegos o programas que usen la gráfica, `/liberar`, baja la ventana de contexto, usa **Optimizar Ollama** (Ajustes → Modelo) y reinicia Ollama. Actualiza el driver de NVIDIA. |
| Responde pero no hace las acciones | Comprueba que el modelo admite herramientas (`qwen3` sí). Ajustes → Asistente → herramientas activadas. Sé concreto («crea una carpeta llamada X en el escritorio»). |
| Ollama está en otro PC | Cambia la dirección en Ajustes → Modelo. Carvis avisará de que tus conversaciones salen de este equipo. |

## Atajos

- **Alt+Espacio no hace nada**: otro programa lo usa (PowerToys Run, Flow Launcher…). Cambia el atajo en Ajustes → General (por ejemplo `Ctrl+Shift+Space`).
- Lo mismo para **Ctrl+Alt+Espacio** (hablar) y **Ctrl+Alt+S** (captura).
- Algunos programas ejecutados como administrador no dejan pasar atajos globales mientras están delante.

## Acciones

- **«Carvis no tiene permiso para esa carpeta»**: añádela en Ajustes → Permisos → Carpetas. AppData, Windows y Archivos de programa están siempre prohibidos.
- **No encuentra un programa**: di el nombre como sale en el menú Inicio. «¿Qué programas tengo instalados?» los lista.
- **Deshacer no puede**: si el archivo se cambió después a mano, Carvis no lo pisa. Mira `/acciones`.
- **PowerShell falla**: Carvis lo ejecuta sin privilegios de administrador y con 60 s de límite.

## Documentos

- **No encuentra nada**: mira el estado del índice en Ajustes → Documentos. Hace falta `nomic-embed-text` (`ollama pull nomic-embed-text`). Pulsa **Vaciar índice** e **Indexar ahora** si cambiaste el modelo.
- **PDF escaneados sin texto**: el OCR de Windows necesita el idioma español instalado (Configuración → Hora e idioma → Idioma → Español → Opciones → Reconocimiento óptico de caracteres).

## Voz

| Síntoma | Solución |
|---|---|
| «No encuentro ningún micrófono» | Conéctalo y comprueba Configuración de Windows → Privacidad → Micrófono (permitir a las apps de escritorio). |
| La barra de «Probar micrófono» no se mueve | Elige otro micrófono en Ajustes → Voz o sube su volumen en Windows. |
| No entiende bien | Usa el modelo `small` o `medium`, habla cerca del micro y sube la pausa (ms) si te corta. |
| Va lento entendiendo | Marca «Usar la tarjeta gráfica». Si tu driver no tiene Vulkan, se usa la CPU: elige `base` o `tiny`. |
| No habla | Descarga los modelos de voz otra vez y usa **Probar la voz**. Revisa los altavoces elegidos. |
| Se interrumpe solo | Desactiva «Cortar a Carvis si hablo mientras habla» o usa auriculares. |
| La palabra «Carvis» no funciona | Necesita el modelo tiny (se descarga con los demás si la opción está marcada). No funciona con el PC bloqueado, a propósito. |

## Pantalla

- **«Falta el modelo» al preguntar por una imagen**: `ollama pull qwen2.5vl:7b`.
- La captura sale negra en algunas apps protegidas (vídeo con DRM): es una limitación de Windows.
- En varios monitores, la captura es del monitor donde está el ratón.

## Instalación y actualizaciones

- **SmartScreen avisa**: el instalador no está firmado. «Más información» → «Ejecutar de todas formas».
- **No se actualiza**: Ajustes → Privacidad → «Buscar actualizaciones». Las versiones salen de GitHub Releases; si el repositorio es privado, la app no puede verlas.
- Para volver a empezar: Ajustes → Privacidad → «Restablecer los ajustes de fábrica» o «Borrar todos mis datos».
