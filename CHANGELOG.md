# Cambios

## 1.1.0 (pulido)

Una versión para que Carvis parezca y funcione como un producto de Windows 11.

**Diseño**
- Sistema de diseño en un solo archivo con colores por tema, espaciado de 4 px, radios, sombras y 5 tallas de letra. Un test impide colores o márgenes a mano en las vistas.
- Iconos Fluent UI System Icons, fondo Mica o Acrylic en Windows 11 (sólido en Windows 10) y el acento de Windows.
- Animaciones de 150–250 ms que respetan la opción de Windows de reducir animaciones.
- Ventana principal, Ajustes y primer arranque rediseñados en tema claro, oscuro y alto contraste.
- Orbe de voz que sigue el volumen real del micrófono y cambia al hablar.
- Estados vacíos con icono, frase y acción, y esqueletos de carga en lugar de pantallas quietas.
- Las acciones salen como tarjetas compactas con icono y resultado.
- Textos revisados: los errores dicen qué ha pasado y qué hacer.

**Foco**
- Nueva sección *Experimental* en Ajustes, todo apagado de fábrica: PowerShell, servidores MCP, complementos, escucha continua y modo razonamiento.

**Rendimiento**
- El registro apunta el tiempo de arranque, lo que tarda la ventana tras Alt+Espacio (aviso si pasa de 300 ms), el primer token y la VRAM de cada modelo.
- El modelo de chat se queda cargado (`KeepAlive -1`). El de visión se descarga al responder y el de chat se vuelve a cargar en segundo plano. Funciona también con un único modelo multimodal (Qwen3-VL); ver [PERFORMANCE.md](docs/PERFORMANCE.md).
- La búsqueda en documentos y en el historial ya no bloquea la interfaz.

**Fiabilidad**
- Registro con Serilog en archivos que rotan, sin rutas ni nombres. Botón en Ajustes para abrir la carpeta.
- Si Carvis se cierra por un error, al volver a abrirlo ofrece copiar el informe o crear un issue ya relleno.
- 80 frases de evaluación por categorías con informe en Markdown (`tests/Carvis.Evals`).
- Arreglado: una frase clave exacta («¿tengo instalado…?») no traía su herramienta si no coincidía también la categoría.

**Seguridad**
- PowerShell bloquea lo peligroso de verdad (formatear, borrar carpetas del sistema, descargar y ejecutar, desactivar Defender o el cortafuegos, código en Base64) y marca en rojo lo delicado, con la explicación.
- Las respuestas que salen de webs o documentos llevan la marca *Origen externo*, y sus comandos traen aviso. Después de leer contenido externo no se puede «permitir siempre».
- Nuevo [SECURITY.md](SECURITY.md).

**Integraciones**
- Cliente MCP (stdio y HTTP), experimental. Cada servidor tiene su permiso: *Preguntar siempre* o *Lecturas sin preguntar*. Su estado sale en Ajustes.

**Repositorio**
- Acciones de GitHub sin Node 20, licencia MIT, revisión de licencias de terceros, README nuevo con demo, CONTRIBUTING.md y formularios de issues.

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
