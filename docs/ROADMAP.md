# Carvis: hoja de ruta hasta la versión final

Esta lista recoge todo lo que le falta a Carvis para pasar de "chat con un modelo local" a un asistente profesional, instalable y fiable que actúe sobre el PC. Está ordenada por áreas. Cada punto lleva:

- **Prioridad**: `P0` imprescindible para que sea usable y seguro, `P1` necesario para la versión 1.0, `P2` mejora para después.
- **Tamaño** aproximado: `S` horas, `M` uno o dos días, `L` varios días.

Al final hay un orden de trabajo propuesto por versiones.

---

## 0. Dónde estamos

Ya funciona:

- [x] Ventana con chat en streaming, historial en memoria y formato Markdown
- [x] Alt+Espacio, icono en bandeja, barra de tareas, instancia única, "Iniciar con Windows"
- [x] Comprobación de Ollama y del modelo, precarga del modelo al arrancar
- [x] `appsettings.json`, tests del Core, contratos vacíos para las fases 2-5

Falta lo importante: **Carvis no puede hacer nada en el PC**. Solo habla. Y como el modelo no sabe que no puede, se inventa que ha hecho las cosas ("¡Carpeta creada con éxito!").

> **Estado (versión 1.0.0):** la lista está completa salvo los puntos que quedan sin marcar, cada uno con su motivo. El detalle de cada versión está en [CHANGELOG.md](../CHANGELOG.md).

---

## 1. Correcciones urgentes

- [x] `P0` `S` El prompt de sistema le prohíbe decir que ha hecho acciones que no ha hecho (corregido junto con esta hoja de ruta).
- [x] `P0` `S` **Ventana de contexto de Ollama.** Si no se indica `num_ctx`, Ollama usa una ventana pequeña (2048 o 4096 tokens, según la versión) y recorta la conversación sin avisar: Carvis "olvida" lo de antes. Añadir `Ollama.ContextLength` (por ejemplo 16384) y enviarlo en cada petición.
- [x] `P0` `S` **Legibilidad del fondo.** El fondo semitransparente deja ver el escritorio y cuesta leer. Opción `Window.Opacity` / fondo opaco por defecto, o efecto Mica/Acrylic real de Windows 11.
- [x] `P0` `S` **Logs en archivo** (`%LocalAppData%\Carvis\logs`, con rotación). Ahora mismo los errores solo salen en la ventana de depuración de Visual Studio, así que en tu PC no hay forma de saber qué ha fallado.
- [x] `P0` `S` **Captura de errores global** (`AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException`, errores del hilo de UI): registrar en el log y mostrar un aviso en vez de cerrarse.
- [x] `P0` `S` Fecha, hora, día de la semana y nombre del usuario en el contexto del modelo (ahora no sabe qué día es).
- [x] `P1` `S` Abrir la ventana en el monitor donde está el ratón (ahora usa el principal).
- [x] `P1` `S` Recordar posición de la ventana entre sesiones.

---

## 2. Motor de acciones (tool calling): lo que convierte el chat en asistente

Es la pieza central de la fase 3 y la que más falta hace.

### 2.1 Infraestructura

- [x] `P0` `L` **Bucle de agente**: el modelo pide una herramienta → Carvis valida los argumentos → pide confirmación si hace falta → ejecuta → devuelve el resultado al modelo → el modelo responde o pide otra herramienta. Con límite de pasos (por ejemplo 8) para evitar bucles.
- [x] `P0` `M` **Registro de herramientas** con nombre, descripción y JSON Schema de los argumentos, enviado a Ollama en `tools`. qwen3:8b soporta tool calling.
- [x] `P0` `M` **Validación de argumentos** antes de ejecutar (tipos, rutas existentes, valores permitidos). Si el modelo manda algo inválido, se le devuelve el error para que lo corrija.
- [x] `P0` `M` Streaming compatible con herramientas: mostrar texto mientras llega y detectar las llamadas a herramientas en el mismo flujo.
- [x] `P1` `M` Varias herramientas en una misma respuesta (por ejemplo "crea la carpeta y mueve estos 3 archivos").
- [x] `P1` `M` **Evaluaciones de tool calling**: batería de frases ("crea una carpeta X en el escritorio", "abre Spotify"...) con la herramienta y argumentos esperados, para medir si qwen3:8b acierta y comparar modelos o prompts. Con un modelo de 8B esto es lo que decide si funciona bien o no.
- [ ] `P2` `M` Router rápido: un modelo pequeño (por ejemplo qwen3:1.7b) decide si la petición necesita herramientas, RAG o solo charla, y así se ahorra tiempo. — *No hecho: lo resuelve el selector de herramientas (palabras clave, frases y embeddings) sin un segundo modelo.*

