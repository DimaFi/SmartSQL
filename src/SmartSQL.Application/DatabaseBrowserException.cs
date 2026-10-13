namespace SmartSQL.Application;

/// <summary>A provider-sanitized browsing failure; never include credentials or raw driver details.</summary>
public sealed class DatabaseBrowserException : Exception
{
    public DatabaseBrowserException(string code, string message) : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
    }

    public string Code { get; }
}
