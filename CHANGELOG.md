# Cambios

## 1.0.0

Primera versión completa.

- Instalador para Windows (Velopack) con actualizaciones automáticas desde GitHub Releases; publicación autocontenida con ReadyToRun.
- Workflow de publicación por etiqueta y un instalador de prueba en cada build de la CI.
- Exportar conversaciones a Markdown (historial o `/exportar`).
- Copiar el texto de una zona de la pantalla con el OCR de Windows.
- Tema de alto contraste; opción de plugins en Ajustes → Permisos.
- Documentación: manual, arquitectura, solución de problemas, avisos de terceros.

## 0.9.0 (voz, visión, GPU)

- Voz opcional: Whisper.net en la gráfica (Vulkan) o en la CPU, detección de voz, Piper leyendo la respuesta frase a frase, atajo para hablar, interrupción, conversación continua y palabra de activación «Carvis» (nunca con el PC bloqueado). Elección de micrófono y altavoces, pruebas y descarga de modelos desde Ajustes.
- Capturas de zona, pantalla o ventana; pegar o arrastrar imágenes; análisis con `qwen2.5vl`, que se descarga de la VRAM a los 2 minutos. Las capturas no se guardan salvo que lo pidas. Herramienta `ver_pantalla`.
- Aviso si el modelo no cabe en la gráfica, VRAM en el pie, `/gpu` y `/liberar`, flash attention y caché KV de 8 bits.
- El texto de las conversaciones solo va al log en modo depuración; aviso si Ollama no está en este PC.
- Tema claro y «como Windows», colores de acento, animación de apertura, atajos de teclado y nombres accesibles.

## 0.6.0 (asistente de escritorio)

- Sistema: volumen, multimedia, información del equipo, bloquear/suspender/reiniciar/apagar con cuenta atrás, portapapeles, brillo, modo oscuro, páginas de Configuración de Windows.
- Recordatorios, temporizadores y alarmas repetidas, que pueden lanzar una rutina.
- Rutinas guardadas, PowerShell con confirmación, calculadora, unidades, fechas, notas y tareas.
- Tiempo, divisas y búsqueda web (SearXNG), desactivados hasta que permitas Internet.

## 0.5.0 (documentos)

- Lectores de PDF, Word, Excel, PowerPoint, texto y OCR; trozos con página y sección; búsqueda híbrida (sqlite-vec + FTS5); indexado incremental y en vivo; fuentes citadas y clicables; adjuntar archivos.

## 0.4.0 (conversaciones y ajustes)

- Conversaciones guardadas y cifradas con historial, memoria, regenerar/editar, comandos `/`, ventana de ajustes, primer arranque guiado, CI.

## 0.3.0 (acciones)

- Motor de herramientas con confirmación, permisos por carpeta, diario de acciones con deshacer; archivos, programas, ventanas y web.

## 0.2.0 y 0.1.0

- Base: ventana flotante con streaming, atajo global, bandeja, instancia única, ajustes por usuario, logs.
