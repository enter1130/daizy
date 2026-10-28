using System.Collections.Specialized;
using Shelf.Models;
using Shelf.Storage;

namespace Shelf.Services;

/// ShelfItem → DataObject，拖出（F7）與寫入剪貼簿（F8、F9）共用（6.4）。
static class DragOutBuilder
{
    /// 內容已不存在（檔案被刪、blob 遺失）時回傳 null，呼叫端要提示而不是啟動拖曳。
    /// 回傳值用完要 Dispose：圖片會暫時載入整張 Bitmap。
    public static OutData? Build(ShelfItem item, BlobStore blobs)
    {
        var data = new DataObject();
        // F6：自己寫入剪貼簿的不要再收；拖回自己身上也據此忽略
        data.SetData(ClipboardMonitor.SelfFormat, new MemoryStream([1]));

        switch (item.Kind)
        {
            case ItemKind.File:
            {
                var files = new StringCollection();
                foreach (var p in (item.Inline ?? "").Split('\n'))
                    if (File.Exists(p) || Directory.Exists(p)) files.Add(p);
                if (files.Count == 0) return null;
                data.SetFileDropList(files);
                // 檔案總管貼上時用「複製」而不是「移動」
                data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Copy)));
                return new OutData(data, null);
            }

            case ItemKind.Text:
            {
                var text = item.Inline ?? ReadBlobText(item, blobs);
                if (text is null) return null;
                data.SetText(text, TextDataFormat.UnicodeText);
                return new OutData(data, null);
            }

            default:
            {
                if (item.BlobPath is null) return null;
                var path = blobs.FullPath(item.BlobPath);
                if (!File.Exists(path)) return null;

                var png = File.ReadAllBytes(path);
                Bitmap bitmap;
                using (var ms = new MemoryStream(png))
                using (var decoded = Image.FromStream(ms))
                    bitmap = new Bitmap(decoded);

                // 同時給三種：PNG（保留透明）、Bitmap（Word、小畫家）、檔案（拖到檔案總管變成檔案）
                data.SetData("PNG", false, new MemoryStream(png));
                data.SetImage(bitmap);
                data.SetFileDropList(new StringCollection { path });
                return new OutData(data, bitmap);
            }
        }
    }

    static string? ReadBlobText(ShelfItem item, BlobStore blobs)
    {
        if (item.BlobPath is null) return null;
        try { return File.ReadAllText(blobs.FullPath(item.BlobPath)); }
        catch (IOException) { return null; }
    }
}

sealed class OutData(DataObject data, Bitmap? bitmap) : IDisposable
{
    public DataObject Data => data;
    public void Dispose() => bitmap?.Dispose();
}
