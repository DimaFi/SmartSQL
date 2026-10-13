namespace SmartSQL.Application.Tests;

public sealed class ConnectionProfileTests
{
    [Xunit.Theory]
    [Xunit.InlineData("localhost")]
    [Xunit.InlineData("db.example.org")]
    [Xunit.InlineData("127.0.0.1")]
    [Xunit.InlineData("::1")]
    public void StructuredTcpProfilesValidateWithoutStoringASecret(string host)
    {
        var profile = Profile() with { Host = host };
        profile.Validate();
        Xunit.Assert.DoesNotContain(typeof(ConnectionProfile).GetProperties(), property =>
            property.Name.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("ConnectionString", StringComparison.OrdinalIgnoreCase) ||
            property.Name.Contains("Uri", StringComparison.OrdinalIgnoreCase));
    }

    [Xunit.Fact]
    public void InvalidSettingsCannotReachThePersistenceOrProviderBoundary()
    {
        var profile = Profile();
        ConnectionProfile[] invalid =
        [
            profile with { Id = Guid.Empty }, profile with { Name = " " },
            profile with { Name = new string('n', 129) }, profile with { Provider = "PostgreSQL" },
            profile with { Provider = "postgresql;password=secret" }, profile with { Host = "postgresql://user:secret@localhost/db" },
            profile with { Host = "localhost;Password=secret" }, profile with { Host = "localhost/db" },
            profile with { Port = 0 }, profile with { Port = 65536 }, profile with { Database = "" },
            profile with { Database = "postgresql://user:secret@localhost/db" }, profile with { Username = "name\nPassword=secret" },
            profile with { Username = new string('u', 257) }, profile with { Name = "\uD800" },
            profile with { SslPolicy = (ConnectionSslPolicy)999 }
        ];
        Xunit.Assert.All(invalid, item => Xunit.Assert.ThrowsAny<ArgumentException>(item.Validate));
    }

    [Xunit.Fact]
    public void ProviderKeysRemainExtensibleAndNativeNamesAreNotSilentlyTrimmed()
    {
        var profile = Profile() with { Provider = "another-provider", Database = " data;archive ", Username = " user=reader ", Name = "Учёт 🌍" };
        profile.Validate();
        Xunit.Assert.Equal(" data;archive ", profile.Database);
        Xunit.Assert.Equal(" user=reader ", profile.Username);
    }

    private static ConnectionProfile Profile() => new(Guid.NewGuid(), "Локальная база", DatabaseProviders.PostgreSql,
        "localhost", 5432, "smartsql", "reader", ConnectionSslPolicy.VerifyFull);
}
