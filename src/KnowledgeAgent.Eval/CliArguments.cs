using System.Globalization;

namespace KnowledgeAgent.Eval;

internal sealed class CliArguments
{
    public string Command { get; private set; } = "run";
    public bool Help { get; private set; }
    public string? Dataset { get; private set; }
    public string? DatasetsDir { get; private set; }
    public bool Judge { get; private set; }
    public string? Profile { get; private set; }
    public List<string> Tags { get; } = [];
    public int? MaxItems { get; private set; }
    public int Parallelism { get; private set; } = 3;
    public bool Gate { get; private set; }
    public bool UpdateBaseline { get; private set; }
    public string? ReportDir { get; private set; }
    public string? Name { get; private set; }
    public bool NoLangfuse { get; private set; }
    public Dictionary<string, string?> Settings { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool Quiet { get; private set; }
    public bool Verbose { get; private set; }

    public static CliArguments Parse(IReadOnlyList<string> args)
    {
        var parsed = new CliArguments();
        var index = 0;
        if (args.Count > 0 && !args[0].StartsWith('-'))
        {
            parsed.Command = args[0].ToLowerInvariant() switch
            {
                "run" or "list" or "sync" => args[0].ToLowerInvariant(),
                "help" => "help",
                _ => throw new CliUsageException($"Unknown command '{args[0]}'."),
            };
            parsed.Help = parsed.Command == "help";
            index = 1;
        }

        for (; index < args.Count; index++)
        {
            var argument = args[index];
            string Value() => index + 1 < args.Count ? args[++index] : throw new CliUsageException($"{argument} needs a value.");
            int Number(int minimum) => int.TryParse(Value(), NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number >= minimum
                ? number
                : throw new CliUsageException($"{argument} needs a whole number of at least {minimum}.");

            switch (argument)
            {
                case "-h" or "--help": parsed.Help = true; break;
                case "--dataset": parsed.Dataset = Value(); break;
                case "--datasets-dir": parsed.DatasetsDir = Value(); break;
                case "--judge": parsed.Judge = true; break;
                case "--profile": parsed.Profile = Value(); break;
                case "--tag": parsed.Tags.Add(Value()); break;
                case "--max-items": parsed.MaxItems = Number(1); break;
                case "--parallelism": parsed.Parallelism = Number(1); break;
                case "--gate": parsed.Gate = true; break;
                case "--update-baseline": parsed.UpdateBaseline = true; break;
                case "--report-dir": parsed.ReportDir = Value(); break;
                case "--name": parsed.Name = Value(); break;
                case "--no-langfuse": parsed.NoLangfuse = true; break;
                case "--quiet": parsed.Quiet = true; break;
                case "--verbose": parsed.Verbose = true; break;
                case "--set":
                    var setting = Value();
                    var separator = setting.IndexOf('=');
                    if (separator <= 0)
                    {
                        throw new CliUsageException("--set expects key=value.");
                    }

                    parsed.Settings[setting[..separator].Trim()] = setting[(separator + 1)..];
                    break;
                default:
                    throw new CliUsageException($"Unknown option '{argument}'.");
            }
        }

        return parsed;
    }
}
