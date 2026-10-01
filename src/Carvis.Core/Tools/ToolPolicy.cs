using Carvis.Core.Configuration;

namespace Carvis.Core.Tools;

/// <summary>Decides when the user has to confirm an action.</summary>
public sealed class ToolPolicy(PermissionsSettings settings)
{
    private readonly HashSet<string> _sessionApprovals = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();

    public bool NeedsConfirmation(ToolPreview preview, ToolContext context) => preview.AlwaysConfirm || preview.Risk switch
    {
        ToolRisk.Read => false,
        ToolRisk.Low => settings.ConfirmLowRisk || context.ExternalContentInTurn,
        // After reading files or web pages, text there could be trying to give orders: always ask.
        ToolRisk.Modify => context.ExternalContentInTurn || (settings.ConfirmChanges && !IsApproved(preview)),
        _ => true,
    };

    public bool CanApproveForSession(ToolPreview preview) =>
        preview.Risk is ToolRisk.Modify or ToolRisk.Low && preview.PermissionScope is not null && !preview.AlwaysConfirm;

    public void ApproveForSession(ToolPreview preview)
    {
        if (!CanApproveForSession(preview))
            return;
        lock (_lock)
            _sessionApprovals.Add(preview.PermissionScope!);
    }

    public void ResetSession()
    {
        lock (_lock)
            _sessionApprovals.Clear();
    }

    private bool IsApproved(ToolPreview preview)
    {
        if (preview.PermissionScope is null)
            return false;
        lock (_lock)
            return _sessionApprovals.Contains(preview.PermissionScope);
    }
}
