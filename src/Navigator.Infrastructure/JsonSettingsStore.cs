using System.Text.Json;
using Navigator.Core.Models;
using Navigator.Core.Services;

namespace Navigator.Infrastructure;

/// <summary>
/// Impostazioni, segnalibri e sessioni in file JSON.
/// Modalità portable: se accanto all'eseguibile esiste "portable.txt" o la cartella "Data",
/// i dati vanno in &lt;cartella del programma&gt;\Data; altrimenti in %AppData%\Navigator.
/// </summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        // NaN per la posizione della finestra
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals,
        // "Left", "QuickView"... invece di numeri: il file resta leggibile e modificabile a mano.
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    public JsonSettingsStore()
        : this(ResolveDataDirectory(out var portable), portable)
    {
    }

    public JsonSettingsStore(string dataDirectory, bool isPortable)
    {
        DataDirectory = dataDirectory;
        IsPortable = isPortable;
        Directory.CreateDirectory(DataDirectory);
    }

    public string DataDirectory { get; }

    public bool IsPortable { get; }

    public AppSettings LoadSettings() => Read<AppSettings>("settings.json") ?? new AppSettings();

    public void SaveSettings(AppSettings settings) => Write("settings.json", settings);

    public IReadOnlyList<BookmarkNode> LoadBookmarks() => Read<List<BookmarkNode>>("bookmarks.json") ?? [];

    public void SaveBookmarks(IEnumerable<BookmarkNode> bookmarks) => Write("bookmarks.json", bookmarks.ToList());

    public IReadOnlyList<Session> LoadSessions() => Read<List<Session>>("sessions.json") ?? [];

    public void SaveSessions(IEnumerable<Session> sessions) => Write("sessions.json", sessions.ToList());

    private static string ResolveDataDirectory(out bool portable)
    {
        var baseDir = AppContext.BaseDirectory;
        var portableData = Path.Combine(baseDir, "Data");
        if (File.Exists(Path.Combine(baseDir, "portable.txt")) || Directory.Exists(portableData))
        {
            portable = true;
            return portableData;
        }

        portable = false;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Navigator");
    }

    private T? Read<T>(string fileName)
        where T : class
    {
        var path = Path.Combine(DataDirectory, fileName);
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(stream, Options);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // File danneggiato: lo mettiamo da parte e ripartiamo dai valori predefiniti.
            TryBackup(path);
            return null;
        }
    }

    private void Write<T>(string fileName, T value)
    {
        var path = Path.Combine(DataDirectory, fileName);
        var temp = path + ".tmp";
        try
        {
            Directory.CreateDirectory(DataDirectory);
            using (var stream = File.Create(temp))
            {
                JsonSerializer.Serialize(stream, value, Options);
            }

            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Salvataggio non riuscito (disco in sola lettura, chiavetta rimossa...): non blocchiamo l'app.
        }
    }

    private static void TryBackup(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Copy(path, path + ".bak", overwrite: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