### 2.2 Confirmación y seguridad de las acciones

- [x] `P0` `M` **Tarjeta de confirmación en el chat**: qué va a hacer, con qué datos exactos ("Crear carpeta `C:\Users\Carvima\Desktop\MaikPedernal`"), botones **Aceptar** / **Cancelar**. Enter y Esc como atajos.
- [x] `P0` `S` **Niveles de riesgo**: lectura (sin confirmar), cambios reversibles (confirmar), destructivos o de sistema (confirmar con aviso rojo y escribir "sí" o doble clic). — *Hecho con aviso rojo y confirmación obligatoria, sin tener que escribir «sí».*
- [x] `P0` `M` **Carpetas permitidas y prohibidas**: por defecto solo carpetas del usuario (Escritorio, Documentos, Descargas, Imágenes...). Nunca `C:\Windows`, `Program Files`, carpetas de otros usuarios, ni la propia carpeta de Carvis.
- [x] `P0` `S` **Borrar = enviar a la papelera**, nunca borrado permanente.
- [x] `P1` `M` **Deshacer**: registro de cada operación de archivos (qué se movió, de dónde a dónde) y botón "Deshacer" en el chat.
- [x] `P1` `S` **Historial de acciones** consultable (qué hizo Carvis, cuándo y con qué resultado).
- [x] `P1` `S` "Permitir siempre esta acción en esta carpeta durante la sesión" para no confirmar diez veces seguidas.
- [x] `P1` `S` Simulación (dry-run): "esto es lo que haría" antes de operaciones masivas. — *La tarjeta de confirmación muestra el plan completo antes de ejecutar.*

---

## 3. Herramientas concretas

### 3.1 Archivos y carpetas (`P0` salvo indicación)

- [x] `S` Crear carpeta
- [x] `S` Crear archivo de texto con contenido
- [x] `S` Listar el contenido de una carpeta
- [x] `M` Buscar archivos por nombre, extensión, fecha o tamaño ("los PDF de la semana pasada en Descargas")
- [x] `S` Leer un archivo de texto o un documento (usa los lectores de la fase 2)
- [x] `S` Renombrar, mover y copiar (también en lote: "mueve todas las fotos a Imágenes/2026")
- [x] `S` Enviar a la papelera
- [x] `S` Abrir un archivo con su programa predeterminado y mostrarlo en el Explorador
- [x] `P1` `S` Comprimir y descomprimir ZIP
- [x] `P1` `M` Ordenar una carpeta automáticamente por tipo o fecha
- [x] `P2` `M` Buscar duplicados y archivos grandes

Rutas especiales: entender "escritorio", "descargas", "documentos" y resolverlas con `Environment.SpecialFolder` (y OneDrive si el escritorio está redirigido).

### 3.2 Programas y ventanas

- [x] `P0` `M` **Abrir programas por nombre**: índice de los accesos directos del menú Inicio (`.lnk`) y de las apps de la Store (`shell:AppsFolder`), con coincidencia aproximada ("abre el spoti" → Spotify).
- [x] `P0` `S` Abrir webs en el navegador predeterminado y buscar en Google/YouTube.
- [x] `P1` `S` Cerrar un programa (con confirmación) y listar los que están abiertos.
- [x] `P1` `M` Cambiar a una ventana abierta, minimizar o maximizar ventanas.
- [x] `P2` `M` Colocar ventanas (mitad izquierda/derecha, otro monitor).

### 3.3 Sistema

- [x] `P1` `S` Volumen (subir, bajar, silenciar) y controles multimedia (play/pausa, siguiente)
- [x] `P1` `S` Información del equipo: CPU, RAM, GPU, espacio en disco, batería, uptime
- [x] `P1` `S` Bloquear el PC, suspender, reiniciar y apagar (con doble confirmación y cuenta atrás cancelable)
- [x] `P1` `S` Portapapeles: leer y escribir ("traduce lo que tengo copiado")
- [x] `P1` `M` **Recordatorios, temporizadores y alarmas** con notificaciones nativas de Windows (siguen funcionando con la ventana cerrada)
- [x] `P2` `S` Brillo de pantalla, modo oscuro/claro de Windows, no molestar
- [x] `P2` `S` Estado de Wi-Fi y Bluetooth

