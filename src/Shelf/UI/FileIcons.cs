using System.Runtime.InteropServices;
using Shelf.Models;

namespace Shelf.UI;

/// 系統檔案圖示，依副檔名共用一份（5.3）。
/// 用 SHGFI_USEFILEATTRIBUTES 只看副檔名、不碰磁碟，檔案不存在或在網路磁碟也不會卡。
static class FileIcons
{
    const string FolderKey = "<folder>";
    static readonly Dictionary<string, Icon?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static Icon? Get(ShelfItem item)
    {
        var firstPath = item.Inline?.Split('\n')[0] ?? "";
        var key = item.IsFolder ? FolderKey : Path.GetExtension(firstPath);
        if (Cache.TryGetValue(key, out var icon)) return icon;

        var info = new Native.SHFILEINFO();
        var r = Native.SHGetFileInfo(
            item.IsFolder ? "folder" : "file" + key,
            item.IsFolder ? Native.FILE_ATTRIBUTE_DIRECTORY : Native.FILE_ATTRIBUTE_NORMAL,
            ref info, (uint)Marshal.SizeOf<Native.SHFILEINFO>(),
            Native.SHGFI_ICON | Native.SHGFI_LARGEICON | Native.SHGFI_USEFILEATTRIBUTES);

        if (r != IntPtr.Zero && info.hIcon != IntPtr.Zero)
        {
            icon = (Icon)Icon.FromHandle(info.hIcon).Clone();
            Native.DestroyIcon(info.hIcon);
        }
        return Cache[key] = icon;
    }

    static readonly Dictionary<string, Icon?> AppCache = new(StringComparer.OrdinalIgnoreCase);

    /// 來源程式自己的小圖示（清單次行用）。exe 被移除就回傳 null，只顯示時間
    public static Icon? App(string exePath)
    {
        if (AppCache.TryGetValue(exePath, out var icon)) return icon;
        var info = new Native.SHFILEINFO();
        var r = Native.SHGetFileInfo(exePath, 0, ref info, (uint)Marshal.SizeOf<Native.SHFILEINFO>(),
            Native.SHGFI_ICON | Native.SHGFI_SMALLICON);
        if (r != IntPtr.Zero && info.hIcon != IntPtr.Zero)
        {
            icon = (Icon)Icon.FromHandle(info.hIcon).Clone();
            Native.DestroyIcon(info.hIcon);
        }
        return AppCache[exePath] = icon;
    }
}
