using System.Text.Json;
using System.Text.Json.Serialization;
using Still.Core.Models;

namespace Still.Core.Persistence;

public interface IStateStore
{
    string DataDirectory { get; }
    string StateFilePath { get; }
    AppState Load(out string? warning);
    void Save(AppState state);
    void WriteHeartbeat(DateTimeOffset now);
    void ClearHeartbeat();
}

public sealed class JsonStateStore : IStateStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public JsonStateStore(string? dataDirectory = null)
    {
        DataDirectory = string.IsNullOrWhiteSpace(dataDirectory) ? ResolveDirectory() : dataDirectory!;
        StateFilePath = Path.Combine(DataDirectory, "state.json");
    }

    public string DataDirectory { get; }
    public string StateFilePath { get; }
    public string BackupFilePath => Path.Combine(DataDirectory, "state.bak.json");
    public string HeartbeatFilePath => Path.Combine(DataDirectory, "heartbeat");

    public AppState Load(out string? warning)
    {
        warning = null;
        Directory.CreateDirectory(DataDirectory);

        var current = TryRead(StateFilePath, out var currentError);
        if (current is not null)
        {
            current.Normalize();
            return current;
        }

        var backup = TryRead(BackupFilePath, out _);
        if (backup is not null)
        {
            backup.Normalize();
            warning = "Die letzte Sicherung wurde geladen, weil die Hauptdatei nicht lesbar war.";
            return backup;
        }

        if (currentError is not null)
        {
            warning = "Die gespeicherten Daten konnten nicht gelesen werden. Es wurde ein leerer Stand gestartet.";
        }

        var fresh = AppState.CreateDefault();
        fresh.Normalize();
        return fresh;
    }

    public void Save(AppState state)
    {
        Directory.CreateDirectory(DataDirectory);
        state.UpdatedAt = DateTimeOffset.Now;
        state.Normalize();

        var json = JsonSerializer.Serialize(state, Options);
        var temp = StateFilePath + ".tmp";
        File.WriteAllText(temp, json);

        if (File.Exists(StateFilePath))
        {
            File.Copy(StateFilePath, BackupFilePath, overwrite: true);
        }

        File.Move(temp, StateFilePath, overwrite: true);
    }

    public void WriteHeartbeat(DateTimeOffset now)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            File.WriteAllText(HeartbeatFilePath, now.ToString("O"));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public void ClearHeartbeat()
    {
        try
        {
            if (File.Exists(HeartbeatFilePath))
            {
                File.Delete(HeartbeatFilePath);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public static string ResolveDirectory()
    {
        var overridden = Environment.GetEnvironmentVariable("STILL_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return overridden;
        }

        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "Still");
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrWhiteSpace(xdg))
        {
            return Path.Combine(xdg, "still");
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", "still");
    }

    private static AppState? TryRead(string path, out string? error)
    {
        error = null;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "Leere Datei.";
                return null;
            }

            return JsonSerializer.Deserialize<AppState>(json, Options);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            error = ex.Message;
            return null;
        }
    }
}
