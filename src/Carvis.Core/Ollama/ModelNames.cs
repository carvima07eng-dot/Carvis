namespace Carvis.Core.Ollama;

public static class ModelNames
{
    /// <summary>Ollama treats "nomic-embed-text" and "nomic-embed-text:latest" as the same model.</summary>
    public static bool AreSame(string? a, string? b) =>
        string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? name)
    {
        name = name?.Trim() ?? string.Empty;
        return name.Contains(':') ? name : name + ":latest";
    }
}
