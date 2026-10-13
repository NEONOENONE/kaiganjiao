using System.Text.Json;
using KaiGanJiao.App;
using KaiGanJiao.Core;

internal static class DesktopTests
{
    public static void Run(string root, Action<bool, string> check)
    {
        var old = JsonSerializer.Deserialize<AppSettings>("{\"Version\":1,\"DefaultModuleId\":2}")!;
        check(!old.EdgeAutoHide && !old.CloseToTray && !old.StartWithWindows, "旧设置兼容：三个新开关默认关闭");
        var store = new AtomicJsonStore<AppSettings>(Path.Combine(root, "desktop-settings.json"), s => s.Version == 1 && Modules.Valid(s.DefaultModuleId));
        var startup = new FakeStartup();
        var settings = new SettingsService(store, startup);
        var enabled = new AppSettings(DefaultModuleId: 3, EdgeAutoHide: true, CloseToTray: true, StartWithWindows: true);
        settings.Save(enabled);
        check(store.Load(() => new()) == enabled && startup.Command == startup.LaunchCommand, "保存全部开关和默认模块，并注册启动命令");
        check(settings.ReadStartupState(old).StartWithWindows, "读取实际启动项状态");
        settings.Save(enabled with { StartWithWindows = false });
        check(startup.Command == null, "关闭自启动移除应用启动项");
        using (var locked = new FileStream(store.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            bool failed = false;
            try { settings.Save(enabled); } catch (IOException) { failed = true; }
            check(failed && startup.Command == null, "保存失败时回滚启动项");
        }
        startup.FailWrites = true;
        var before = File.ReadAllText(store.Path);
        try { settings.Save(enabled); } catch (IOException) { }
        check(File.ReadAllText(store.Path) == before, "启动项写入失败不保存虚假设置");
        using (var dialog = new SettingsDialog(enabled, false))
        {
            dialog.Show(); Application.DoEvents();
            check(dialog.SelectedSettings == enabled, "设置面板正确加载全部选项");
            var checks = Descendants(dialog).OfType<CheckBox>().ToArray();
            check(checks.Length == 3 && !checks.Single(c => c.Text == "开机自启动").Enabled, "隔离实例禁止修改系统启动项");
            check(Descendants(dialog).OfType<Button>().Where(b => b.Text is "保存" or "取消").All(b => b.RectangleToScreen(b.ClientRectangle).Bottom <= dialog.RectangleToScreen(dialog.ClientRectangle).Bottom), "设置保存和取消按钮可见");
            dialog.Close();
        }
        var monitor = new Rectangle(-1920, 0, 1920, 1080);
        var work = new Rectangle(-1920, 0, 1920, 1040);
        var rect = new Rectangle(-1915, 100, 380, 620);
        check(EdgeGeometry.Detect(rect, monitor, work, [], 16) == ScreenEdge.Left, "负坐标显示器左侧识别");
        check(EdgeGeometry.Detect(new(-382, 100, 380, 620), monitor, work, [], 16) == ScreenEdge.Right, "右侧识别");
        check(EdgeGeometry.Detect(new(-1000, 8, 380, 620), monitor, work, [], 16) == ScreenEdge.Top, "上侧识别");
        check(EdgeGeometry.Detect(new(-1000, 420, 380, 620), monitor, work, [], 16) == ScreenEdge.None, "底部不与任务栏争用");
        check(EdgeGeometry.Detect(new(-382, 100, 380, 620), monitor, work, [new(0, 0, 1920, 1080)], 16) == ScreenEdge.None, "双屏接缝不隐藏");
        check(EdgeGeometry.Detect(rect, monitor, new(-1880, 0, 1880, 1080), [], 16) == ScreenEdge.None, "左侧任务栏占用时不隐藏");
        var snapped = EdgeGeometry.Snap(rect, work, ScreenEdge.Left);
        check(snapped.Left == work.Left && work.Contains(snapped), "贴边吸附并保持可见");
        check(EdgeGeometry.Strip(snapped, ScreenEdge.Left, 6) == new Rectangle(work.Left, 100, 6, 620), "边缘唤回细条几何");
        check(work.Contains(EdgeGeometry.Fit(new(6000, 6000, 380, 620), work)), "显示器变化后移回可见范围");

        using (var window = new Form { Bounds = new Rectangle(Screen.PrimaryScreen!.WorkingArea.Left, Screen.PrimaryScreen.WorkingArea.Top + 80, 300, 300), StartPosition = FormStartPosition.Manual })
        {
            bool busy = false;
            using var edge = new EdgeAutoHide(window, () => busy);
            window.Show(); Application.DoEvents();
            // Choose an outer edge away from the real cursor; no mouse input is injected.
            var screen = Screen.PrimaryScreen;
            var candidates = new[] { new Rectangle(screen.WorkingArea.Left, screen.WorkingArea.Top + 80, 300, 300),
                new Rectangle(screen.WorkingArea.Right - 300, screen.WorkingArea.Top + 80, 300, 300),
                new Rectangle(screen.WorkingArea.Left + 100, screen.WorkingArea.Top, 300, 300) };
            window.Bounds = candidates.First(r => !r.Contains(Cursor.Position) && EdgeGeometry.Detect(r, screen.Bounds, screen.WorkingArea,
                Screen.AllScreens.Where(s => s.DeviceName != screen.DeviceName).Select(s => s.Bounds), 16) != ScreenEdge.None);
            edge.SetEnabled(true);
            busy = true;
            check(!edge.TryCollapse() && window.Visible, "编辑或菜单打开时不收起");
            busy = false;
            edge.EvaluatePointer();
            var limit = DateTime.UtcNow.AddSeconds(3);
            while (!edge.IsCollapsed && DateTime.UtcNow < limit) { Application.DoEvents(); Thread.Sleep(20); }
            check(!window.Visible && edge.IsCollapsed, "鼠标离开后一次性延时收起并保留细条");
            edge.Reveal(true);
            check(window.Visible && !edge.IsCollapsed, "边缘唤回恢复窗口");
            check(!edge.TryCollapse(), "主动唤回时等待鼠标进入，避免立刻缩回");
            edge.CheckDock(); edge.TryCollapse(); edge.SetEnabled(false);
            check(window.Visible && !edge.IsCollapsed && edge.DockedEdge == ScreenEdge.None, "关闭贴边开关立即恢复");
            window.Close();
        }
        var taskStore = new AtomicJsonStore<TaskDocument>(Path.Combine(root, "tray-tasks.json"), TaskService.IsValid);
        using (var resident = new MainForm(new TaskService(taskStore), new AppSettings(CloseToTray: true), root))
        {
            resident.Show(); Application.DoEvents(); resident.Close(); Application.DoEvents();
            check(!resident.IsDisposed && !resident.Visible, "常驻开启时关闭窗口隐藏到托盘");
            resident.ShowWindow(); Application.DoEvents();
            check(resident.Visible, "托盘显示入口恢复窗口");
            resident.ExitApplication(); Application.DoEvents();
            check(resident.IsDisposed, "退出程序始终彻底退出");
        }
        using var nonresident = new MainForm(new TaskService(taskStore), new AppSettings(), root);
        nonresident.Show(); Application.DoEvents(); nonresident.Close();
        check(nonresident.IsDisposed, "常驻关闭时点关闭直接退出");
    }
    private static IEnumerable<Control> Descendants(Control c) => c.Controls.Cast<Control>().SelectMany(x => new[] { x }.Concat(Descendants(x)));
    private sealed class FakeStartup : IStartupRegistration
    {
        public string LaunchCommand => "\"C:\\Folder With Spaces\\KaiGanJiao.exe\"";
        public string? Command;
        public bool FailWrites;
        public string? ReadCommand() => Command;
        public void WriteCommand(string? command) { if (FailWrites) throw new IOException("模拟启动项拒绝写入"); Command = command; }
    }
}
