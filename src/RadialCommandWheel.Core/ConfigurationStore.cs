using System.Text.Json;
using System.Text.Json.Serialization;

namespace RadialCommandWheel.Core;

public sealed record LoadResult(AppConfiguration Configuration, string Message, bool RecoveredFromBackup);

public sealed class ConfigurationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ConfigurationStore(string? path = null)
    {
        Path = path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RadialCommandWheel",
            "config.json");
    }

    public string Path { get; }

    public string BackupPath => Path + ".bak";

    public AppLanguage ReadLanguageHint()
    {
        foreach (var path in new[] { Path, BackupPath })
        {
            try
            {
                if (File.Exists(path))
                {
                    var language = Deserialize(File.ReadAllText(path)).Settings.Language;
                    if (Enum.IsDefined(language))
                    {
                        return language;
                    }
                }
            }
            catch
            {
                // A malformed file must not prevent another instance from reporting its status.
            }
        }

        return AppLanguage.English;
    }

    public string CreateRecoveryBackup(string reason)
    {
        if (!File.Exists(Path))
        {
            throw new FileNotFoundException("There is no current configuration to back up.", Path);
        }

        var safeReason = string.Concat(reason.Select(character =>
            char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-'));
        var destination = Path + $".{safeReason}-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}";
        File.Copy(Path, destination, false);
        return destination;
    }

    public LoadResult Load()
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        if (!File.Exists(Path) && !File.Exists(BackupPath))
        {
            var defaults = ConfigurationDefaults.Create();
            Save(defaults);
            return new LoadResult(defaults, "Created a default configuration.", false);
        }

        var primary = TryRead(Path, out var primaryError);
        if (primary is not null)
        {
            return new LoadResult(primary, "Configuration loaded.", false);
        }

        var backup = TryRead(BackupPath, out var backupError);
        if (backup is not null)
        {
            PreserveInvalidFile(Path);
            return new LoadResult(backup, $"Recovered the last valid backup. Primary file error: {primaryError}", true);
        }

        var fallback = ConfigurationDefaults.Create();
        var failedPath = Path + $".invalid-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        PreserveInvalidFile(Path, failedPath);

        Save(fallback);
        return new LoadResult(fallback,
            $"Configuration could not be read ({primaryError}; {backupError}). Defaults were restored; the invalid file was preserved when possible.",
            true);
    }

    public void Save(AppConfiguration configuration)
    {
        configuration.SchemaVersion = AppConfiguration.CurrentSchemaVersion;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temporaryPath = Path + ".tmp";

        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                   64 * 1024, FileOptions.WriteThrough))
        {
            JsonSerializer.Serialize(stream, configuration, JsonOptions);
            stream.Flush(true);
        }

        if (File.Exists(Path))
        {
            try
            {
                File.Replace(temporaryPath, Path, BackupPath, true);
            }
            catch (PlatformNotSupportedException)
            {
                File.Copy(Path, BackupPath, true);
                File.Move(temporaryPath, Path, true);
            }
            catch (IOException)
            {
                File.Copy(Path, BackupPath, true);
                File.Move(temporaryPath, Path, true);
            }
        }
        else
        {
            File.Move(temporaryPath, Path, true);
        }
    }

    public static string Serialize(AppConfiguration configuration) =>
        JsonSerializer.Serialize(configuration, JsonOptions);

    public static AppConfiguration Deserialize(string json) =>
        JsonSerializer.Deserialize<AppConfiguration>(json, JsonOptions)
        ?? throw new JsonException("The configuration was empty.");

    private static AppConfiguration? TryRead(string path, out string error)
    {
        error = "";
        if (!File.Exists(path))
        {
            error = "File does not exist.";
            return null;
        }

        try
        {
            var json = File.ReadAllText(path);
            var configuration = Deserialize(json);
            if (configuration.SchemaVersion > AppConfiguration.CurrentSchemaVersion)
            {
                error = $"Configuration version {configuration.SchemaVersion} is newer than this application supports.";
                return null;
            }
            ConfigurationValidator.Normalize(configuration);
            var validation = ConfigurationValidator.Validate(configuration);
            if (!validation.IsValid)
            {
                error = string.Join(" ", validation.Errors);
                return null;
            }

            return configuration;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            error = exception.Message;
            return null;
        }
    }

    private static void PreserveInvalidFile(string path, string? destination = null)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Copy(path, destination ?? path + $".invalid-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}", false);
            }
        }
        catch
        {
            // Recovery should continue even if the diagnostic copy cannot be created.
        }
    }
}