### 3.4 Scripts y automatizaciones

- [x] `P1` `M` **Ejecutar PowerShell**: siempre mostrando el script completo antes de confirmar, con tiempo límite, salida capturada y mostrada en el chat, y sin privilegios de administrador.
- [x] `P1` `M` **Rutinas guardadas**: "modo estudio" = abrir VS Code + Spotify + silenciar notificaciones; se crean desde el chat y se lanzan por nombre.
- [x] `P2` `M` Tareas programadas ("cada lunes a las 9 ordena Descargas").

### 3.5 Utilidades sin riesgo (`P1` `S` cada una)

- [x] Calculadora exacta (los LLM se equivocan con las cuentas)
- [x] Conversión de unidades y divisas (las divisas necesitan Internet: opcional)
- [x] Fecha y hora, días entre fechas, zonas horarias
- [x] Notas rápidas y lista de tareas locales ("apunta que tengo que…")

### 3.6 Opcionales con Internet (desactivados por defecto) — `P2`

- [x] Búsqueda web local con SearXNG o similar
- [x] Tiempo meteorológico
- [ ] Calendario y correo (Outlook o Google): requieren cuentas y salen del modo 100 % local — *No hecho: necesita cuentas y sale del modo local.*

---

## 4. Documentos y RAG (fase 2)

- [x] `P0` `M` Pantalla para elegir las carpetas que se indexan, con exclusiones (por ejemplo `node_modules`, `.git`) y tamaño máximo por archivo.
- [x] `P0` `M` Lectores: PDF (PdfPig), DOCX (OpenXML), TXT, MD.
- [x] `P1` `M` Más formatos: XLSX, PPTX, CSV, HTML, código fuente.
- [x] `P1` `M` OCR para PDF escaneados e imágenes (Windows.Media.Ocr, que ya viene con Windows, o Tesseract).
- [x] `P0` `M` Troceado con solapamiento respetando párrafos y títulos; guardar página y sección de cada trozo.
- [x] `P0` `M` Embeddings con `nomic-embed-text` en lotes, con los prefijos `search_document:` / `search_query:` que pide ese modelo.
- [x] `P0` `M` SQLite + sqlite-vec para los vectores, más FTS5 para búsqueda por palabras.
- [x] `P1` `M` **Búsqueda híbrida** (vectores + palabras clave) y reordenado: mejora mucho los resultados con nombres propios y códigos.
- [x] `P0` `M` **Indexado incremental**: solo lo nuevo o modificado (fecha + hash), borrar lo eliminado, `FileSystemWatcher` para cambios en vivo.
- [x] `P0` `S` Progreso del indexado en la UI, en segundo plano, pausable y sin bloquear el chat.
- [x] `P0` `M` **Citas** en las respuestas: "según `Apuntes/Tema3.pdf`, página 12", clicables para abrir el archivo.
- [x] `P1` `S` Decidir cuándo usar RAG (no buscar en documentos para "hola").
- [x] `P1` `M` Arrastrar un archivo al chat para preguntar solo sobre él.
- [x] `P2` `M` Resumir un documento entero o compararlo con otro.

---

## 5. Memoria y conversaciones

- [x] `P0` `M` **Guardar las conversaciones** en SQLite: ahora se pierden al cerrar.
- [x] `P1` `M` Panel lateral con el historial: buscar, renombrar, borrar, fijar; título automático generado por el modelo.
- [x] `P1` `S` Exportar una conversación a Markdown o PDF.
- [x] `P1` `M` **Memoria a largo plazo**: datos del usuario que Carvis recuerda entre conversaciones ("me llamo Carlos", "estudio DAM", "mi carpeta de clase es…"), con una pantalla para verlos y borrarlos.
- [x] `P1` `M` Resumen automático de la parte antigua de una conversación larga para no salirse de la ventana de contexto.
- [x] `P1` `S` Migraciones del esquema de la base de datos (para actualizar sin perder datos).

---

## 6. Voz (fase 4)

