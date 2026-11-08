using System.Text.Json;

namespace Vakage;

/// <summary>
/// Machine-local settings and logs live in %LOCALAPPDATA%\Vakage. The archive
/// itself (the library) lives wherever <see cref="AppSettings.LibraryPath"/>
/// says, by default Documents\Vakage.
/// </summary>
internal static class AppPaths
{
    public static string UserDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Vakage");

    public static string SettingsPath => Path.Combine(UserDataDirectory, "settings.json");

    public static string DefaultLibrary => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "Vakage");
}

internal sealed class AppSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>The folder holding vakage.sqlite and the file store.</summary>
    public string? LibraryPath { get; set; }

    /// <summary>The account last shown, so the app opens on it again.</summary>
    public string? LastAccount { get; set; }

    public string ResolvedLibraryPath => string.IsNullOrWhiteSpace(LibraryPath) ? AppPaths.DefaultLibrary : LibraryPath;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsPath)) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            AppLog.Error("Settings could not be read; using defaults", ex);
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.UserDataDirectory);
            File.WriteAllText(AppPaths.SettingsPath, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex)
        {
            AppLog.Error("Settings could not be saved", ex);
        }
    }
}
