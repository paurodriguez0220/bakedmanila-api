namespace BakedManila.Core.Tests.Data;

public static class TestDb
{
    public static string NewConnectionString() =>
        $@"Server=(localdb)\MSSQLLocalDB;Database=BakedManila.Tests.{Guid.NewGuid():N};Trusted_Connection=True;MultipleActiveResultSets=true";
}
