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
        ["buscar_en_web"] = ["busca en google", "busca en internet", "busca en youtube", "googlea", "en google", "en youtube", "wikipedia", "maps", "mapa", "traduce", "como llego"],
    };
}
