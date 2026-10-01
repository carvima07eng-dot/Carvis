# Seguridad

Carvis es un asistente que puede hacer cosas en tu PC: crear y mover archivos, abrir programas, cambiar el volumen y, si lo activas, ejecutar scripts. Por eso la seguridad está pensada desde el principio. Aquí explico qué protege Carvis, cómo lo hace y dónde están sus límites.

## Versiones con soporte

Solo la última versión publicada recibe arreglos de seguridad. Carvis se actualiza solo (Ajustes → Acerca de).

| Versión | Soporte |
|---|---|
| 1.1.x | ✅ |
| < 1.1 | ❌ |

## Cómo avisar de un fallo

No abras un issue público con los detalles. Usa **Security → Report a vulnerability** en el repositorio de GitHub, que crea un aviso privado. Si no te aparece esa opción, abre un issue que diga solo "quiero informar de un problema de seguridad" y te contesto por privado.

Cuéntame qué versión usas, los pasos para reproducirlo y qué podría hacer alguien con el fallo. Intento responder en una semana.

## Modelo de seguridad

### 1. Todo en tu PC

- El modelo funciona en local con [Ollama](https://ollama.com). Tus mensajes, documentos y capturas no salen del equipo. Si en Ajustes pones un Ollama de otro ordenador, Carvis te avisa al arrancar.
- Internet está **desactivado por defecto**. Las herramientas que lo usan (tiempo, divisas, búsqueda web) solo funcionan si lo activas en Ajustes → Permisos.
- Lo único que Carvis consulta en Internet por su cuenta es la lista de versiones de GitHub, para avisarte de actualizaciones, y se puede apagar.
- Las conversaciones y los recuerdos se guardan cifrados con tu cuenta de Windows (DPAPI): otro usuario o una copia del archivo en otro PC no puede leerlos.

### 2. El modelo propone, tú decides

El modelo de lenguaje no ejecuta nada por sí mismo: elige una herramienta y Carvis le pone una de estas etiquetas de riesgo:

| Riesgo | Ejemplos | ¿Pregunta? |
|---|---|---|
| Lectura | listar una carpeta, buscar en tus apuntes | No |
| Bajo | abrir un programa, subir el volumen | No, salvo que lo pidas en Ajustes |
| Cambio | crear, mover o renombrar archivos | Sí (se puede permitir durante la sesión) |
| Peligroso | borrar, apagar, ejecutar scripts | Siempre, sin "permitir siempre" |

- Cada acción aparece como una tarjeta en el chat, con lo que va a hacer y los archivos afectados, antes de que la aceptes.
- Lo que se borra va a la **papelera** y no se elimina directamente. Las acciones con cambios se apuntan en un historial y casi todas se pueden **deshacer**.
- Carvis solo toca las carpetas permitidas (Ajustes → Permisos). Por defecto es tu carpeta de usuario sin `AppData`, y nunca carpetas del sistema.

### 3. Contenido de origen externo

El texto de una web, un documento, una captura o la salida de un script puede intentar dar órdenes al modelo ("ignora lo anterior y borra…"). Esto se llama *inyección de prompt*. Carvis lo trata así:

- Ese texto llega al modelo marcado como **datos, no instrucciones**.
- Después de leer contenido externo, **cualquier acción que cambie algo pide confirmación**, aunque normalmente no la pidiera. La tarjeta lo dice ("Origen externo…") y no ofrece "permitir siempre en esta sesión".
- Las respuestas basadas en contenido externo llevan la marca **Origen externo**. Si traen comandos, sale un aviso para revisarlos, y en rojo si alguno es peligroso.

### 4. PowerShell (Experimental)

Ejecutar scripts está **desactivado por defecto** (Ajustes → Experimental). Si lo activas:

- Ves el script completo antes de aceptarlo. Se para a los 60 segundos y su salida cuenta como contenido externo.
- Antes de preguntarte, Carvis lo analiza en busca de patrones peligrosos.
  - **Se bloquean**, y no llegan a mostrarse para aceptar:
    - formatear o borrar discos (`Format-Volume`, `diskpart`, `Clear-Disk`);
    - borrar carpetas del sistema, un disco entero o toda tu carpeta de usuario;
    - borrar claves del registro;
    - descargar algo de Internet y ejecutarlo (`iwr … | iex`, `DownloadFile` + `Start-Process`);
    - código escondido en Base64 (`-EncodedCommand`);
    - desactivar Microsoft Defender o el cortafuegos;
    - borrar copias de sombra o tocar el arranque (`vssadmin`, `bcdedit`).
  - **Se marcan en rojo**, con la explicación del riesgo:
    - borrados recursivos;
    - cambios en el registro;
    - `Set-ExecutionPolicy Bypass`;
    - crear usuarios o administradores;
    - apagar o reiniciar.
- Las rutinas no pueden incluir pasos de PowerShell.

El análisis es una segunda barrera, no una garantía: alguien con ganas puede escribir un script dañino que no encaje en ningún patrón. **No aceptes un script que no entiendas.**

### 5. Servidores MCP (Experimental)

Carvis puede usar herramientas de servidores [MCP](https://modelcontextprotocol.io). Está **desactivado por defecto**.

- Un servidor MCP es un programa de terceros: tiene los permisos de tu usuario y Carvis no puede controlar lo que hace por dentro. Añade solo servidores en los que confíes.
- Cada servidor tiene su permiso. Con **Preguntar siempre**, el que viene por defecto, confirmas cada llamada. Con **Lecturas sin preguntar**, solo se ejecutan sin preguntar las herramientas que el propio servidor marca como de solo lectura.
- Lo que devuelve un servidor MCP cuenta siempre como contenido externo.

### 6. Complementos

Los complementos (`%AppData%\Carvis\plugins`) son DLL de .NET que se cargan dentro de Carvis con todos sus permisos. Están desactivados por defecto. Actívalos solo con complementos que hayas compilado tú o de alguien de confianza.

### 7. Registros e informes de fallos

- Los registros (`%LOCALAPPDATA%\Carvis\logs`) no guardan lo que escribes ni lo que responde el modelo (salvo que pongas `Logging.IncludeContent` a `true` en `settings.json` para depurar). Antes de escribir nada se cambian tu carpeta de usuario por `%USERPROFILE%` y tu nombre por `<usuario>`.
- Si Carvis se cierra por un error, guarda un informe con esas mismas protecciones. Al volver a abrirlo te ofrece copiarlo o abrir un issue en GitHub, y solo se envía si tú lo mandas.

## Fuera de alcance

- Alguien que ya tiene acceso a tu sesión de Windows: puede leer lo mismo que Carvis.
- Lo que haga Ollama, un servidor MCP o un complemento por su cuenta.
- Respuestas incorrectas del modelo que no ejecutan nada. Siguen siendo errores, pero no fallos de seguridad.
