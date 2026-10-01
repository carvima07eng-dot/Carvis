using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Carvis.Core.Platform;

namespace Carvis.Windows;

/// <summary>The real Recycle Bin: items can be restored from Explorer.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRecycleBin : IRecycleBin
{
    private const uint FO_DELETE = 3;
    private const ushort FOF_SILENT = 0x0004;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOERRORUI = 0x0400;
    private const ushort FOF_WANTNUKEWARNING = 0x4000;

    public bool CanRestore => false;

    public Task<RecycleResult> SendAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        if (paths.Count == 0)
            return Task.FromResult(new RecycleResult(null));

        var operation = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            // Double null terminated list: the marshaller adds the last \0.
            pFrom = string.Join('\0', paths) + '\0',
            // FOF_WANTNUKEWARNING still warns if something is too big for the bin and would be lost.
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | FOF_WANTNUKEWARNING,
        };

        var result = SHFileOperation(ref operation);
        if (result != 0)
            throw new IOException($"Windows no ha podido enviar a la papelera (código 0x{result:X}).");
        if (operation.fAnyOperationsAborted)
            throw new IOException("Se ha cancelado el envío a la papelera.");

        return Task.FromResult(new RecycleResult(null));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT fileOp);
}
