using System.Drawing.Imaging;
using Shelf.Models;

namespace Shelf.Storage;

/// 大內容落地：store\{id}.png / .txt，以及 thumbs\{id}.jpg 縮圖。
sealed class BlobStore(string root)
{
    const int ThumbShortSide = 96;

    readonly string _thumbs = Path.Combine(root, "thumbs");

    static readonly ImageCodecInfo Jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

    /// 回傳相對於 root 的路徑，存進 ShelfItem.BlobPath
    public string Write(Guid id, string ext, byte[] bytes)
    {
        var rel = Path.Combine("store", id + ext);
        var full = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllBytes(full, bytes);
        return rel;
    }

    public string FullPath(string rel) => Path.Combine(root, rel);

    public string ThumbPath(Guid id) => Path.Combine(_thumbs, id + ".jpg");

    /// 短邊縮到 96 px 的 JPEG（品質 75）。透明處補白底，否則 JPEG 會變黑。
    public void WriteThumb(Guid id, Image src)
    {
        double scale = Math.Min(1.0, (double)ThumbShortSide / Math.Min(src.Width, src.Height));
        int w = Math.Max(1, (int)(src.Width * scale));
        int h = Math.Max(1, (int)(src.Height * scale));

        using var thumb = new Bitmap(w, h);
        using (var g = Graphics.FromImage(thumb))
        {
            g.Clear(Color.White);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, 0, 0, w, h);
        }

        Directory.CreateDirectory(_thumbs);
        using var p = new EncoderParameters(1);
        p.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 75L);
        thumb.Save(ThumbPath(id), Jpeg, p);
    }

    public void Delete(ShelfItem item)
    {
        if (item.BlobPath is { } rel) TryDelete(FullPath(rel));
        TryDelete(ThumbPath(item.Id));
    }

    static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
