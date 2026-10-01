# Manual de Carvis

## Primer arranque

La primera vez se abre un asistente de seis pasos:

1. **Tu nombre**, para que Carvis te llame por él.
2. **Ollama**: comprueba que está instalado y en marcha. Si no, puedes abrir la descarga o instalarlo con winget.
3. **Modelos**: descarga `qwen3:8b` (chat, unos 5 GB) y `nomic-embed-text` (documentos). Se ve el progreso.
4. **Carpetas**: las que quieres que Carvis lea para responder sobre tus documentos (puedes saltarlo).
5. **Atajo**: pulsa Alt+Espacio para comprobar que funciona.
6. Listo.

Todo se puede cambiar después en **Ajustes** (rueda dentada, Ctrl+, o el icono de la bandeja).

## La ventana

- **Alt+Espacio** la muestra u oculta. Esc la minimiza (o la oculta en modo Spotlight).
- La caja de abajo es para escribir. **Enter** envía, **Shift+Enter** hace una línea nueva, **↑** recupera lo último que enviaste.
- Junto a la caja: 📎 adjuntar archivos, ⛶ captura de pantalla, 🎤 hablar (si la voz está activada).
- Abajo: el modelo (clic para cambiarlo), la velocidad de la última respuesta, la memoria de la gráfica en uso y el estado del indexado.
- Cada respuesta tiene **Copiar**, y la última **Regenerar**; tu último mensaje se puede **Editar**.
- El reloj de arriba abre el **historial**: buscar, fijar, renombrar, exportar a Markdown o borrar conversaciones. `/exportar` guarda la actual en Documentos\Carvis\Conversaciones.

## Acciones en el PC

Carvis usa herramientas para actuar. Cuando una acción cambia algo, aparece una tarjeta con lo que va a hacer:

- **Aceptar** (o Enter) la ejecuta; **Cancelar** (o Esc) la descarta.
- **Permitir en esta sesión** deja de preguntar por ese tipo de acción hasta que cierres Carvis.
- Borrar, sobrescribir, ejecutar PowerShell, reiniciar o apagar **siempre** piden confirmación, y salen en rojo.
- Casi todo se puede **deshacer**: botón en la tarjeta, «deshaz lo último» o `/acciones` para ver el historial. Lo borrado va a la papelera de Windows.

Por seguridad Carvis solo trabaja dentro de tu carpeta de usuario (o de las carpetas que elijas en Ajustes → Permisos) y nunca toca Windows, Archivos de programa ni AppData. Si un texto de un documento, una web o una captura le pide hacer algo, no lo hace sin preguntarte.

### Lo que sabe hacer

| Tema | Ejemplos |
|---|---|
| Archivos | «crea una carpeta Clase en el escritorio», «mueve los PDF de descargas a Documentos», «comprime la carpeta Fotos», «ordena descargas por tipo», «¿qué archivos ocupan más?», «busca duplicados en Imágenes» |
| Programas y ventanas | «abre Spotify», «cierra el Chrome», «pon Word a la izquierda», «¿qué tengo abierto?» |
| Web | «busca en YouTube tutoriales de Avalonia», «abre la web de Florida Universitària» |
| Sistema | «sube el volumen», «pausa la música», «¿cuánta batería queda?», «baja el brillo», «modo oscuro», «bloquea el PC», «apaga el ordenador en 30 minutos» / «cancela el apagado», «abre los ajustes del bluetooth» |
| Portapapeles | «traduce lo que he copiado», «cópiame esto» |
| Recordatorios | «recuérdame en 20 minutos sacar la ropa», «alarma todos los días a las 7:30», «pon un temporizador de 10 minutos», «¿qué recordatorios tengo?» |
| Rutinas | «crea una rutina modo estudio que abra VS Code, abra mis apuntes y ponga el volumen al 20», «activa el modo estudio», «cada lunes a las 9 ejecuta el modo estudio» |
| Notas y tareas | «apunta que la contraseña del wifi está en la pizarra», «añade la tarea entregar la práctica el viernes», «¿qué tengo pendiente?», «ya he hecho la tarea 3» |
| Cálculos | «¿cuánto es 1250 × 1,21?», «el 15 % de 80», «10 millas en km», «¿cuántos días faltan para el 25 de diciembre?», «¿qué hora es en Tokio?» |
| Memoria | «recuerda que estudio 2º de DAM», «¿qué sabes de mí?», «olvida lo del coche» |
| PowerShell | lo que no cubren las demás; siempre ves el script entero antes de aceptarlo |

