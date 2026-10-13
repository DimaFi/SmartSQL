namespace SmartSQL.Application;

public static class DatabaseProviders
{
    public const string PostgreSql = "postgresql";
}

public enum ConnectionSslPolicy
{
    Disable,
    Prefer,
    Require,
    VerifyCA,
    VerifyFull
}

/// <summary>Nonsecret connection settings only. Passwords are supplied separately at connection time.</summary>
public sealed record ConnectionProfile(Guid Id, string Name, string Provider, string Host,
    int Port, string Database, string Username, ConnectionSslPolicy SslPolicy)
{
    public void Validate()
    {
        if (Id == Guid.Empty)
            throw new ArgumentException("Профиль подключения должен иметь идентификатор.", nameof(Id));
        ValidateText(Name, 128, nameof(Name));
        ValidateText(Provider, 64, nameof(Provider));
        if (Provider.Any(character => !(character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')))
            throw new ArgumentException("Укажите код провайдера строчными латинскими буквами.", nameof(Provider));
        ValidateText(Host, 253, nameof(Host));
        if (Uri.CheckHostName(Host) == UriHostNameType.Unknown || Host.IndexOfAny(['/', '\\', '@', ';', '=']) >= 0)
            throw new ArgumentException("Укажите имя узла или IP-адрес, без URI и строки подключения.", nameof(Host));
        if (Port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(Port), "Порт должен быть от 1 до 65535.");
        ValidateText(Database, 256, nameof(Database));
        ValidateText(Username, 256, nameof(Username));
        if (!Enum.IsDefined(SslPolicy))
            throw new ArgumentException("Неизвестная политика SSL.", nameof(SslPolicy));
    }

    private static void ValidateText(string? value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > maxLength || value.Any(char.IsControl) || value.Contains("://", StringComparison.Ordinal))
            throw new ArgumentException($"Поле {field} должно содержать от 1 до {maxLength} символов без управляющих символов и URI.", field);
        for (var index = 0; index < value.Length; index++)
        {
            if (char.IsHighSurrogate(value[index]) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
                index++;
            else if (char.IsSurrogate(value[index]))
                throw new ArgumentException($"Поле {field} содержит некорректный Unicode.", field);
        }
    }
}
