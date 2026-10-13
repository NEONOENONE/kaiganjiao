using KaiGanJiao.Core;
using Microsoft.Win32;

namespace KaiGanJiao.App;

public interface IStartupRegistration
{
    string LaunchCommand { get; }
    string? ReadCommand();
    void WriteCommand(string? command);
}

public sealed class StartupRegistration : IStartupRegistration
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "KaiGanJiao";
    public string LaunchCommand => "\"" + Environment.ProcessPath + "\"";
    public string? ReadCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        return key?.GetValue(ValueName) as string;
    }
    public void WriteCommand(string? command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(KeyPath, writable: true);
        if (command == null) key.DeleteValue(ValueName, throwOnMissingValue: false);
        else key.SetValue(ValueName, command, RegistryValueKind.String);
    }
}

public sealed class SettingsService(AtomicJsonStore<AppSettings> store, IStartupRegistration? startup)
{
    public bool CanConfigureStartup => startup != null;
    public AppSettings ReadStartupState(AppSettings current) => startup == null ? current :
        current with { StartWithWindows = startup.ReadCommand() == startup.LaunchCommand };

    public void Save(AppSettings next)
    {
        var previous = startup?.ReadCommand();
        var desired = next.StartWithWindows ? startup?.LaunchCommand : null;
        bool changed = startup != null && previous != desired;
        if (changed) startup!.WriteCommand(desired);
        try { store.Save(next); }
        catch (Exception saveError)
        {
            if (changed)
            {
                try { startup!.WriteCommand(previous); }
                catch (Exception restoreError)
                { throw new IOException("设置保存失败，且未能恢复开机启动项，请重新检查开机自启动开关。", new AggregateException(saveError, restoreError)); }
            }
            throw;
        }
    }
}

public sealed class SettingsDialog : Form
{
    private readonly CheckBox edge = new() { Text = "贴边自动隐藏", AutoSize = true };
    private readonly CheckBox resident = new() { Text = "关闭窗口后后台常驻（隐藏到托盘）", AutoSize = true };
    private readonly CheckBox startup = new() { Text = "开机自启动", AutoSize = true };
    private readonly ComboBox defaultModule = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200 };
    public AppSettings SelectedSettings => original with { DefaultModuleId = ((Module)defaultModule.SelectedItem!).Id,
        EdgeAutoHide = edge.Checked, CloseToTray = resident.Checked, StartWithWindows = startup.Checked };
    private readonly AppSettings original;
    public SettingsDialog(AppSettings settings, bool canConfigureStartup, string? dataFolder = null)
    {
        original = settings;
        Ui.SetDialog(this, "设置", 440); ClientSize = new Size(440, 425);
        edge.Checked = settings.EdgeAutoHide; resident.Checked = settings.CloseToTray;
        startup.Checked = settings.StartWithWindows; startup.Enabled = canConfigureStartup;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, ColumnCount = 1, RowCount = 10 };
        void Add(Control c) { c.Margin = new Padding(0, 4, 0, 6); layout.Controls.Add(c); }
        void Hint(string text) => Add(new Label { Text = text, AutoSize = true, MaximumSize = new Size(390, 0), ForeColor = Ui.Muted });
        Add(edge);
        Hint("拖到屏幕上、左或右侧外边缘后，鼠标离开会收起；移到边缘细条即可展开。避开任务栏和两块屏幕的接缝。");
        Add(resident);
        Hint("开启：点 × 后继续运行，可从托盘重新打开。\n关闭：点 × 彻底退出，不再占用运行资源。");
        Add(startup);
        Hint(canConfigureStartup ? "登录 Windows 后自动打开。若被系统的“启动应用”禁用，需在那里重新开启。" : "测试实例不能修改 Windows 开机启动项。");
        Add(new Label { Text = "新增任务的默认模块", AutoSize = true });
        defaultModule.DisplayMember = nameof(Module.Title); defaultModule.Items.AddRange(Modules.All.Cast<object>().ToArray());
        defaultModule.SelectedItem = Modules.All.Single(m => m.Id == settings.DefaultModuleId); Add(defaultModule);
        if (dataFolder != null)
        {
            var openFolder = Ui.Button("打开数据文件夹");
            openFolder.Click += (_, _) =>
            {
                try { DataDirectory.Open(dataFolder); }
                catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception or UnauthorizedAccessException or ArgumentException)
                { MessageBox.Show(this, "无法打开数据文件夹：\n" + e.Message + "\n\n此操作不会修改待办数据。", "打开数据文件夹", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            };
            Add(openFolder);
        }
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        var cancel = Ui.Button("取消"); cancel.DialogResult = DialogResult.Cancel;
        var save = Ui.Button("保存"); save.DialogResult = DialogResult.OK;
        actions.Controls.Add(cancel); actions.Controls.Add(save); Add(actions);
        Controls.Add(layout); AcceptButton = save; CancelButton = cancel;
    }
}
