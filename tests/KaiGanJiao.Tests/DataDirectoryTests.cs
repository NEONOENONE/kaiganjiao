using KaiGanJiao.App;

internal static class DataDirectoryTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        var folder = Path.Combine(root, "数据文件夹 with spaces");
        Directory.CreateDirectory(folder);
        var marker = Path.Combine(folder, "existing.txt"); File.WriteAllText(marker, "preserve");
        var physical = DataDirectory.ResolvePhysicalPath(folder);
        check(Path.IsPathFullyQualified(physical) && Directory.Exists(physical), "解析实际存在的绝对目录");
        check(File.ReadAllText(Path.Combine(physical, "existing.txt")) == "preserve", "解析指向同一份文件");
        check(DataDirectory.ResolvePhysicalPath(folder + Path.DirectorySeparatorChar) == physical, "路径尾部分隔符兼容");
        var command = DataDirectory.CreateOpenCommand(folder);
        check(command.FileName == Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe") && !command.UseShellExecute, "明确调用系统资源管理器");
        check(command.ArgumentList.Count == 1 && command.ArgumentList[0] == physical, "中文与空格路径作为单个参数传递");
        check(DataDirectory.ToExplorerPath(@"\\?\C:\Users\NEON\AppData\Local\KaiGanJiao") == @"C:\Users\NEON\AppData\Local\KaiGanJiao", "去除本地扩展路径前缀");
        check(DataDirectory.ToExplorerPath(@"\\?\UNC\server\share\folder") == @"\\server\share\folder", "保留UNC网络路径含义");
        var missing = Path.Combine(root, "missing"); bool rejected = false;
        try { DataDirectory.ResolvePhysicalPath(missing); } catch (DirectoryNotFoundException) { rejected = true; }
        check(rejected && !Directory.Exists(missing), "目录缺失时报告错误，不创建空目录冒充数据");
        check(File.ReadAllText(marker) == "preserve", "打开目录准备过程不改写已有文件");
        var live = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KaiGanJiao");
        if (Directory.Exists(live))
        {
            var actual = DataDirectory.ResolvePhysicalPath(live);
            check(Directory.Exists(actual), "本机AppData实际数据目录可访问");
            Console.WriteLine("Actual data directory: " + actual);
        }
    }
}
