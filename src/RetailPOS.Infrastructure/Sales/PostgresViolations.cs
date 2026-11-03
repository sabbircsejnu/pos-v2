using Npgsql;

namespace RetailPOS.Infrastructure.Sales;

internal static class PostgresViolations
{
    /// <summary>
    /// True when the exception chain contains a unique-index violation whose constraint/index name (PostgreSQL) or
    /// column list (SQLite, used by relational unit tests) mentions <paramref name="marker"/>.
    /// </summary>
    public static bool IsUniqueViolation(Exception exception, string marker)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
                return postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
                       (postgres.ConstraintName?.Contains(marker, StringComparison.OrdinalIgnoreCase) ?? false);

            if (current.GetType().Name == "SqliteException" &&
                current.Message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase) &&
                current.Message.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
