using System.Text.Json;

namespace Carvis.App.Services;

public sealed record WindowPlacement(int X, int Y, double Width, double Height);

/// <summary>Remembers where the window was, between sessions.</summary>
public sealed class WindowStateStore(string file)
{
    public WindowPlacement? Load()
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(file)) : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return null;
        }
    }

    public void Save(WindowPlacement placement)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(placement));
        }
        catch (IOException)
        {
        }
    }
}
