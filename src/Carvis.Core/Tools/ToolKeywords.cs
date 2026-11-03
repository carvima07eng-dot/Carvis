namespace Carvis.Core.Tools;

/// <summary>Optional: a tool (e.g. from a plugin) can tell the selector which phrases point to it.</summary>
public interface IHasKeywords
{
    IReadOnlyList<string> Keywords { get; }
}

/// <summary>Phrases that strongly suggest a built-in tool. Written without accents, lowercase.</summary>
internal static class ToolKeywords
{
    public static readonly Dictionary<string, string[]> ByTool = new()
    {
        ["crear_carpeta"] = ["crea una carpeta", "crear carpeta", "crear una carpeta", "nueva carpeta", "haz una carpeta", "creame una carpeta", "carpeta nueva", "carpeta llamada", "carpeta con el nombre", "una carpeta"],
        ["crear_archivo"] = ["crea un archivo", "archivo de texto", "nuevo archivo", "escribe un archivo", "guarda en un archivo", "crea un txt", ".txt", ".md", "documento de texto", "guardalo en", "crea una lista en"],
        ["listar_carpeta"] = ["que hay en", "contenido de", "listar", "lista los", "lista lo", "muestrame la carpeta", "que tengo en", "ver la carpeta", "que archivos hay"],
        ["buscar_archivos"] = ["busca", "buscar", "encuentra", "encuentrame", "donde esta", "donde tengo", "localiza"],
        ["leer_archivo"] = ["lee", "leer", "resume", "resumen", "que dice", "contenido del archivo", "de que va"],
        ["mover"] = ["mueve", "mover", "pasa el", "pasa los", "lleva", "traslada", "muevelo", "muevelos"],
        ["copiar"] = ["copia", "copiar", "haz una copia", "duplica"],
        ["renombrar"] = ["renombra", "cambia el nombre", "cambiale el nombre", "llamalo", "ponle de nombre", "renombrar", "ponle el nombre"],
        ["enviar_a_papelera"] = ["borra", "elimina", "papelera", "suprime", "borrar", "eliminar", "quitalo", "tira"],
        ["abrir_archivo"] = ["abre el archivo", "abre la carpeta", "abre el pdf", "abre el documento", "abrir archivo", "abreme el archivo", "abre la foto", "abre el video"],
        ["mostrar_en_explorador"] = ["explorador", "muestrame donde", "ensename donde", "en el explorador"],
        ["comprimir"] = ["comprime", "comprimir", "zip", "haz un zip", "empaqueta"],
        ["descomprimir"] = ["descomprime", "descomprimir", "extrae", "unzip", "descomprimelo"],
        ["organizar_carpeta"] = ["ordena", "organiza", "ordenar", "organizar", "limpia la carpeta", "pon orden"],
        ["buscar_duplicados"] = ["duplicad*", "repetid*", "iguales", "archivos dobles"],
        ["archivos_grandes"] = ["ocupa", "ocupan", "grandes", "pesan", "mas grande", "mas pesados", "liberar espacio"],
        ["deshacer"] = ["deshaz", "deshacer", "revierte", "vuelve atras", "como estaba", "anula lo"],
        ["historial_acciones"] = ["que has hecho", "historial", "acciones que", "lo que hiciste"],
        ["abrir_programa"] = ["abre", "abrir", "lanza", "inicia", "ejecuta", "arranca", "abreme", "pon el", "pon spotify"],
        ["programas_instalados"] = ["instalados", "que programas tengo", "tengo instalado", "esta instalado"],
        ["programas_abiertos"] = ["abiertos", "tengo abierto", "que hay abierto", "ventanas abiertas", "programas abiertos"],
        ["ventana"] = ["ventana", "minimiza", "maximiza", "izquierda", "derecha", "al frente", "otro monitor", "otra pantalla", "restaura", "cambia a"],
        ["cerrar_programa"] = ["cierra", "cerrar", "mata", "termina el", "quita el programa", "cierrame"],
        ["abrir_web"] = ["web", "pagina", "url", "http", "www", ".com", ".es", ".org", "abre youtube", "abre google", "abre gmail"],
        ["recordar"] = ["recuerda que", "acuerdate de que", "me llamo", "mi nombre es", "guarda que", "apuntate que", "no olvides que"],
        ["olvidar"] = ["olvida", "borra de tu memoria", "ya no recuerdes"],
        ["listar_recuerdos"] = ["que sabes de mi", "que recuerdas", "que tienes guardado", "tu memoria"],
        ["buscar_en_documentos"] = ["mis apuntes", "mis documentos", "en mis archivos", "segun mis", "busca en mis", "que dicen mis", "en el temario"],
        ["indexar_documentos"] = ["indexa", "indexar", "actualiza el indice", "lee mis documentos", "vuelve a leer"],
        ["resumir_documento"] = ["resume el documento", "resumeme", "hazme un resumen", "resumen de", "de que trata"],
        ["comparar_documentos"] = ["compara", "comparar", "diferencias entre", "en que se parecen"],
        ["volumen"] = ["volumen", "sube el volumen", "baja el volumen", "silencia", "quita el sonido", "pon el sonido", "mas alto", "mas bajo", "mutea", "sin sonido"],
        ["multimedia"] = ["pausa", "pausa la musica", "siguiente cancion", "cancion anterior", "pasa de cancion", "reanuda", "para la musica", "play", "la musica"],
        ["info_sistema"] = ["bateria", "cuanta ram", "memoria ram", "procesador", "grafica", "cpu", "gpu", "vram", "espacio libre", "espacio en disco", "mi pc", "mi ordenador", "cuanto tiempo lleva encendido", "estoy conectado", "conexion"],
        ["energia"] = ["apaga el pc", "apaga el ordenador", "reinicia", "suspende", "bloquea", "bloquear", "apagar", "cancela el apagado", "hibernar"],
        ["portapapeles"] = ["portapapeles", "lo que he copiado", "lo copiado", "copialo", "copia al portapapeles", "texto copiado"],
        ["brillo"] = ["brillo", "sube el brillo", "baja el brillo", "pantalla mas clara", "pantalla mas oscura"],
        ["tema_windows"] = ["modo oscuro", "modo claro", "tema oscuro", "tema claro"],
        ["ajustes_windows"] = ["wifi", "wi-fi", "bluetooth", "configuracion de windows", "ajustes de windows", "actualizaciones", "no molestar", "apps de inicio", "impresora*"],
        ["crear_recordatorio"] = ["recuerdame", "recordatorio", "avisame", "alarma", "despiertame", "a las", "manana a", "cada dia", "todos los dias", "cada lunes", "programa"],
        ["temporizador"] = ["temporizador", "cuenta atras", "minutos", "segundos", "timer"],
        ["listar_recordatorios"] = ["que recordatorios", "mis recordatorios", "mis alarmas", "que tengo programado", "recordatorios pendientes"],
        ["borrar_recordatorio"] = ["cancela el recordatorio", "borra el recordatorio", "quita la alarma", "cancela la alarma", "cancela el temporizador"],
        ["crear_rutina"] = ["crea una rutina", "nueva rutina", "guarda una rutina", "rutina llamada", "modo estudio", "modo gaming", "automatiza"],
        ["ejecutar_rutina"] = ["ejecuta la rutina", "lanza la rutina", "activa el modo", "modo estudio", "modo gaming", "rutina", "pon el modo"],
        ["listar_rutinas"] = ["que rutinas", "mis rutinas", "lista las rutinas"],
        ["borrar_rutina"] = ["borra la rutina", "elimina la rutina"],
        ["ejecutar_powershell"] = ["powershell", "script", "comando", "terminal", "consola", "cmd"],
        ["calcular"] = ["calcula", "cuanto es", "cuanto son", "cuanto da", "porcentaje", "%", "raiz", "multiplica", "divide", "suma", "resta", "elevado"],
        ["convertir_unidades"] = ["convierte", "pasa a", "en km", "en millas", "en metros", "en kilos", "en libras", "grados", "fahrenheit", "celsius", "cuantos gb", "cuantos mb", "pulgadas"],
        ["fecha_hora"] = ["que hora es en", "hora en", "dias faltan", "cuantos dias", "dias entre", "que dia cae", "que dia de la semana", "dentro de", "dias hasta"],
        ["crear_nota"] = ["apunta", "anota", "toma nota", "nota", "apuntame"],
        ["listar_notas"] = ["mis notas", "que notas", "las notas", "lo que apunte"],
        ["borrar_nota"] = ["borra la nota", "elimina la nota"],
        ["crear_tarea"] = ["tarea", "pendiente", "anade a la lista", "tengo que", "por hacer", "to do"],
        ["listar_tareas"] = ["mis tareas", "que tareas", "que tengo pendiente", "lista de tareas", "que me queda por hacer"],
        ["completar_tarea"] = ["ya he hecho", "marca como hecha", "completada", "tarea hecha", "he terminado"],
        ["borrar_tarea"] = ["borra la tarea", "elimina la tarea", "quita la tarea"],
        ["tiempo"] = ["el tiempo", "que tiempo", "tiempo hace", "va a llover", "llueve", "lloverá", "temperatura", "clima", "prevision", "hace frio", "hace calor"],
        ["divisas"] = ["euros", "dolares", "libras", "yenes", "cambio de", "divisa*", "moneda*", "en dolares", "en euros"],
        ["leer_resultados_web"] = ["busca en internet", "busca informacion", "noticias", "ultima hora", "actual", "hoy en", "quien gano", "precio de"],
        ["ver_pantalla"] = ["pantalla", "que ves", "que hay en mi pantalla", "mira esto", "este error", "esta ventana", "lo que tengo delante", "captura"],
        ["buscar_en_web"] = ["busca en google", "busca en internet", "busca en youtube", "googlea", "en google", "en youtube", "wikipedia", "maps", "mapa", "traduce", "como llego"],
    };
}