- [x] `P1` `M` Entrada de voz con Whisper.net usando la GPU (CUDA), modelo `small` o `medium` en español. — *Con Vulkan en vez de CUDA: basta el driver de NVIDIA.*
- [x] `P1` `S` **Pulsar para hablar** (atajo configurable) como primer paso, antes de la palabra de activación.
- [x] `P1` `M` Detección de voz (VAD, por ejemplo Silero) para saber cuándo has terminado de hablar. — *Por energía con suelo de ruido adaptativo, sin modelo neuronal.*
- [x] `P1` `M` Salida de voz con Piper (voz en español, por ejemplo `es_ES-davefx`), leyendo por frases mientras llega la respuesta, sin esperar al final.
- [x] `P1` `S` Interrumpir a Carvis hablando por encima o pulsando una tecla.
- [x] `P2` `L` **Palabra de activación "Carvis"** entrenada en local (openWakeWord o similar), con control de falsos positivos y consumo bajo de CPU. — *Con Whisper tiny sobre los tramos de voz, sin entrenar un modelo propio.*
- [x] `P1` `S` Elegir micrófono y altavoces, indicador visual de "escuchando", y la voz desactivada por defecto.
- [x] `P2` `M` Modo conversación continua manos libres.

---

## 7. Visión y pantalla (fase 5)

- [x] `P1` `M` Captura con atajo: pantalla completa, ventana activa o región seleccionada con el ratón.
- [x] `P1` `M` Análisis con `qwen2.5vl` ("¿qué error sale aquí?", "resume esta página").
- [x] `P1` `S` Pegar o arrastrar imágenes al chat.
- [x] `P1` `S` Las capturas no se guardan en disco salvo que lo pidas.
- [x] `P2` `M` OCR de la pantalla para copiar texto de cualquier sitio.

---

## 8. Interfaz y experiencia de uso

- [x] `P0` `M` **Pantalla de ajustes** dentro de la app (modelo, atajo, carpetas, voz, permisos, modo de ventana), sin editar JSON a mano.
- [x] `P0` `M` **Primer arranque guiado**: comprueba Ollama (y ofrece descargarlo), descarga los modelos desde la app con barra de progreso (`PullModelAsync`), elige carpetas y prueba el atajo.
- [x] `P1` `S` Selector de modelo en la ventana, leyendo los modelos instalados en Ollama.
- [x] `P1` `S` Regenerar respuesta, editar y reenviar un mensaje.
- [x] `P1` `S` Comandos rápidos con `/`: `/nueva`, `/modelo`, `/buscar`, `/olvidar`.
- [x] `P1` `S` Mostrar el razonamiento de qwen3 plegado cuando `EnableThinking` está activo.
- [x] `P1` `S` Ventana redimensionable y tamaño de letra configurable.
- [x] `P1` `S` Resaltado de sintaxis en los bloques de código y enlaces clicables.
- [x] `P1` `S` Lista de mensajes virtualizada (conversaciones largas sin perder fluidez).
- [x] `P1` `S` Mensajes de error claros y con solución en todos los casos (sin GPU, sin memoria, modelo que no admite herramientas…).
- [x] `P1` `S` Notificaciones nativas de Windows (recordatorios, tareas largas terminadas).
- [x] `P2` `S` Tema claro, colores de acento, animaciones de apertura.
- [x] `P2` `M` Accesibilidad: lector de pantalla, navegación completa con teclado, contraste alto. — *Nombres accesibles, atajos de teclado y tema de alto contraste.*
- [x] `P2` `S` Indicador de velocidad (tokens/s) y de VRAM en uso.

---

## 9. Rendimiento y GPU (RTX 5070, 12 GB)

- [x] `P0` `S` Comprobar que Ollama usa la GPU (`ollama ps` debe decir 100 % GPU) y avisar en la app si está tirando de CPU.
- [x] `P1` `M` **Gestión de VRAM**: qwen3:8b (~5-6 GB) + nomic-embed-text (~0,3 GB) caben juntos, pero qwen2.5vl:7b (~6 GB) y Whisper medium (~2 GB) no caben todos a la vez. Cargar y descargar modelos según la tarea.
- [x] `P1` `S` Activar flash attention y caché KV cuantizada en Ollama para contextos largos.
- [x] `P1` `S` Opciones de generación configurables (temperatura, top_p) por tarea: baja para herramientas, normal para charla.
- [x] `P2` `M` Arranque más rápido: ReadyToRun, carga diferida de servicios.

---

## 10. Seguridad y privacidad

- [x] `P0` `M` **Inyección de instrucciones**: el texto de documentos, webs o capturas nunca puede lanzar acciones por sí solo. Marcarlo como contenido no fiable y exigir siempre confirmación.
- [x] `P0` `S` Todo local por defecto: comprobar que Ollama escucha solo en `localhost` y que Carvis no hace peticiones a Internet sin permiso.
- [x] `P1` `S` Los logs no guardan el contenido de las conversaciones salvo en modo depuración.
- [x] `P1` `M` Cifrar la base de datos de conversaciones y memoria con DPAPI (ligada a tu usuario de Windows).
- [x] `P1` `S` Botón "Borrar todos mis datos".
- [x] `P2` `S` Modo invitado o pantalla bloqueada: no responder a la palabra de activación con el PC bloqueado.

