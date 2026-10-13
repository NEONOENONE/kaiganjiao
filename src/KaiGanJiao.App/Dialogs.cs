using KaiGanJiao.Core;

namespace KaiGanJiao.App;

internal static class Ui
{
    public static readonly Color Ink = Color.FromArgb(40, 45, 54);
    public static readonly Color Muted = Color.FromArgb(112, 119, 130);
    public static readonly Font Title = new("Microsoft YaHei UI", 12, FontStyle.Bold);
    public static readonly Font Small = new("Microsoft YaHei UI", 8.5f);
    public static Button Button(string text) => new() { Text = text, AutoSize = true, MinimumSize = new Size(76, 34), FlatStyle = FlatStyle.System, Margin = new Padding(4) };
    public static void SetDialog(Form form, string title, int width)
    {
        form.Text = title;
        form.Font = SystemFonts.MessageBoxFont;
        form.AutoScaleMode = AutoScaleMode.Dpi;
        form.AutoScaleDimensions = new SizeF(96, 96);
        form.StartPosition = FormStartPosition.CenterParent;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.MaximizeBox = false;
        form.MinimizeBox = false;
        form.ShowInTaskbar = false;
        form.ClientSize = new Size(width, 330);
        form.BackColor = Color.White;
        form.ForeColor = Ink;
        form.Padding = new Padding(18);
    }
}

public sealed class TaskEditor : Form
{
    private readonly TextBox input = new() { Multiline = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, AccessibleName = "任务内容", MaxLength = 0 };
    private readonly RadioButton[] modules;
    private readonly Label error = new() { AutoSize = true, ForeColor = Color.Firebrick };
    public string TaskText => input.Text.Trim();
    public int ModuleId => (int)modules.First(b => b.Checked).Tag!;
    public TaskEditor(int module, TodoItem? task = null)
    {
        Ui.SetDialog(this, task == null ? "记下一件事" : "编辑任务", 410);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = "要做什么？", Font = Ui.Title, AutoSize = true, Margin = new Padding(0, 0, 0, 10) });
        input.Text = task?.Text ?? "";
        layout.Controls.Add(input);
        var choices = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0, 10, 0, 4) };
        modules = Modules.All.Select(m => new RadioButton { Text = m.Title, Appearance = Appearance.Button, AutoSize = true,
            TextAlign = ContentAlignment.MiddleCenter, Padding = new Padding(4), Margin = new Padding(3), Tag = m.Id,
            Checked = m.Id == (task?.ModuleId ?? module) }).ToArray();
        choices.Controls.AddRange(modules);
        layout.Controls.Add(choices);
        layout.Controls.Add(error);
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var cancel = Ui.Button("取消"); cancel.DialogResult = DialogResult.Cancel;
        var save = Ui.Button(task == null ? "添加" : "保存");
        save.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(TaskText)) { error.Text = "请先写下要做的事。"; input.Focus(); return; }
            DialogResult = DialogResult.OK;
        };
        actions.Controls.Add(cancel); actions.Controls.Add(save);
        layout.Controls.Add(actions); Controls.Add(layout);
        AcceptButton = save; CancelButton = cancel;
        Shown += (_, _) => { input.Focus(); input.SelectionStart = input.TextLength; };
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Enter) { ((Button)AcceptButton!).PerformClick(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
}

public sealed class ConfirmDialog : Form
{
    public ConfirmDialog(string task, bool completion)
    {
        Ui.SetDialog(this, completion ? "确认完成" : "删除任务", 420);
        ClientSize = new Size(420, 290);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label { Text = completion ? "还有什么遗漏的吗？" : "确定删除这件事吗？", AutoSize = true, Font = Ui.Title, Margin = new Padding(0, 0, 0, 12) });
        layout.Controls.Add(new TextBox { Text = task, ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None, BackColor = Color.White, Dock = DockStyle.Fill, TabStop = false, AccessibleName = "当前任务" });
        var yes = Ui.Button(completion ? "是的，我已经全部完成了" : "删除任务");
        var no = Ui.Button(completion ? "等等，好像还差一点" : "保留任务");
        yes.Dock = no.Dock = DockStyle.Fill;
        yes.DialogResult = DialogResult.OK; no.DialogResult = DialogResult.Cancel;
        layout.Controls.Add(yes); layout.Controls.Add(no); Controls.Add(layout);
        CancelButton = no; AcceptButton = no;
        Shown += (_, _) => no.Focus();
    }
}
