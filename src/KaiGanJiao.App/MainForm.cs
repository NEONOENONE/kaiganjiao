using System.Drawing.Drawing2D;
using Microsoft.Win32;
using KaiGanJiao.Core;

namespace KaiGanJiao.App;

public sealed class MainForm : Form
{
    private readonly TaskService service;
    private readonly string dataFolder;
    private AppSettings settings;
    private readonly SettingsService settingsService;
    private readonly EdgeAutoHide edgeHide;
    private readonly NotifyIcon tray = new() { Text = "开干角" };
    private readonly ContextMenuStrip trayMenu = new();
    private bool exitRequested;
    private readonly FlowLayoutPanel list = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(14, 10, 14, 10) };
    private readonly System.Windows.Forms.Timer ageTimer = new();
    private readonly ContextMenuStrip moreMenu = new();
    private bool arranging;
    public MainForm(TaskService service, AppSettings settings, string dataFolder, SettingsService? settingsService = null)
    {
        this.service = service; this.settings = settings; this.dataFolder = dataFolder;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        tray.Icon = Icon;
        this.settingsService = settingsService ?? new SettingsService(
            new AtomicJsonStore<AppSettings>(Path.Combine(dataFolder, "settings.json"), s => s.Version == 1 && Modules.Valid(s.DefaultModuleId)), null);
        Text = "开干角";
        AutoScaleDimensions = new SizeF(96, 96); AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(380, 610); MinimumSize = new Size(350, 360);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(247, 248, 250); ForeColor = Ui.Ink;
        var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 58, ColumnCount = 3, Padding = new Padding(18, 9, 12, 8), BackColor = Color.White };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        header.Controls.Add(new Label { Text = "开干角", Font = Ui.Title, AutoSize = true, Anchor = AnchorStyles.Left });
        var add = new Button { Text = "+", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, AccessibleName = "新增任务" };
        var more = new Button { Text = "···", Dock = DockStyle.Fill, FlatStyle = FlatStyle.Flat, AccessibleName = "更多" };
        add.FlatAppearance.BorderSize = more.FlatAppearance.BorderSize = 0;
        header.Controls.Add(add); header.Controls.Add(more);
        add.Click += (_, _) => EditTask(null);
        moreMenu.Items.Add("设置…", null, (_, _) => OpenSettings());
        moreMenu.Items.Add(new ToolStripSeparator());
        moreMenu.Items.Add("使用方法", null, (_, _) => MessageBox.Show(this, "＋ 记下一件事\n双击任务文字：编辑\n右键任务：编辑、移动或删除\n圆圈：确认后完成\n\n天数按每满 24 小时累计。\n设置中可开启贴边自动隐藏、开机自启动及后台常驻。\n托盘右键菜单可显示窗口或彻底退出。", "开干角", MessageBoxButtons.OK, MessageBoxIcon.Information));
        moreMenu.Items.Add("退出程序", null, (_, _) => ExitApplication());
        more.Click += (_, _) => moreMenu.Show(more, new Point(0, more.Height));
        Controls.Add(list); Controls.Add(header);
        edgeHide = new EdgeAutoHide(this, () => !Enabled || moreMenu.Visible || trayMenu.Visible ||
            list.Controls.Cast<Control>().SelectMany(s => s.Controls.OfType<TaskRow>()).Any(r => r.TaskMenu.Visible));
        trayMenu.Items.Add("显示窗口", null, (_, _) => ShowWindow());
        trayMenu.Items.Add("设置…", null, (_, _) => { ShowWindow(); OpenSettings(); });
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add("退出程序", null, (_, _) => ExitApplication());
        tray.ContextMenuStrip = trayMenu;
        tray.DoubleClick += (_, _) => ShowWindow();
        moreMenu.Closed += (_, _) => edgeHide.EvaluatePointer();
        trayMenu.Closed += (_, _) => edgeHide.EvaluatePointer();
        list.ClientSizeChanged += (_, _) => Arrange();
        DpiChanged += (_, _) => BeginInvoke(() => { Arrange(); RefreshAges(); });
        ageTimer.Tick += (_, _) => RefreshAges();
        SystemEvents.PowerModeChanged += PowerChanged;
        SystemEvents.TimeChanged += TimeChanged;
        Activated += (_, _) => RefreshAges();
        VisibleChanged += (_, _) => RefreshAges();
        Shown += (_, _) => ApplyDesktopSettings();
        RenderTasks();
    }
    public void ShowWindow() { edgeHide.Reveal(true); RefreshAges(); }
    public void ExitApplication() { exitRequested = true; Close(); }
    private void ApplyDesktopSettings()
    {
        tray.Visible = settings.EdgeAutoHide || settings.CloseToTray;
        edgeHide.SetEnabled(settings.EdgeAutoHide);
    }
    private void OpenSettings()
    {
        AppSettings current;
        try { current = settingsService.ReadStartupState(settings); }
        catch (Exception e) { MessageBox.Show(this, "无法读取启动设置：" + e.Message, "设置", MessageBoxButtons.OK, MessageBoxIcon.Error); return; }
        using var dialog = new SettingsDialog(current, settingsService.CanConfigureStartup, dataFolder);
        while (dialog.ShowDialog(this) == DialogResult.OK)
        {
            try
            {
                var next = dialog.SelectedSettings;
                settingsService.Save(next); settings = next; ApplyDesktopSettings(); return;
            }
            catch (Exception e) { MessageBox.Show(this, "未能保存设置：" + e.Message, "设置", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        }
        edgeHide.EvaluatePointer();
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exitRequested && e.CloseReason == CloseReason.UserClosing && settings.CloseToTray)
        { e.Cancel = true; tray.Visible = true; edgeHide.HideToTray(); }
        base.OnFormClosing(e);
    }
    private void EditTask(TodoItem? task)
    {
        using var dialog = new TaskEditor(settings.DefaultModuleId, task);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        Change(() => { if (task == null) service.Add(dialog.TaskText, dialog.ModuleId); else service.Edit(task.Id, dialog.TaskText, dialog.ModuleId); });
    }
    private void Finish(TodoItem task)
    {
        using var dialog = new ConfirmDialog(task.Text, true);
        if (dialog.ShowDialog(this) == DialogResult.OK) Change(() => service.Complete(task.Id));
    }
    private void Delete(TodoItem task)
    {
        using var dialog = new ConfirmDialog(task.Text, false);
        if (dialog.ShowDialog(this) == DialogResult.OK) Change(() => service.Delete(task.Id));
    }
    private void Change(Action action)
    {
        try { action(); RenderTasks(); }
        catch (Exception e) { MessageBox.Show(this, "未能保存，本次操作未生效。\n" + e.Message, "保存失败", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
    private void RenderTasks()
    {
        int scroll = -list.AutoScrollPosition.Y;
        list.SuspendLayout();
        while (list.Controls.Count > 0) { var old = list.Controls[0]; list.Controls.Remove(old); old.Dispose(); }
        foreach (var module in Modules.All)
        {
            var section = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Margin = new Padding(0, 0, 0, 20) };
            var heading = new TableLayoutPanel { AutoSize = true, ColumnCount = 2, RowCount = 2, Margin = new Padding(0, 0, 0, 8) };
            heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 9)); heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var stripe = new Panel { Size = new Size(4, 1), BackColor = ColorTranslator.FromHtml(module.Color), Dock = DockStyle.Fill, Margin = new Padding(0, 3, 5, 3) };
            heading.Controls.Add(stripe, 0, 0); heading.SetRowSpan(stripe, 2);
            heading.Controls.Add(new Label { Text = module.Title, Font = Ui.Title, AutoSize = true }, 1, 0);
            heading.Controls.Add(new Label { Text = module.Subtitle, Font = Ui.Small, ForeColor = Ui.Muted, AutoSize = true, Margin = new Padding(3, 3, 0, 0) }, 1, 1);
            section.Controls.Add(heading);
            var tasks = service.Items.Where(t => !t.IsCompleted && t.ModuleId == module.Id).ToList();
            foreach (var task in tasks)
            {
                var row = new TaskRow(task, () => Finish(task), () => EditTask(task));
                var menu = row.TaskMenu;
                menu.Closed += (_, _) => edgeHide.EvaluatePointer();
                menu.Items.Add("编辑", null, (_, _) => EditTask(task));
                var move = new ToolStripMenuItem("移动到");
                foreach (var target in Modules.All)
                {
                    var item = new ToolStripMenuItem(target.Title) { Checked = target.Id == task.ModuleId };
                    item.Click += (_, _) => Change(() => service.Edit(task.Id, task.Text, target.Id));
                    move.DropDownItems.Add(item);
                }
                menu.Items.Add(move); menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add("删除", null, (_, _) => Delete(task));
                section.Controls.Add(row);
            }
            if (tasks.Count == 0) section.Controls.Add(new Label { Text = "还没有任务", AutoSize = true, ForeColor = Ui.Muted, Margin = new Padding(12, 4, 0, 4) });
            list.Controls.Add(section);
        }
        list.ResumeLayout(); Arrange();
        list.AutoScrollPosition = new Point(0, scroll);
        RefreshAges();
    }
    private void Arrange()
    {
        if (arranging) return;
        arranging = true;
        try
        {
            int width = Math.Max(160, list.ClientSize.Width - list.Padding.Horizontal - SystemInformation.VerticalScrollBarWidth - 2);
            foreach (Control section in list.Controls)
            {
                section.Width = width;
                foreach (Control child in section.Controls)
                {
                    child.Width = width;
                    if (child is TaskRow row) row.ArrangeRow();
                }
            }
        }
        finally { arranging = false; }
    }
    private void RefreshAges()
    {
        if (IsDisposed) return;
        ageTimer.Stop();
        if (!Visible) return;
        var now = DateTimeOffset.UtcNow;
        foreach (Control section in list.Controls)
            foreach (var row in section.Controls.OfType<TaskRow>()) row.UpdateAge(now);
        var delay = TaskAge.UntilNextBoundary(service.Items, now);
        if (delay.HasValue) { ageTimer.Interval = (int)Math.Clamp(Math.Ceiling(delay.Value.TotalMilliseconds), 50, 86_400_000); ageTimer.Start(); }
    }
    private void RecalculateOnUi()
    {
        if (IsHandleCreated && !IsDisposed)
        {
            try { BeginInvoke(RefreshAges); }
            catch (InvalidOperationException) { /* Window is closing. */ }
        }
    }
    private void PowerChanged(object sender, PowerModeChangedEventArgs e) { if (e.Mode == PowerModes.Resume) RecalculateOnUi(); }
    private void TimeChanged(object? sender, EventArgs e) => RecalculateOnUi();
    protected override void Dispose(bool disposing)
    {
        if (disposing) { SystemEvents.PowerModeChanged -= PowerChanged; SystemEvents.TimeChanged -= TimeChanged;
            edgeHide.Dispose(); tray.Visible = false; tray.Dispose(); trayMenu.Dispose(); ageTimer.Dispose(); moreMenu.Dispose(); }
        base.Dispose(disposing);
    }
}

