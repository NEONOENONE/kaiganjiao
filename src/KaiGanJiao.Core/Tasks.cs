namespace KaiGanJiao.Core;

public sealed record Module(int Id, string Title, string Subtitle, string Color);
public static class Modules
{
    public static readonly IReadOnlyList<Module> All = Array.AsReadOnly(new[] {
        new Module(1, "马上开干", "别再准备了，这两天就做", "#D95252"),
        new Module(2, "这周拿下", "稍微准备，一周内搞定", "#D4A122"),
        new Module(3, "查完接棒", "先查资料，前两组完成后接上", "#5089DA"),
        new Module(4, "灵感收纳", "想到什么就先丢进来", "#9299A4") });
    public static bool Valid(int id) => id is >= 1 and <= 4;
}
public sealed record TodoItem(Guid Id, string Text, int ModuleId, DateTimeOffset CreatedUtc,
    bool IsCompleted = false, DateTimeOffset? CompletedUtc = null);
public sealed record TaskDocument(int Version, List<TodoItem> Tasks);
public sealed record AppSettings(int Version = 1, int DefaultModuleId = 2,
    bool EdgeAutoHide = false, bool CloseToTray = false, bool StartWithWindows = false);

public static class TaskAge
{
    public static int Days(TodoItem task, DateTimeOffset now) =>
        Math.Max(0, (int)Math.Floor((now - task.CreatedUtc).TotalDays));

    // One wake-up at the nearest 24-hour boundary; no periodic per-second polling.
    public static TimeSpan? UntilNextBoundary(IEnumerable<TodoItem> tasks, DateTimeOffset now)
    {
        var pending = tasks.Where(t => !t.IsCompleted).Select(t =>
            t.CreatedUtc > now ? t.CreatedUtc.AddDays(1) - now :
            t.CreatedUtc.AddDays(Days(t, now) + 1) - now).ToArray();
        return pending.Length == 0 ? null : pending.Min();
    }
}

public sealed class TaskService
{
    private readonly AtomicJsonStore<TaskDocument> store;
    private readonly Func<DateTimeOffset> clock;
    private List<TodoItem> tasks;
    public IReadOnlyList<TodoItem> Items => tasks.AsReadOnly();
    public TaskService(AtomicJsonStore<TaskDocument> store, Func<DateTimeOffset>? clock = null)
    {
        this.store = store;
        this.clock = clock ?? (() => DateTimeOffset.UtcNow);
        tasks = store.Load(() => new TaskDocument(1, [])).Tasks;
    }
    public static bool IsValid(TaskDocument doc) => doc.Version == 1 && doc.Tasks != null &&
        doc.Tasks.All(t => t != null && t.Id != Guid.Empty && !string.IsNullOrWhiteSpace(t.Text) &&
            Modules.Valid(t.ModuleId) && t.CreatedUtc != default && t.CreatedUtc.Offset == TimeSpan.Zero &&
            (t.IsCompleted == t.CompletedUtc.HasValue) &&
            (!t.CompletedUtc.HasValue || (t.CompletedUtc.Value != default && t.CompletedUtc.Value.Offset == TimeSpan.Zero))) &&
        doc.Tasks.Select(t => t.Id).Distinct().Count() == doc.Tasks.Count;
    public TodoItem Add(string text, int module)
    {
        Validate(text, module);
        var task = new TodoItem(Guid.NewGuid(), text.Trim(), module, clock().ToUniversalTime());
        Commit([.. tasks, task]);
        return task;
    }
    public void Edit(Guid id, string text, int module)
    {
        Validate(text, module);
        Replace(id, t => t with { Text = text.Trim(), ModuleId = module });
    }
    public void Complete(Guid id) => Replace(id, t => t.IsCompleted ? t :
        t with { IsCompleted = true, CompletedUtc = clock().ToUniversalTime() });
    public void Delete(Guid id)
    {
        if (!tasks.Any(t => t.Id == id)) throw new InvalidOperationException("任务不存在。");
        Commit(tasks.Where(t => t.Id != id).ToList());
    }
    private static void Validate(string text, int module)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("请先写下要做的事。");
        if (!Modules.Valid(module)) throw new ArgumentException("请选择一个模块。");
    }
    private void Replace(Guid id, Func<TodoItem, TodoItem> update)
    {
        if (!tasks.Any(t => t.Id == id)) throw new InvalidOperationException("任务不存在。");
        Commit(tasks.Select(t => t.Id == id ? update(t) : t).ToList());
    }
    private void Commit(List<TodoItem> next)
    {
        store.Save(new TaskDocument(1, next)); // Do not change the UI state if disk write fails.
        tasks = next;
    }
}
