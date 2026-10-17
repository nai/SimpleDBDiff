namespace SimpleDBDiff.Tests;

public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SIMPLEDBDIFF_TEST_LEFT")) ||
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SIMPLEDBDIFF_TEST_RIGHT")))
            Skip = "Configure SIMPLEDBDIFF_TEST_LEFT and SIMPLEDBDIFF_TEST_RIGHT for real PostgreSQL integration checks.";
    }
}
