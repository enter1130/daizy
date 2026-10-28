using Shelf.Models;

namespace Shelf.Storage;

/// 縮圖 LRU 快取，上限 100 張；超過就 Dispose 最久沒用的，需要時再從 thumbs\ 讀。
sealed class ThumbnailCache(BlobStore blobs, int capacity = 100)
{
    readonly Dictionary<Guid, LinkedListNode<(Guid Id, Image Image)>> _map = [];
    readonly LinkedList<(Guid Id, Image Image)> _lru = new();

    public Image? Get(ShelfItem item)
    {
        if (_map.TryGetValue(item.Id, out var node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            return node.Value.Image;
        }

        var path = blobs.ThumbPath(item.Id);
        if (!File.Exists(path)) return null;

        // Image.FromStream 要求串流在 Image 存活期間保持開啟，所以複製一份再關檔。
        // 不用 Image.FromFile：會鎖住檔案。
        Image image;
        using (var fs = File.OpenRead(path))
        using (var decoded = Image.FromStream(fs))
            image = new Bitmap(decoded);

        _map[item.Id] = _lru.AddFirst((item.Id, image));
        if (_lru.Count > capacity)
        {
            var last = _lru.Last!;
            _lru.RemoveLast();
            _map.Remove(last.Value.Id);
            last.Value.Image.Dispose();
        }
        return image;
    }
}
