namespace RadialCommandWheel.Core;

public enum TargetValidationState
{
    Valid,
    Warning,
    Error,
    NotApplicable
}

public sealed record TargetValidation(TargetValidationState State, string Message, string MessageKey = "")
{
    public bool IsError => State == TargetValidationState.Error;
}

public static class TargetValidator
{
    public static TargetValidation Validate(WheelItem item)
    {
        if (item.Type == WheelItemType.Group)
        {
            return new TargetValidation(TargetValidationState.NotApplicable,
                "Internal groups do not use filesystem paths.", "TargetGroup");
        }

        if (string.IsNullOrWhiteSpace(item.Target))
        {
            return new TargetValidation(TargetValidationState.Error,
                "Choose a target path or command before launching this item.", "TargetMissing");
        }

        if (item.Type == WheelItemType.Folder)
        {
            return Directory.Exists(item.Target)
                ? new TargetValidation(TargetValidationState.Valid, "Folder found.", "TargetValidFolder")
                : new TargetValidation(TargetValidationState.Error,
                    $"Folder not found: {item.Target}");
        }

        if (item.Type == WheelItemType.File)
        {
            return File.Exists(item.Target)
                ? new TargetValidation(TargetValidationState.Valid, "File found.", "TargetValidFile")
                : new TargetValidation(TargetValidationState.Error,
                    $"File not found: {item.Target}");
        }

        if (Path.IsPathFullyQualified(item.Target))
        {
            return File.Exists(item.Target)
                ? new TargetValidation(TargetValidationState.Valid, "Executable found.", "TargetValidExecutable")
                : new TargetValidation(TargetValidationState.Error,
                    $"Executable not found: {item.Target}");
        }

        if (item.Target.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]) >= 0)
        {
            return new TargetValidation(TargetValidationState.Error,
                "Relative paths with folders are ambiguous. Choose a full path.", "TargetRelative");
        }

        return new TargetValidation(TargetValidationState.Warning,
            "This is a command name. Windows will search its normal executable locations when launched.",
            "TargetCommandName");
    }
}

