using System.Text.Json;
using Shelf.Storage;

namespace Shelf.Models;

enum ThemeMode { System, Light, Dark }

/// 使用者設定（7.7），存在 settings.json
sealed class Settings
{
    public bool AutoStart { get; set; }
    public uint HotkeyModifiers { get; set; } = Native.MOD_WIN | Native.MOD_ALT;
    public uint HotkeyKey { get; set; } = 0x56;   // V
    public ThemeMode Theme { get; set; }
    public bool CollectClipboard { get; set; } = true;
    public int Limit { get; set; } = 200;

    /// 浮動球左上角的螢幕座標（實體像素）。null 表示還沒拖過，用預設位置
    public int? BubbleX { get; set; }
    public int? BubbleY { get; set; }

    static string FilePath => Path.Combine(ShelfStore.Root, "settings.json");

    public static Settings Load()
    {
        try
        {
            using var fs = File.OpenRead(FilePath);
            return JsonSerializer.Deserialize(fs, ShelfJsonContext.Default.Settings) ?? new();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return new();   // 第一次執行或檔案壞了：用預設值
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(ShelfStore.Root);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, ShelfJsonContext.Default.Settings));
    }

    public Settings Clone() => (Settings)MemberwiseClone();
}
