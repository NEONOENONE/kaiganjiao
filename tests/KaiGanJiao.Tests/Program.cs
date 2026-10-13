using System.Reflection;
using KaiGanJiao.Core;
using KaiGanJiao.App;

internal static class Program
{
    private static int passed;
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../artifacts/tests", Guid.NewGuid().ToString("N")));
    [STAThread]
    private static int Main(string[] args)
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Directory.CreateDirectory(Root);
        try
        {
            if (args.Contains("--data-folder"))
            {
                DataDirectoryTests.Run(Root, Check);
                Console.WriteLine($"PASS: {passed} data-directory assertions.");
                return 0;
            }
            if (args.Contains("--desktop"))
            {
                DesktopTests.Run(Root, Check);
                Console.WriteLine($"PASS: {passed} desktop assertions. Isolated files: {Root}");
                return 0;
            }
            DateTimeOffset now = new(2026, 10, 1, 3, 0, 0, TimeSpan.Zero);
            var store = Store("normal");
            var service = new TaskService(store, () => now);
            Check(service.Items.Count == 0, "首次启动为空列表");
            var first = service.Add("  第一项  ", 2);
            Check(first.Text == "第一项" && first.ModuleId == 2 && first.CreatedUtc == now && first.Id != Guid.Empty, "新增任务字段及 UTC");
            Throws<ArgumentException>(() => service.Add(" \n ", 2), "空任务被拒绝");
            Throws<ArgumentException>(() => service.Add("无效模块", 5), "无效模块被拒绝");
            for (int module = 1; module <= 4; module++)
            {
                service.Edit(first.Id, "更新文字", module);
                Check(service.Items[0].ModuleId == module && service.Items[0].CreatedUtc == first.CreatedUtc, $"移动至模块 {module} 保留创建时间");
            }
            foreach (var (hours, days) in new[] { (0.0, 0), (23.999, 0), (24.0, 1), (47.999, 1), (48.0, 2), (2400.0, 100), (-1.0, 0) })
                Check(TaskAge.Days(first, first.CreatedUtc.AddHours(hours)) == days, $"存在天数 {hours} 小时 -> {days} 天");
            Check(TaskAge.UntilNextBoundary([first], now.AddHours(23)) == TimeSpan.FromHours(1), "下一次精确边界");
            Check(TaskAge.UntilNextBoundary([first], now.AddHours(24)) == TimeSpan.FromDays(1), "边界后重新安排");
            now = now.AddDays(120).AddHours(2);
            Check(TaskAge.Days(first, now) == 120, "模拟长时间关闭或休眠后重算");
            service.Complete(first.Id);
            Check(service.Items[0].IsCompleted && service.Items[0].CompletedUtc == now, "完成并记录时间");
            Check(TaskAge.UntilNextBoundary(service.Items, now) == null, "全部完成后停止天数计时");
            var restarted = new TaskService(Store("normal"));
            Check(restarted.Items.Single() == service.Items.Single(), "重启保留完成任务与字段");
            var second = restarted.Add("保留项", 1);
            restarted.Delete(first.Id);
            Check(restarted.Items.Single().Id == second.Id, "删除独立于完成");
            var beforeFailure = restarted.Items.ToArray();
            using (var locked = new FileStream(store.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                Throws<IOException>(() => restarted.Add("不能落盘", 2), "文件锁定时报告写入失败");
            Check(restarted.Items.SequenceEqual(beforeFailure), "保存失败不修改内存状态");
            Check(new TaskService(Store("normal")).Items.SequenceEqual(beforeFailure), "保存失败不损坏原文件");
            var recovery = Store("recovery"); var rs = new TaskService(recovery);
            rs.Add("已备份", 3); rs.Add("最新项", 4);
            File.WriteAllText(recovery.Path, "{ broken json");
            var recovered = new TaskService(recovery);
            Check(recovered.Items.Single().Text == "已备份" && recovery.RecoveryNotice != null, "损坏时恢复上一版备份");
            Check(Directory.GetFiles(Path.GetDirectoryName(recovery.Path)!, "*.damaged-*").Length == 1, "保留损坏原文件");
            var broken = Store("broken"); Directory.CreateDirectory(Path.GetDirectoryName(broken.Path)!);
            File.WriteAllText(broken.Path, "bad"); File.WriteAllText(broken.Path + ".bak", "bad-backup");
            Throws<DataRecoveryException>(() => new TaskService(broken), "双文件损坏时停止加载");
            Check(File.ReadAllText(broken.Path) == "bad" && File.ReadAllText(broken.Path + ".bak") == "bad-backup", "双损坏不覆盖数据");
            Check(!TaskService.IsValid(new TaskDocument(1, [first, first])), "拒绝重复 ID");
            Check(!TaskService.IsValid(new TaskDocument(1, [first with { ModuleId = 0 }])), "拒绝错误数据模块");
            Check(!TaskService.IsValid(new TaskDocument(1, [first with { IsCompleted = true }])), "拒绝缺少完成时间");
            var settingsStore = new AtomicJsonStore<AppSettings>(Path.Combine(Root, "settings.json"), s => s.Version == 1 && Modules.Valid(s.DefaultModuleId));
            settingsStore.Save(new AppSettings());
            Check(settingsStore.Load(() => new AppSettings()).DefaultModuleId == 2, "设置独立保存且默认模块为2");
            TestDialogs();
            TestMainLayout();
            DesktopTests.Run(Root, Check);
            DataDirectoryTests.Run(Root, Check);
            Console.WriteLine($"PASS: {passed} assertions. Isolated files: {Root}");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static AtomicJsonStore<TaskDocument> Store(string name) => new(Path.Combine(Root, name, "tasks.json"), TaskService.IsValid);
    private static IEnumerable<Control> Children(Control root) => root.Controls.Cast<Control>().SelectMany(c => new[] { c }.Concat(Children(c)));
    private static void TestDialogs()
    {
        using (var edit = new TaskEditor(2))
        {
            edit.Shown += (_, _) => edit.BeginInvoke(() =>
            {
                Check(Children(edit).OfType<TextBox>().Single().Focused, "新增面板自动输入焦点");
                Check(edit.ModuleId == 2, "新增面板默认这周拿下");
                ((Button)edit.AcceptButton!).PerformClick();
                Check(edit.DialogResult == DialogResult.None, "新增面板阻止空内容");
                Children(edit).OfType<TextBox>().Single().Text = "面板测试";
                typeof(TaskEditor).GetMethod("ProcessCmdKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(edit, [new Message(), Keys.Enter]);
            });
            Check(edit.ShowDialog() == DialogResult.OK && edit.TaskText == "面板测试", "Enter 保存并返回内容");
        }
        using (var canceled = new TaskEditor(2))
        {
            canceled.Shown += (_, _) => canceled.BeginInvoke(() =>
                typeof(Form).GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(canceled, [Keys.Escape]));
            Check(canceled.ShowDialog() == DialogResult.Cancel, "Esc 取消新增");
        }
        foreach (string action in new[] { "cancel", "complete", "escape", "close" })
        {
            using var dialog = new ConfirmDialog("确认测试任务", true);
            dialog.Shown += (_, _) => dialog.BeginInvoke(() =>
            {
                var cancel = (Button)dialog.CancelButton!;
                Check(cancel.Text == "等等，好像还差一点" && cancel.Focused && dialog.AcceptButton == cancel, "完成确认默认取消焦点");
                if (action == "cancel") cancel.PerformClick();
                else if (action == "complete") Children(dialog).OfType<Button>().Single(b => b.Text == "是的，我已经全部完成了").PerformClick();
                else if (action == "close") dialog.Close();
                else typeof(Form).GetMethod("ProcessDialogKey", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(dialog, [Keys.Escape]);
            });
            Check(dialog.ShowDialog() == (action == "complete" ? DialogResult.OK : DialogResult.Cancel), "确认面板：" + action);
        }
        using var delete = new ConfirmDialog("删除测试", false);
        Check(((Button)delete.CancelButton!).Text == "保留任务", "删除提供独立取消确认");
    }
    private static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("FAIL: " + label);
        passed++; Console.WriteLine("PASS " + label);
    }
    private static void TestMainLayout()
    {
        var service = new TaskService(Store("layout"));
        using (var window = new MainForm(service, new AppSettings(), Root))
        {
            window.Show(); Application.DoEvents();
            var list = window.Controls.OfType<FlowLayoutPanel>().Single();
            Check(list.Controls.Count == 4, "四模块始终存在");
            Check(!list.VerticalScroll.Visible && !list.HorizontalScroll.Visible, "空列表四模块适配默认窗口");
            window.Close();
        }
        for (int i = 0; i < 40; i++) service.Add($"第{i}项：" + new string('长', 150), i % 4 + 1);
        using var many = new MainForm(service, new AppSettings(), Root);
        many.Show(); Application.DoEvents();
        var scroll = many.Controls.OfType<FlowLayoutPanel>().Single();
        Check(scroll.VerticalScroll.Visible && !scroll.HorizontalScroll.Visible, "大量长任务仅纵向滚动");
        many.Size = many.MinimumSize; Application.DoEvents();
        Check(!scroll.HorizontalScroll.Visible, "最小窗口没有横向溢出");
        many.Close();
        var boundaryTime = DateTimeOffset.UtcNow.AddDays(-1).AddSeconds(2);
        var boundaryService = new TaskService(Store("live-boundary"), () => boundaryTime);
        boundaryService.Add("临界任务", 2);
        using var boundaryWindow = new MainForm(boundaryService, new AppSettings(), Root);
        boundaryWindow.Show(); Application.DoEvents();
        Check(Children(boundaryWindow).OfType<Label>().Any(l => l.Text == "0天"), "临界前主界面显示0天");
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && !Children(boundaryWindow).OfType<Label>().Any(l => l.Text == "1天"))
        {
            Application.DoEvents(); Thread.Sleep(20);
        }
        Check(Children(boundaryWindow).OfType<Label>().Any(l => l.Text == "1天"), "主界面定时器跨24小时自动更新");
        boundaryWindow.Close();
    }
    private static void Throws<T>(Action action, string label) where T : Exception
    {
        try { action(); } catch (T) { Check(true, label); return; }
        throw new Exception("FAIL: " + label);
    }
}
