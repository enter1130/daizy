using System.Text.Json;
using Shelf.Models;

namespace Shelf.Storage;

/// 記憶體清單 + index.json。清單只在 UI 執行緒異動；存檔在計時器執行緒，以 _lock 保護。
sealed class ShelfStore : IDisposable
{
    public static string Root { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Shelf");

    readonly string _indexPath = Path.Combine(Root, "index.json");
    readonly List<ShelfItem> _items;
    readonly System.Threading.Timer _saveTimer;
    readonly object _lock = new();
    string? _latestHash;
    bool _dirty;

    public ShelfStore()
    {
        Directory.CreateDirectory(Root);
        Blobs = new BlobStore(Root);
        _items = Load();
        _latestHash = _items.FirstOrDefault()?.ContentHash;
        _saveTimer = new System.Threading.Timer(_ => Save());
    }

    public BlobStore Blobs { get; }

    /// 未釘選項目的上限，超過時刪最舊的（F15）
    public int Limit { get; set; } = 200;

    /// 新到舊。只能在 UI 執行緒讀。
    public IReadOnlyList<ShelfItem> Items => _items;

    public long DiskBytes => _items.Where(i => i.BlobPath is not null).Sum(i => i.SizeBytes);

    public event Action? Changed;

    /// 與最新一筆相同（F5）。builder 在背景執行緒落地前先問，省得寫了又刪。
    public bool IsLatest(string hash) => hash == Volatile.Read(ref _latestHash);

    public void Add(ShelfItem item)
    {
        if (IsLatest(item.ContentHash))   // 兩個 builder 同時跑時的第二道關卡
        {
            Blobs.Delete(item);
            return;
        }

        lock (_lock)
        {
            _items.Insert(0, item);
            for (int i = _items.Count - 1, unpinned = _items.Count(x => !x.Pinned); unpinned > Limit && i >= 0; i--)
            {
                if (_items[i].Pinned) continue;
                Blobs.Delete(_items[i]);
                _items.RemoveAt(i);
                unpinned--;
            }
            _dirty = true;
        }
        Commit();
    }

    public void SetPinned(ShelfItem item, bool pinned)
    {
        lock (_lock)
        {
            item.Pinned = pinned;
            _dirty = true;
        }
        Commit();
    }

    public void Remove(ShelfItem item)
    {
        lock (_lock)
        {
            if (!_items.Remove(item)) return;
            _dirty = true;
        }
        Blobs.Delete(item);
        Commit();
    }

    /// 清空未釘選項目（F14），回傳刪了幾筆
    public int ClearUnpinned()
    {
        List<ShelfItem> removed;
        lock (_lock)
        {
            removed = _items.FindAll(i => !i.Pinned);
            _items.RemoveAll(i => !i.Pinned);
            _dirty = true;
        }
        foreach (var item in removed) Blobs.Delete(item);
        Commit();
        return removed.Count;
    }

    /// 異動後：更新去重基準（刪掉最新一筆後，再複製同樣內容要能收）、排程存檔、通知 UI
    void Commit()
    {
        Volatile.Write(ref _latestHash, _items.FirstOrDefault()?.ContentHash);
        _saveTimer.Change(500, Timeout.Infinite);   // debounce
        Changed?.Invoke();
    }

    List<ShelfItem> Load()
    {
        if (!File.Exists(_indexPath)) return [];
        try
        {
            using var fs = File.OpenRead(_indexPath);
            return JsonSerializer.Deserialize(fs, ShelfJsonContext.Default.ListShelfItem) ?? [];
        }
        catch (JsonException)
        {
            // 壞檔留著備查，不要直接覆蓋掉使用者的資料
            File.Move(_indexPath, _indexPath + ".bad", overwrite: true);
            return [];
        }
    }

    void Save()
    {
        lock (_lock)
        {
            if (!_dirty) return;
            var tmp = _indexPath + ".tmp";
            using (var fs = File.Create(tmp))
                JsonSerializer.Serialize(fs, _items, ShelfJsonContext.Default.ListShelfItem);
            File.Move(tmp, _indexPath, overwrite: true);   // 先寫暫存再換名，寫到一半斷電不會壞檔
            _dirty = false;
        }
    }

    public void Dispose()
    {
        _saveTimer.Dispose();
        Save();
    }
}
