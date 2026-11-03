namespace Carvis.Core.Tools;

public interface IToolRegistry
{
    IReadOnlyList<ITool> All { get; }
    ITool? Find(string name);
    void Add(ITool tool);
}

public sealed class ToolRegistry : IToolRegistry
{
    private readonly List<ITool> _tools = [];
    private readonly object _lock = new();

    public ToolRegistry(IEnumerable<ITool> tools)
    {
        foreach (var tool in tools)
            Add(tool);
    }

    public IReadOnlyList<ITool> All
    {
        get { lock (_lock) return _tools.ToList(); }
    }

    public ITool? Find(string name)
    {
        var normalized = name.Trim().Replace('-', '_').Replace(' ', '_');
        lock (_lock)
            return _tools.FirstOrDefault(t => string.Equals(t.Name, normalized, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Registers a tool; a later tool with the same name replaces the earlier one (plugins).</summary>
    public void Add(ITool tool)
    {
        lock (_lock)
        {
            _tools.RemoveAll(t => string.Equals(t.Name, tool.Name, StringComparison.OrdinalIgnoreCase));
            _tools.Add(tool);
        }
    }
}