---

## 11. Calidad e ingeniería

- [x] `P0` `M` **CI en GitHub Actions** con Windows: compilar, tests y formato en cada push y pull request.
- [x] `P1` `M` Tests de los viewmodels y de la UI con Avalonia.Headless.
- [x] `P0` `M` Tests de cada herramienta en carpetas temporales (nunca sobre archivos reales).
- [x] `P1` `M` Tests de integración opcionales contra un Ollama real.
- [x] `P1` `S` Analizadores y `dotnet format` obligatorios; avisos tratados como errores.
- [x] `P1` `S` Reintentos con espera ante fallos puntuales de Ollama, y detectar si se cae para reconectar solo.
- [x] `P1` `S` **Configuración de usuario en `%AppData%\Carvis`**, separada del `appsettings.json` de fábrica, para que las actualizaciones no la pisen.
- [x] `P1` `S` Validar la configuración al arrancar y avisar de valores incorrectos.
- [x] `P1` `S` Versionado SemVer, `CHANGELOG.md`, plantillas de issues y PR.
- [x] `P2` `M` Sistema de plugins para añadir herramientas sin tocar el núcleo.

---

## 12. Instalación y distribución

- [x] `P0` `M` **Ejecutable publicado** autocontenido para win-x64: que no haga falta instalar .NET ni usar `dotnet run`. — *Carpeta autocontenida con ReadyToRun (no single-file: sqlite-vec y Whisper cargan DLL nativas).*
- [x] `P0` `M` **Instalador** con acceso directo en el menú Inicio, desinstalador y opción de iniciar con Windows (Velopack, que además trae actualizaciones automáticas, o Inno Setup).
- [x] `P1` `M` **Actualizaciones automáticas** desde GitHub Releases.
- [x] `P1` `S` Comprobar si Ollama está instalado y, si no, ofrecer instalarlo.
- [x] `P1` `S` Icono definitivo en todos los tamaños e información del ejecutable (versión, autor).
- [ ] `P2` `M` Firma de código para que Windows SmartScreen no lo bloquee (requiere un certificado, que cuesta dinero). — *No hecho: requiere comprar un certificado.*
- [ ] `P1` `S` Licencia del proyecto y licencias de terceros (Piper, Whisper y los modelos tienen las suyas). — *Terceros en THIRD-PARTY-NOTICES.md; falta que elijas la licencia del proyecto.*

---

## 13. Documentación

- [x] `P1` `S` Manual de usuario: qué puede hacer, ejemplos de frases, atajos, permisos.
- [x] `P1` `S` Documentación técnica: arquitectura, cómo añadir una herramienta, cómo depurar.
- [x] `P1` `S` Solución de problemas: Ollama no responde, atajo ocupado, GPU no usada, logs.

---

## Orden de trabajo propuesto

| Versión | Contenido | Resultado |
|---|---|---|
| **0.2** | Correcciones urgentes (1): contexto, logs, errores, fondo, fecha/hora | Base estable y depurable en tu PC |
| **0.3** | Motor de acciones + confirmación + permisos (2) y herramientas básicas de archivos y programas (3.1, 3.2) | Carvis crea carpetas de verdad, abre programas y webs |
| **0.4** | Conversaciones guardadas y memoria (5), ajustes y primer arranque (8), CI (11) | Uso diario cómodo |
| **0.5** | Documentos y RAG (4) | Pregunta sobre tus apuntes y archivos con citas |
| **0.6** | Sistema, recordatorios, scripts y rutinas (3.3-3.5) | Asistente completo de escritorio |
| **0.7** | Voz con pulsar para hablar y Piper (6) | Hablarle y que conteste en voz alta |
| **0.8** | Visión y capturas (7), gestión de VRAM (9) | "¿Qué hay en mi pantalla?" |
| **0.9** | Palabra de activación, seguridad completa (10), rendimiento | "Carvis, …" manos libres |
| **1.0** | Instalador, actualizaciones, documentación (12, 13) | Versión final instalable |

El salto más grande en utilidad es la **versión 0.3**: con ella Carvis deja de ser un chat y empieza a hacer cosas.
