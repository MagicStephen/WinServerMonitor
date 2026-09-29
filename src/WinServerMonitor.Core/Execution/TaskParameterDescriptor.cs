namespace WinServerMonitor.Core.Execution;

public enum TaskParameterKind
{
    Text,
    MultilineText,
    Number,
    Boolean,
    Choice,
}

public sealed record TaskParameterDescriptor(
    string Name,
    string Label,
    TaskParameterKind Kind = TaskParameterKind.Text,
    bool Required = false,
    string? DefaultValue = null,
    string? Help = null,
    IReadOnlyList<string>? Choices = null);
