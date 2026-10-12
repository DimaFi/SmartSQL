using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmartSql.LocalAssistant;

public enum FieldType
{
    Text,
    Number,
    Date,
    Boolean
}

public enum FilterOperator
{
    Equal,
    NotEqual,
    Contains,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual
}

public enum SortDirection
{
    Ascending,
    Descending
}

public sealed record FieldSchema(string Name, FieldType Type);

public sealed record TableSchema(string Name, IReadOnlyList<FieldSchema> Fields);

public sealed record UserPermissions(
    bool CanView,
    IReadOnlySet<string> ReadableFields,
    int MaximumRows = 100);

public sealed record AssistantRequest(
    TableSchema Schema,
    UserPermissions Permissions,
    string RussianRequest);

public sealed record FilterClause(
    string Field,
    FilterOperator Operator,
    JsonElement Value);

public sealed record SortClause(string Field, SortDirection Direction);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "result")]
[JsonDerivedType(typeof(AcceptedDecision), "accepted")]
[JsonDerivedType(typeof(ExplainedDecision), "explained")]
[JsonDerivedType(typeof(RefusedDecision), "refused")]
public abstract record AssistantDecision;

public sealed record AcceptedDecision(ViewIntent Intent) : AssistantDecision;

public sealed record ExplainedDecision(ExplanationIntent Intent) : AssistantDecision;

public sealed record RefusedDecision(string Explanation) : AssistantDecision;

public sealed record ViewIntent(
    string Table,
    IReadOnlyList<string> Fields,
    IReadOnlyList<FilterClause> Filters,
    SortClause? Sort,
    int Limit);

public sealed record ExplanationIntent(string Topic, string Explanation);

public interface ILanguageModel
{
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken cancellationToken);
}
