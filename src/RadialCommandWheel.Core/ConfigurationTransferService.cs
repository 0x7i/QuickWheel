using System.Text.Json;
using System.Text.Json.Serialization;

namespace RadialCommandWheel.Core;

public sealed class ConfigurationExportEnvelope
{
    public string Format { get; set; } = ConfigurationTransferService.FormatIdentifier;
    public int ExportVersion { get; set; } = ConfigurationTransferService.CurrentExportVersion;
    public string ExportedAtUtc { get; set; } = DateTimeOffset.UtcNow.ToString("O");
    public string ApplicationVersion { get; set; } = "1.0.0";
    public AppConfiguration Configuration { get; set; } = new();
}

public sealed record ConfigurationTransferInspection(
    bool IsValid,
    AppConfiguration? Configuration,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    int MissingTargetCount,
    int ItemCount,
    int GroupCount,
    int ThemeCount)
{
    public IReadOnlyList<string> MissingTargets { get; init; } = [];

    public static ConfigurationTransferInspection Invalid(params string[] errors) =>
        new(false, null, errors, [], 0, 0, 0, 0);
}

public static class ConfigurationTransferService
{
    public const string FormatIdentifier = "RadialCommandWheel.Configuration";
    public const int CurrentExportVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string Export(AppConfiguration configuration, string applicationVersion)
    {
        var envelope = new ConfigurationExportEnvelope
        {
            ExportedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
            ApplicationVersion = applicationVersion,
            Configuration = configuration.Clone()
        };
        envelope.Configuration.SchemaVersion = AppConfiguration.CurrentSchemaVersion;
        return JsonSerializer.Serialize(envelope, JsonOptions);
    }

    public static ConfigurationTransferInspection Inspect(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ConfigurationTransferInspection.Invalid("The import file is empty.");
        }

        ConfigurationExportEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<ConfigurationExportEnvelope>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            return ConfigurationTransferInspection.Invalid($"Malformed JSON: {exception.Message}");
        }

        if (envelope is null)
        {
            return ConfigurationTransferInspection.Invalid("The import file contains no configuration.");
        }

        if (!string.Equals(envelope.Format, FormatIdentifier, StringComparison.Ordinal))
        {
            return ConfigurationTransferInspection.Invalid("This is not a Radial Command Wheel export file.");
        }

        if (envelope.ExportVersion <= 0 || envelope.ExportVersion > CurrentExportVersion)
        {
            return ConfigurationTransferInspection.Invalid(
                $"Export format version {envelope.ExportVersion} is not supported.");
        }

        if (envelope.Configuration is null)
        {
            return ConfigurationTransferInspection.Invalid("The configuration payload is missing.");
        }

        if (envelope.Configuration.SchemaVersion > AppConfiguration.CurrentSchemaVersion)
        {
            return ConfigurationTransferInspection.Invalid(
                $"Configuration schema version {envelope.Configuration.SchemaVersion} is newer than this application supports.");
        }

        var rawValidation = ConfigurationValidator.Validate(envelope.Configuration);
        if (!rawValidation.IsValid)
        {
            return ConfigurationTransferInspection.Invalid(rawValidation.Errors.ToArray());
        }

        ConfigurationValidator.Normalize(envelope.Configuration);
        var normalizedValidation = ConfigurationValidator.Validate(envelope.Configuration);
        if (!normalizedValidation.IsValid)
        {
            return ConfigurationTransferInspection.Invalid(normalizedValidation.Errors.ToArray());
        }

        var missing = ConfigurationValidator.EnumerateAll(envelope.Configuration.MainWheel)
            .Where(item => item.Type != WheelItemType.Group)
            .Where(item => TargetValidator.Validate(item).IsError)
            .Select(item => $"{item.DisplayCaption}: {item.Target}")
            .ToArray();
        var warnings = normalizedValidation.Warnings.Concat(
            missing.Select(path => $"Missing target: {path}")).Distinct().ToArray();
        var itemCount = ConfigurationValidator.EnumerateAll(envelope.Configuration.MainWheel)
            .Count(item => item.Type != WheelItemType.Group);
        return new ConfigurationTransferInspection(
            true,
            envelope.Configuration,
            [],
            warnings,
            missing.Length,
            itemCount,
            envelope.Configuration.Groups.Count(),
            envelope.Configuration.Themes.Count)
        {
            MissingTargets = missing
        };
    }
}
