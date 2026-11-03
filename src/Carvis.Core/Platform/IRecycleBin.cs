using Carvis.Core.Configuration;

namespace Carvis.Core.Platform;

/// <summary>Result of sending items to the bin. TrashLocations is set when Carvis can restore them itself.</summary>
public sealed record RecycleResult(IReadOnlyDictionary<string, string>? TrashLocations);

public interface IRecycleBin
{
    /// <summary>Whether Carvis can undo a deletion by itself (Windows' bin is restored from Explorer).</summary>
    bool CanRestore { get; }

    Task<RecycleResult> SendAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default);
}

/// <summary>Used where there is no system recycle bin: moves items into Carvis' own trash folder.</summary>
public sealed class TrashFolderRecycleBin(AppPaths paths) : IRecycleBin
{
    public bool CanRestore => true;

    public Task<RecycleResult> SendAsync(IReadOnlyList<string> items, CancellationToken cancellationToken = default)
    {
        var batch = Path.Combine(paths.TrashDirectory, DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        Directory.CreateDirectory(batch);
        var locations = new Dictionary<string, string>();

        foreach (var item in items)
        {
            var target = Path.Combine(batch, Path.GetFileName(item));
            if (Directory.Exists(item))
                Directory.Move(item, target);
            else
                File.Move(item, target);
            locations[item] = target;
        }

        return Task.FromResult(new RecycleResult(locations));
    }
}
