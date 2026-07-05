namespace BakedManila.Core.Tests.Data;

public static class TestDb
{
    private const string ConnectionTemplateEnvVar = "TEST_SQL_CONNECTION_TEMPLATE";

    /// <summary>
    /// Builds a fresh, uniquely-named database connection string for a single test run.
    /// Locally this targets LocalDB. In CI (no LocalDB on ubuntu-latest), set
    /// TEST_SQL_CONNECTION_TEMPLATE with a "{db}" placeholder to point at the
    /// SQL Server service container instead — e.g.
    /// "Server=localhost,1433;Database={db};User Id=sa;Password=...;TrustServerCertificate=True".
    /// </summary>
    public static string NewConnectionString()
    {
        var dbName = $"BakedManila.Tests.{Guid.NewGuid():N}";
        var template = Environment.GetEnvironmentVariable(ConnectionTemplateEnvVar);

        return string.IsNullOrEmpty(template)
            ? $@"Server=(localdb)\MSSQLLocalDB;Database={dbName};Trusted_Connection=True;MultipleActiveResultSets=true"
            : template.Replace("{db}", dbName);
    }
}