internal sealed class TaskRow : Panel
{
    private readonly TodoItem task;
    private readonly Label text;
    private readonly Label age;
    private readonly CircleButton complete;
    private readonly ToolTip tip = new() { AutoPopDelay = 30000 };
    public ContextMenuStrip TaskMenu { get; } = new();
    public TaskRow(TodoItem task, Action finish, Action edit)
    {
        this.task = task;
        DoubleBuffered = true; BackColor = Color.White; Margin = new Padding(0, 0, 0, 5);
        complete = new CircleButton { AccessibleName = "完成：" + task.Text, TabStop = true };
        complete.Click += (_, _) => finish();
        text = new Label { Text = task.Text, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, UseMnemonic = false, Cursor = Cursors.Hand, AccessibleName = task.Text };
        age = new Label { ForeColor = Ui.Muted, TextAlign = ContentAlignment.MiddleRight, Font = Ui.Small };
        text.DoubleClick += (_, _) => edit(); DoubleClick += (_, _) => edit();
        ContextMenuStrip = text.ContextMenuStrip = age.ContextMenuStrip = TaskMenu;
        tip.SetToolTip(text, task.Text); tip.SetToolTip(complete, "确认完成");
        Controls.AddRange([complete, text, age]);
        foreach (Control control in Controls)
        {
            control.MouseEnter += (_, _) => BackColor = Color.FromArgb(238, 241, 245);
            control.MouseLeave += (_, _) => BackColor = Color.White;
        }
    }
    public void ArrangeRow()
    {
        int S(int n) => (int)Math.Round(n * DeviceDpi / 96.0);
        int textWidth = Math.Max(S(70), Width - S(102));
        int height = Math.Clamp(TextRenderer.MeasureText(task.Text, Font, new Size(textWidth, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height, S(24), S(86));
        Height = height + S(16);
        // All three elements share the row's vertical centre, including wrapped text.
        complete.SetBounds(S(4), (Height - S(28)) / 2, S(28), S(28));
        text.SetBounds(S(38), S(8), textWidth, height);
        age.SetBounds(Width - S(59), S(8), S(51), height);
    }
    public void UpdateAge(DateTimeOffset now) => age.Text = $"{TaskAge.Days(task, now)}天";
    protected override void Dispose(bool disposing) { if (disposing) { tip.Dispose(); TaskMenu.Dispose(); } base.Dispose(disposing); }
}

internal sealed class CircleButton : Button
{
    public CircleButton() { FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        float size = Math.Min(Width, Height) * .58f;
        using var pen = new Pen(Focused ? Ui.Ink : Ui.Muted, Math.Max(1.5f, DeviceDpi / 72f));
        e.Graphics.DrawEllipse(pen, (Width - size) / 2, (Height - size) / 2, size, size);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle);
    }
}
