namespace Apex.Services;
public enum TaskOutcomeKind
{
    Success, Partial, Canceled, Error
}
public sealed record OutputReference(string Path, string Label);
public sealed record TaskOutcome(TaskOutcomeKind Kind, string Message, IReadOnlyList<OutputReference>? Outputs = null);
