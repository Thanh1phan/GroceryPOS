using System.Text.Json;
using GroceryPOS.UI.Controls.Theming;
using Serilog;

namespace GroceryPOS.App.Preferences;

/// <summary>
/// Per-machine display preferences (light/dark theme) kept in <c>%LocalAppData%\GroceryPOS\ui-preferences.json</c>.
/// Unlike the store settings they are not shared through the database: the counter by the window may want the light
/// theme while the back office prefers dark. Saved whenever the theme changes (top bar toggle or Cấu hình → Giao diện).
/// </summary>
internal static class UiPreferences
{
    private static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GroceryPOS", "ui-preferences.json");

    private sealed record Data(string Theme);

    /// <summary>Applies the saved theme and starts saving later theme changes.</summary>
    public static void Initialize()
    {
        try
        {
            if (File.Exists(FilePath)
                && JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) is { } data
                && Enum.TryParse<ThemeMode>(data.Theme, ignoreCase: true, out var mode))
            {
                ThemeManager.SetMode(mode);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warning(exception, "Không đọc được tùy chọn giao diện {Path}", FilePath);
        }

        ThemeManager.ThemeChanged += (_, e) => Save(e.Current.Mode);
    }

    private static void Save(ThemeMode mode)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(new Data(mode.ToString())));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warning(exception, "Không lưu được tùy chọn giao diện {Path}", FilePath);
        }
    }
}
