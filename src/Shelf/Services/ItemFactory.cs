using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Shelf.Models;
using Shelf.Storage;

namespace Shelf.Services;

/// 把 IDataObject（剪貼簿或拖入）轉成 ShelfItem。
///
/// Capture 必須在 UI 執行緒、拿到 IDataObject 的當下呼叫：它只把資料取出來（IDataObject 過了就失效）。
/// 回傳的 builder 才做雜湊、落地、縮圖這些重活，交給 Task.Run 在背景跑。
static class ItemFactory
{
    const int InlineTextLimit = 4096;   // UTF-8 bytes
    const int TitleLength = 80;

    /// preferImage：拖入時 true（瀏覽器拖圖會順便附 URL 文字，要的是圖）；
    /// 剪貼簿時 false（Excel / Word 複製會順便附一張截圖，要的是文字）。
    /// 回傳 null 表示沒有可收的內容。
    public static Func<ShelfItem?>? Capture(IDataObject data, bool preferImage, AppInfo? source, ShelfStore store)
    {
        // 檔案永遠最先判斷，否則會被當成路徑文字收進來
        if (Get(data, DataFormats.FileDrop) is string[] dropped)
        {
            var paths = Collectable(dropped);
            return paths.Length > 0 ? () => FromFiles(paths, source, store) : null;
        }

        var text = Get(data, DataFormats.UnicodeText) as string;
        bool hasText = !string.IsNullOrWhiteSpace(text);
        if (hasText && !preferImage)
            return () => FromText(text!, source, store);

        // 優先取 PNG：保留透明度，且不用重新編碼；沒有才退回 Bitmap（由 DIB 轉來）
        var png = (Get(data, "PNG") as MemoryStream)?.ToArray();
        var bmp = png is null ? Get(data, DataFormats.Bitmap) as Bitmap : null;
        if (png is not null || bmp is not null)
            return () => FromImage(png, bmp, source, store);

        return hasText ? () => FromText(text!, source, store) : null;
    }

    public static bool HasSupportedData(IDataObject data) =>
        Get(data, DataFormats.FileDrop) is string[] paths
            ? Collectable(paths).Length > 0
            : data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent("PNG") || data.GetDataPresent(DataFormats.Bitmap);

    /// 捷徑（.lnk）不收：使用者不需要暫存捷徑
    public static string[] Collectable(string[] paths) =>
        [.. paths.Where(p => !p.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))];

    /// 某些來源（遠端桌面、舊程式）的 IDataObject 取資料時會丟 COMException
    static object? Get(IDataObject data, string format)
    {
        try { return data.GetDataPresent(format) ? data.GetData(format) : null; }
        catch (ExternalException) { return null; }
    }

    static ShelfItem? FromFiles(string[] paths, AppInfo? source, ShelfStore store)
    {
        var joined = string.Join('\n', paths);
        var hash = Hash(Encoding.UTF8.GetBytes(joined.ToLowerInvariant()));
        if (store.IsLatest(hash)) return null;

        long size = 0;
        foreach (var p in paths)
        {
            try { var fi = new FileInfo(p); if (fi.Exists) size += fi.Length; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        var name = Path.GetFileName(paths[0].TrimEnd('\\'));
        if (name.Length == 0) name = paths[0];   // 磁碟根目錄 C:\

        return new ShelfItem
        {
            Id = Guid.NewGuid(),
            Kind = ItemKind.File,
            CreatedAt = DateTime.Now,
            Title = paths.Length == 1 ? name : $"{name} 等 {paths.Length} 個項目",
            Inline = joined,
            SizeBytes = size,
            ContentHash = hash,
            SourceApp = source?.Name,
            SourcePath = source?.Path,
            IsFolder = paths.Length == 1 && Directory.Exists(paths[0]),
        };
    }

    static ShelfItem? FromText(string text, AppInfo? source, ShelfStore store)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = Hash(bytes);
        if (store.IsLatest(hash)) return null;

        var id = Guid.NewGuid();
        bool inline = bytes.Length <= InlineTextLimit;
        return new ShelfItem
        {
            Id = id,
            Kind = ItemKind.Text,
            CreatedAt = DateTime.Now,
            Title = MakeTitle(text),
            Inline = inline ? text : null,
            BlobPath = inline ? null : store.Blobs.Write(id, ".txt", bytes),
            SizeBytes = bytes.Length,
            ContentHash = hash,
            SourceApp = source?.Name,
            SourcePath = source?.Path,
        };
    }

    /// 存 PNG → 產縮圖 → 立刻釋放原圖（5.3）
    static ShelfItem? FromImage(byte[]? png, Bitmap? bmp, AppInfo? source, ShelfStore store)
    {
        using var ms = png is null ? null : new MemoryStream(png);
        using Image img = bmp ?? Image.FromStream(ms!);

        if (png is null)
        {
            using var encoded = new MemoryStream();
            img.Save(encoded, ImageFormat.Png);
            png = encoded.ToArray();
        }

        var hash = Hash(png);
        if (store.IsLatest(hash)) return null;

        var id = Guid.NewGuid();
        var path = store.Blobs.Write(id, ".png", png);
        store.Blobs.WriteThumb(id, img);
        return new ShelfItem
        {
            Id = id,
            Kind = ItemKind.Image,
            CreatedAt = DateTime.Now,
            Title = $"圖片 {img.Width}×{img.Height}",
            BlobPath = path,
            SizeBytes = png.Length,
            ContentHash = hash,
            SourceApp = source?.Name,
            SourcePath = source?.Path,
        };
    }

    /// 連續空白（含換行）壓成一個空格，取前 80 字
    static string MakeTitle(string text)
    {
        var sb = new StringBuilder(TitleLength);
        bool pendingSpace = false;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c)) { pendingSpace = sb.Length > 0; continue; }
            if (pendingSpace) { sb.Append(' '); pendingSpace = false; }
            sb.Append(c);
            if (sb.Length >= TitleLength) break;
        }
        return sb.ToString();
    }

    static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes).AsSpan(0, 16));
}
