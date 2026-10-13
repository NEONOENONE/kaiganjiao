using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace KaiGanJiao.App;

public enum ScreenEdge { None, Left, Right, Top }
public static class EdgeGeometry
{
    public static ScreenEdge Detect(Rectangle window, Rectangle screen, Rectangle work, IEnumerable<Rectangle> others, int threshold)
    {
        var choices = new[] { (ScreenEdge.Top, Math.Abs(window.Top - screen.Top)),
            (ScreenEdge.Left, Math.Abs(window.Left - screen.Left)), (ScreenEdge.Right, Math.Abs(window.Right - screen.Right)) };
        foreach (var (edge, distance) in choices.OrderBy(c => c.Item2))
        {
            if (distance > threshold) continue;
            if ((edge == ScreenEdge.Top && work.Top != screen.Top) ||
                (edge == ScreenEdge.Left && work.Left != screen.Left) ||
                (edge == ScreenEdge.Right && work.Right != screen.Right)) continue;
            var outside = edge switch {
                ScreenEdge.Left => new Rectangle(screen.Left - 1, window.Top, 1, window.Height),
                ScreenEdge.Right => new Rectangle(screen.Right, window.Top, 1, window.Height),
                _ => new Rectangle(window.Left, screen.Top - 1, window.Width, 1) };
            if (!others.Any(s => s.IntersectsWith(outside))) return edge;
        }
        return ScreenEdge.None;
    }
    public static Rectangle Fit(Rectangle window, Rectangle work) => new(
        Math.Clamp(window.X, work.Left, Math.Max(work.Left, work.Right - window.Width)),
        Math.Clamp(window.Y, work.Top, Math.Max(work.Top, work.Bottom - window.Height)),
        Math.Min(window.Width, work.Width), Math.Min(window.Height, work.Height));
    public static Rectangle Snap(Rectangle window, Rectangle work, ScreenEdge edge)
    {
        var result = Fit(window, work);
        return edge switch { ScreenEdge.Left => result with { X = work.Left },
            ScreenEdge.Right => result with { X = work.Right - result.Width },
            ScreenEdge.Top => result with { Y = work.Top }, _ => result };
    }
    public static Rectangle Strip(Rectangle window, ScreenEdge edge, int size) => edge switch {
        ScreenEdge.Left => new Rectangle(window.Left, window.Top, size, window.Height),
        ScreenEdge.Right => new Rectangle(window.Right - size, window.Top, size, window.Height),
        _ => new Rectangle(window.Left, window.Top, window.Width, size) };
}

