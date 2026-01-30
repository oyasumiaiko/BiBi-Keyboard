using System.Text.Json;

namespace BiBiVoiceWin;

internal sealed record DialogContextEntry(string Summary, DateTimeOffset UpdatedAt);

/// <summary>
/// 对话上下文持久化存储（每个窗口一个摘要）。
/// 说明：用于在多次识别间复用“领域摘要”，避免每次从零开始。
/// </summary>
internal sealed class DialogContextStore
{
    private sealed class StoreFile
    {
        public int Version { get; set; } = 1;
        public Dictionary<string, DialogContextEntry> Items { get; set; } = new();
    }

    private readonly string _path;
    private readonly object _lock = new();
    private Dictionary<string, DialogContextEntry> _items = new(StringComparer.OrdinalIgnoreCase);

    public DialogContextStore(string path)
    {
        _path = path;
        Load();
    }

    public string? GetSummary(string key, TimeSpan ttl)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        lock (_lock)
        {
            if (!_items.TryGetValue(key, out var entry)) return null;
            if (ttl > TimeSpan.Zero && DateTimeOffset.UtcNow - entry.UpdatedAt > ttl)
            {
                _items.Remove(key);
                Save();
                return null;
            }
            return entry.Summary;
        }
    }

    public void SetSummary(string key, string summary)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(summary)) return;
        lock (_lock)
        {
            _items[key] = new DialogContextEntry(summary, DateTimeOffset.UtcNow);
            Save();
        }
    }

    private void Load()
    {
        if (!File.Exists(_path)) return;
        try
        {
            var json = File.ReadAllText(_path);
            var file = JsonSerializer.Deserialize<StoreFile>(json);
            if (file?.Items is not null)
            {
                _items = new Dictionary<string, DialogContextEntry>(file.Items, StringComparer.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // 读失败不影响主流程
            _items = new Dictionary<string, DialogContextEntry>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
            var file = new StoreFile { Items = _items };
            var json = JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
        catch
        {
            // 写失败不影响主流程
        }
    }
}
