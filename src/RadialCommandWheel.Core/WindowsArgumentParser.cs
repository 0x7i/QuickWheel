using System.Text;

namespace RadialCommandWheel.Core;

public static class WindowsArgumentParser
{
    public static IReadOnlyList<string> Split(string? commandLine)
    {
        var arguments = new List<string>();
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return arguments;
        }

        var current = new StringBuilder();
        var inQuotes = false;
        var hasToken = false;

        for (var index = 0; index < commandLine.Length; index++)
        {
            var character = commandLine[index];
            if (character == '\\')
            {
                var slashCount = 1;
                while (index + 1 < commandLine.Length && commandLine[index + 1] == '\\')
                {
                    slashCount++;
                    index++;
                }

                if (index + 1 < commandLine.Length && commandLine[index + 1] == '"')
                {
                    current.Append('\\', slashCount / 2);
                    if (slashCount % 2 == 1)
                    {
                        current.Append('"');
                        index++;
                        hasToken = true;
                    }
                }
                else
                {
                    current.Append('\\', slashCount);
                    hasToken = true;
                }

                continue;
            }

            if (character == '"')
            {
                if (inQuotes && index + 1 < commandLine.Length && commandLine[index + 1] == '"')
                {
                    current.Append('"');
                    index++;
                    hasToken = true;
                }
                else
                {
                    inQuotes = !inQuotes;
                    hasToken = true;
                }

                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                if (hasToken)
                {
                    arguments.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }

                continue;
            }

            current.Append(character);
            hasToken = true;
        }

        if (inQuotes)
        {
            throw new FormatException("The argument string contains an unmatched quote.");
        }

        if (hasToken)
        {
            arguments.Add(current.ToString());
        }

        return arguments;
    }

    public static string Quote(string argument)
    {
        if (argument.Length == 0)
        {
            return "\"\"";
        }

        if (!argument.Any(character => char.IsWhiteSpace(character) || character == '"'))
        {
            return argument;
        }

        var result = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var character in argument)
        {
            if (character == '\\')
            {
                backslashes++;
            }
            else if (character == '"')
            {
                result.Append('\\', backslashes * 2 + 1);
                result.Append('"');
                backslashes = 0;
            }
            else
            {
                result.Append('\\', backslashes);
                result.Append(character);
                backslashes = 0;
            }
        }

        result.Append('\\', backslashes * 2);
        result.Append('"');
        return result.ToString();
    }

    public static string Join(IEnumerable<string> arguments) =>
        string.Join(' ', arguments.Select(Quote));
}