// Event-driven pointer tracking: the one-shot timer exists only during the hide delay.
public sealed class EdgeAutoHide : IMessageFilter, IDisposable
{
    private readonly Form owner;
    private readonly Func<bool> busy;
    private readonly System.Windows.Forms.Timer delay = new() { Interval = 550 };
    private readonly EdgeStrip strip = new();
    private readonly List<Control> tracked = [];
    private bool enabled, moving, disposed, awaitingPointerEntry;
    public ScreenEdge DockedEdge { get; private set; }
    public bool IsCollapsed => strip.Visible;
    public EdgeAutoHide(Form owner, Func<bool> busy)
    {
        this.owner = owner; this.busy = busy;
        delay.Tick += (_, _) => { delay.Stop(); TryCollapse(); };
        strip.MouseEnter += (_, _) => Reveal(false);
        strip.MouseDown += (_, _) => Reveal(true);
        owner.ResizeBegin += Moving;
        owner.ResizeEnd += Moved;
        owner.Deactivate += PointerChanged;
        owner.VisibleChanged += VisibilityChanged;
        SystemEvents.DisplaySettingsChanged += DisplaysChanged;
        Application.AddMessageFilter(this);
        Track(owner);
    }
    private void Track(Control c)
    {
        if (tracked.Contains(c)) return;
        tracked.Add(c); c.MouseLeave += PointerChanged; c.ControlAdded += Added; c.Disposed += Removed;
        foreach (Control child in c.Controls) Track(child);
    }
    private void Added(object? sender, ControlEventArgs e) { if (e.Control != null) Track(e.Control); }
    private void Removed(object? sender, EventArgs e) { if (sender is Control c) { Untrack(c); tracked.Remove(c); } }
    private void Untrack(Control c) { c.MouseLeave -= PointerChanged; c.ControlAdded -= Added; c.Disposed -= Removed; }
    public void SetEnabled(bool value)
    {
        enabled = value; delay.Stop();
        if (!value) { if (IsCollapsed) Reveal(true); DockedEdge = ScreenEdge.None; }
        else CheckDock();
    }
    private void Moving(object? sender, EventArgs e) { moving = true; delay.Stop(); }
    private void Moved(object? sender, EventArgs e) { moving = false; CheckDock(); }
    public void CheckDock()
    {
        delay.Stop(); awaitingPointerEntry = false;
        if (!enabled || !owner.Visible || owner.WindowState != FormWindowState.Normal) { DockedEdge = ScreenEdge.None; return; }
        var screen = Screen.FromRectangle(owner.Bounds);
        DockedEdge = EdgeGeometry.Detect(owner.Bounds, screen.Bounds, screen.WorkingArea,
            Screen.AllScreens.Where(s => s.DeviceName != screen.DeviceName).Select(s => s.Bounds), Math.Max(12, owner.DeviceDpi / 6));
        if (DockedEdge != ScreenEdge.None) owner.Bounds = EdgeGeometry.Snap(owner.Bounds, screen.WorkingArea, DockedEdge);
        EvaluatePointer();
    }
    public void EvaluatePointer()
    {
        if (awaitingPointerEntry)
        {
            if (!owner.Bounds.Contains(Cursor.Position)) { delay.Stop(); return; }
            awaitingPointerEntry = false;
        }
        if (disposed || !enabled || moving || DockedEdge == ScreenEdge.None || !owner.Visible ||
            owner.WindowState != FormWindowState.Normal || busy() || owner.Bounds.Contains(Cursor.Position)) { delay.Stop(); return; }
        if (!delay.Enabled) delay.Start();
    }
    public bool TryCollapse()
    {
        if (!enabled || moving || awaitingPointerEntry || DockedEdge == ScreenEdge.None || !owner.Visible || !owner.Enabled ||
            owner.WindowState != FormWindowState.Normal || busy() || owner.Bounds.Contains(Cursor.Position)) return false;
        strip.Bounds = EdgeGeometry.Strip(owner.Bounds, DockedEdge, Math.Max(5, owner.DeviceDpi / 16));
        // Hide the body rather than placing it on a neighbouring monitor. The strip remains accessible.
        strip.Show(); owner.Hide(); return true;
    }
    public void Reveal(bool activate)
    {
        delay.Stop(); strip.Hide();
        if (owner.WindowState == FormWindowState.Minimized) owner.WindowState = FormWindowState.Normal;
        owner.Bounds = EdgeGeometry.Fit(owner.Bounds, Screen.FromRectangle(owner.Bounds).WorkingArea);
        // A tray / second-launch reveal must remain open long enough for the user to reach it.
        awaitingPointerEntry = activate && !owner.Bounds.Contains(Cursor.Position);
        owner.Show(); owner.BringToFront(); if (activate) owner.Activate();
    }
    public void HideToTray() { delay.Stop(); strip.Hide(); owner.Hide(); }
    private void PointerChanged(object? sender, EventArgs e) => EvaluatePointer();
    private void VisibilityChanged(object? sender, EventArgs e) { if (!owner.Visible) delay.Stop(); }
    private void DisplaysChanged(object? sender, EventArgs e)
    {
        if (owner.IsHandleCreated && !owner.IsDisposed)
            try { owner.BeginInvoke(() => { if (IsCollapsed) Reveal(true); if (owner.Visible) { owner.Bounds = EdgeGeometry.Fit(owner.Bounds, Screen.FromRectangle(owner.Bounds).WorkingArea); CheckDock(); } }); }
            catch (InvalidOperationException) { }
    }
    public bool PreFilterMessage(ref Message m)
    {
        if (enabled && m.Msg == 0xA0 && m.HWnd == owner.Handle)
        {
            var tracking = new MouseTracking { Size = (uint)Marshal.SizeOf<MouseTracking>(), Flags = 0x12, Window = m.HWnd };
            TrackMouseEvent(ref tracking); // Receive leave events after crossing the native title bar / resize border.
        }
        if (m.Msg is 0x200 or 0xA0 or 0x2A2 or 0x2A3) EvaluatePointer();
        return false;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseTracking { public uint Size; public uint Flags; public IntPtr Window; public uint HoverTime; }
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TrackMouseEvent(ref MouseTracking tracking);
    public void Dispose()
    {
        disposed = true; Application.RemoveMessageFilter(this); SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
        owner.ResizeBegin -= Moving; owner.ResizeEnd -= Moved; owner.Deactivate -= PointerChanged; owner.VisibleChanged -= VisibilityChanged;
        foreach (var c in tracked.ToArray()) Untrack(c); tracked.Clear(); delay.Dispose(); strip.Dispose();
    }
    private sealed class EdgeStrip : Form
    {
        public EdgeStrip() { Text = "开干角 · 移入展开"; FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual; TopMost = true; BackColor = Color.FromArgb(80, 137, 218); Cursor = Cursors.Hand; }
        protected override bool ShowWithoutActivation => true;
        protected override CreateParams CreateParams { get { var cp = base.CreateParams; cp.ExStyle |= 0x08000080; return cp; } }
    }
}
