using System.Runtime.Versioning;
using Carvis.Core.Indexing.Readers;

namespace Carvis.Windows;

/// <summary>Windows' built-in OCR (Windows.Media.Ocr) in the languages installed for the user.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsOcrEngine : IOcrEngine
{
    private const string Script = """
        Add-Type -AssemblyName System.Runtime.WindowsRuntime
        $asTask = ([System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' })[0]
        function Await($operation, [Type]$type) { $task = $asTask.MakeGenericMethod($type).Invoke($null, @($operation)); $task.Wait() | Out-Null; $task.Result }
        [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime] | Out-Null
        [Windows.Media.Ocr.OcrEngine, Windows.Foundation, ContentType = WindowsRuntime] | Out-Null
        [Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics, ContentType = WindowsRuntime] | Out-Null
        $file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($path)) ([Windows.Storage.StorageFile])
        $stream = Await ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
        $decoder = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
        $bitmap = Await ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
        $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
        $result = Await ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
        $result.Lines | ForEach-Object { $_.Text }
        """;

    public bool IsAvailable => Environment.OSVersion.Version.Major >= 10;

    public async Task<string> RecognizeAsync(byte[] image, CancellationToken cancellationToken = default)
    {
        var file = Path.Combine(Path.GetTempPath(), $"carvis-ocr-{Guid.NewGuid():N}.img");
        await File.WriteAllBytesAsync(file, image, cancellationToken);
        try
        {
            var result = await PowerShellRunner.RunAsync($"$path = {PowerShellRunner.Quote(file)}\n{Script}", TimeSpan.FromSeconds(60), cancellationToken);
            return result.Success ? result.Output : string.Empty;
        }
        finally
        {
            File.Delete(file);
        }
    }
}