Los recordatorios avisan con una notificación y un sonido aunque la ventana esté cerrada (Carvis tiene que estar en marcha en la bandeja). Si el PC estaba apagado, avisan al encenderlo, indicando que llegan tarde.

## Tus documentos

En **Ajustes → Documentos** eliges las carpetas. Carvis lee PDF, Word, Excel, PowerPoint, texto, Markdown, CSV, HTML y RTF; las imágenes y los PDF escaneados con OCR de Windows. Se actualiza solo cuando cambian los archivos.

Pregunta normalmente («¿qué dicen mis apuntes sobre el modelo OSI?»): la respuesta cita las fuentes [1], [2]… y debajo aparecen los archivos con la página; clic para abrirlos. También puedes **adjuntar** un archivo (📎 o arrastrándolo) para preguntar solo sobre él, y pedir «resume este documento» o «compara estos dos».

## Voz

1. **Ajustes → Voz y pantalla → Activar la voz**.
2. **Descargar modelos de voz** (Whisper para entender, Piper y una voz en español para hablar; entre 100 MB y 1,6 GB según la precisión elegida).
3. Prueba el micrófono y la voz con los botones.

Después pulsa **Ctrl+Alt+Espacio** (o el micrófono), habla y calla: Carvis sabe que has terminado por la pausa. Si preguntas hablando, contesta en voz alta. Para cortarle, vuelve a pulsar el atajo, Esc o **Parar**.

Opciones: **palabra de activación** («Carvis, abre Spotify»; nunca escucha con el PC bloqueado), **conversación continua** (después de contestar vuelve a escuchar) e **interrumpir hablando** (mejor con auriculares, si no se oye a sí mismo).

Todo el audio se procesa en tu PC. Whisper usa la gráfica mediante Vulkan (basta con el driver de NVIDIA).

## Pantalla e imágenes

- **Ctrl+Alt+S** o el botón ⛶ → **Elegir una zona**: arrastra con el ratón (Intro = pantalla completa, Esc = cancelar). También **Pantalla completa** o **Ventana activa**.
- **Ctrl+V** en la caja pega una imagen copiada; también puedes arrastrar imágenes.
- La imagen aparece encima de la caja; escribe la pregunta («¿qué significa este error?») o envíala tal cual.
- Hace falta el modelo de visión: `ollama pull qwen2.5vl:7b`. Tras usarlo se descarga de la gráfica en 2 minutos para dejar sitio al de chat.
- **Copiar el texto de una zona (OCR)**, en el mismo menú: eliges una zona y su texto pasa al portapapeles (útil con imágenes, vídeos o ventanas que no dejan copiar).
- Las capturas solo existen en memoria. Si quieres guardarlas, actívalo en Ajustes (se guardan en Imágenes\Carvis).

## Ajustes

| Pestaña | Qué hay |
|---|---|
| General | Nombre, atajo, tipo de ventana (normal o Spotlight), tema (oscuro, claro, como Windows o alto contraste) y color de acento, animaciones, fondo, tamaño de letra, inicio con Windows |
| Modelo | Dirección de Ollama, modelos, descargas, contexto, creatividad, razonamiento, cuánto tiempo se queda cargado, optimizar Ollama para la gráfica |
| Asistente | Instrucciones de Carvis, herramientas, historial |
| Voz y pantalla | Todo lo de la voz y las capturas |
| Permisos | Confirmaciones, carpetas permitidas, Internet, SearXNG, plugins |
| Documentos | Carpetas, exclusiones, tamaño máximo, indexar ahora, pausar, vaciar el índice |
| Memoria | Ver y borrar lo que recuerda |
| Privacidad | Cifrado, guardar conversaciones, actualizaciones, logs, **borrar todos mis datos** |

## Privacidad

- Las conversaciones, la memoria, las notas y los recordatorios se guardan cifrados con tu cuenta de Windows (DPAPI) en `%LocalAppData%\Carvis`.
- Carvis solo sale a Internet para: descargar modelos cuando lo pides, buscar actualizaciones en GitHub (desactivable) y las herramientas de Internet si las permites.
- Los logs (`%LocalAppData%\Carvis\logs`) no guardan lo que escribes salvo que actives el modo depuración.
- **Ajustes → Privacidad → Borrar todos mis datos** lo elimina todo.

## Desinstalar

Configuración de Windows → Aplicaciones → Carvis → Desinstalar. Tus datos se quedan en `%LocalAppData%\Carvis` y `%AppData%\Carvis` por si reinstalas; bórralos a mano (o con «Borrar todos mis datos» antes) si no los quieres.
