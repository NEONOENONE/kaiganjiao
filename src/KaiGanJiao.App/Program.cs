using KaiGanJiao.Core;
using System.Security.Cryptography;
using System.Text;

namespace KaiGanJiao.App;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        // Explicit isolated directory is useful for testing; normal launches always use LocalAppData.
        var folder = args.Length == 2 && args[0] == "--data-dir" ? System.IO.Path.GetFullPath(args[1]) :
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KaiGanJiao");
        try
        {
            var instanceKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(folder.ToUpperInvariant())));
            using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\KaiGanJiao-" + instanceKey, out bool firstInstance);
            if (!firstInstance) { showSignal.Set(); return; }
            Directory.CreateDirectory(folder);
            using var dataLock = new FileStream(System.IO.Path.Combine(folder, ".writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var taskStore = new AtomicJsonStore<TaskDocument>(System.IO.Path.Combine(folder, "tasks.json"), TaskService.IsValid);
            var settingsStore = new AtomicJsonStore<AppSettings>(System.IO.Path.Combine(folder, "settings.json"), s => s.Version == 1 && Modules.Valid(s.DefaultModuleId));
            var service = new TaskService(taskStore);
            var settings = settingsStore.Load(() => new AppSettings());
            if (!File.Exists(settingsStore.Path)) settingsStore.Save(settings);
            Application.ThreadException += (_, e) => MessageBox.Show("操作未完成：" + e.Exception.Message, "开干角", MessageBoxButtons.OK, MessageBoxIcon.Error);
            var settingsService = new SettingsService(settingsStore, args.Length == 0 ? new StartupRegistration() : null);
            using var window = new MainForm(service, settings, folder, settingsService);
            _ = window.Handle;
            var registration = ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) =>
            {
                if (!window.IsDisposed)
                    try { window.BeginInvoke(window.ShowWindow); }
                    catch (InvalidOperationException) { }
            }, null, Timeout.Infinite, executeOnlyOnce: false);
            window.Shown += (_, _) =>
            {
                var notices = new[] { taskStore.RecoveryNotice, settingsStore.RecoveryNotice }.Where(s => s != null);
                if (notices.Any()) MessageBox.Show(window, string.Join("\n", notices), "已恢复备份", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            };
            try { Application.Run(window); }
            finally { registration.Unregister(null); }
        }
        catch (Exception e)
        {
            MessageBox.Show("无法启动开干角。若已有窗口，请先使用该窗口。\n\n" + e.Message + "\n\n数据目录：" + folder,
                "开干角", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
