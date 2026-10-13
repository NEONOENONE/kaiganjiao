using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace KaiGanJiao.App;

public static class DataDirectory
{
    public static string ResolvePhysicalPath(string folder)
    {
        var fullPath = Path.GetFullPath(folder);
        if (!Directory.Exists(fullPath)) throw new DirectoryNotFoundException("数据目录不存在，请保留现有文件并检查保存位置：" + fullPath);
        // MSIX-launched processes can see a virtual AppData path that Explorer cannot see.
        // Ask Windows for the actual directory backing this handle; never guess a package name.
        using var handle = CreateFileW(fullPath, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero);
        if (handle.IsInvalid) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法访问数据目录。");
        var buffer = new StringBuilder(512);
        uint length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        if (length >= buffer.Capacity)
        {
            buffer = new StringBuilder(checked((int)length + 1));
            length = GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
        }
        if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法确定数据目录的实际位置。");
        if (length >= buffer.Capacity) throw new IOException("数据目录路径过长，无法打开。");
        return ToExplorerPath(buffer.ToString());
    }

    public static string ToExplorerPath(string path)
    {
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + path[8..];
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal) && path.Length > 6 && path[5] == ':') return path[4..];
        return path;
    }

    public static ProcessStartInfo CreateOpenCommand(string folder)
    {
        var command = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"))
        { UseShellExecute = false };
        command.ArgumentList.Add(ResolvePhysicalPath(folder));
        return command;
    }

    public static void Open(string folder)
    {
        using var process = Process.Start(CreateOpenCommand(folder));
        if (process == null) throw new IOException("无法启动 Windows 文件资源管理器。");
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint length, uint flags);
}
