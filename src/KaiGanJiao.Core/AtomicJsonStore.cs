using System.Text.Json;

namespace KaiGanJiao.Core;

public sealed class DataRecoveryException(string message) : IOException(message);

public sealed class AtomicJsonStore<T>(string path, Func<T, bool> validate) where T : class
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public string? RecoveryNotice { get; private set; }
    public string Path { get; } = System.IO.Path.GetFullPath(path);
    public T Load(Func<T> create)
    {
        if (!File.Exists(Path) && !File.Exists(Path + ".bak")) return create();
        if (File.Exists(Path))
        {
            try { return Read(Path); }
            catch (Exception e) when (e is JsonException or InvalidDataException) { }
        }
        if (File.Exists(Path + ".bak"))
        {
            T recovered;
            try { recovered = Read(Path + ".bak"); }
            catch (Exception e) when (e is JsonException or InvalidDataException)
            { throw new DataRecoveryException("数据与备份均无法读取。为保护原文件，程序已停止加载；请保留文件后再恢复。"); }
            if (File.Exists(Path)) File.Copy(Path, Path + ".damaged-" + Guid.NewGuid().ToString("N"));
            // Replace corrupt primary without replacing the known-good backup.
            Write(recovered, preserveBackup: true);
            RecoveryNotice = "数据文件异常或缺失，已恢复上一次备份。损坏的原文件（如有）已另存为 .damaged 文件；最近一次修改可能需要重新录入。";
            return recovered;
        }
        throw new DataRecoveryException("数据文件无法读取且没有可用备份。未覆盖原文件，请先备份应用数据目录后再修复。");
    }
    private T Read(string file)
    {
        var value = JsonSerializer.Deserialize<T>(File.ReadAllBytes(file), Options);
        if (value == null || !validate(value)) throw new InvalidDataException("数据结构或版本不受支持。");
        return value;
    }
    public void Save(T value)
    {
        if (!validate(value)) throw new InvalidDataException("拒绝保存无效数据。");
        Write(value, false);
    }
    private void Write(T value, bool preserveBackup)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(stream, value, Options);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(Path)) File.Replace(temp, Path, preserveBackup ? null : Path + ".bak");
            else
            {
                File.Move(temp, Path);
                if (!File.Exists(Path + ".bak")) File.Copy(Path, Path + ".bak");
            }
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
