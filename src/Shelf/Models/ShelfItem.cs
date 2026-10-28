using System.Text.Json.Serialization;

namespace Shelf.Models;

enum ItemKind { File, Text, Image }

sealed record ShelfItem
{
    public required Guid Id { get; init; }
    public required ItemKind Kind { get; init; }
    public required DateTime CreatedAt { get; init; }
    public bool Pinned { get; set; }

    /// 清單上顯示的一行文字：檔名 / 文字前 80 字 / "圖片 1920×1080"
    public required string Title { get; init; }

    /// File：原始路徑（多檔用 \n 分隔）
    /// Text：若 ≤ 4 KB 直接放這裡，否則為 null 並改存 BlobPath
    /// Image：null
    public string? Inline { get; init; }

    /// 落地檔案的相對路徑（store/{Id}.png 或 .txt），Inline 有值時為 null
    public string? BlobPath { get; init; }

    public long SizeBytes { get; init; }
    public required string ContentHash { get; init; }   // SHA-256 前 16 bytes，用於去重
    public string? SourceApp { get; init; }              // 來源程式名稱（搜尋用）
    public string? SourcePath { get; init; }             // 來源程式 exe 路徑（清單上顯示它的圖示）

    /// File 且為單一資料夾。建立時判斷一次，之後繪製不必碰磁碟
    public bool IsFolder { get; init; }
}

// 只略過 null，不能用 WhenWritingDefault：ItemKind.File 是 0，會被省略，讀回時 required 的 Kind 缺值整份失敗
[JsonSourceGenerationOptions(UseStringEnumConverter = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<ShelfItem>))]
[JsonSerializable(typeof(Settings))]
sealed partial class ShelfJsonContext : JsonSerializerContext
{
}
