using Npgsql;
using SimpleDBDiff.Core;

namespace SimpleDBDiff.Postgres;

public sealed class PostgresSchemaReader : ISchemaReader
{
    public async Task<SchemaSnapshot> LoadAsync(string connectionString, CancellationToken cancellationToken = default)
    {
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using (var path = new NpgsqlCommand("SET search_path TO ''", connection))
            await path.ExecuteNonQueryAsync(cancellationToken);

        await using (var unsupported = new NpgsqlCommand("""
            SELECT n.nspname, c.relname FROM pg_catalog.pg_class c
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE (c.relkind IN ('p', 'f') OR c.relispartition)
              AND n.nspname NOT IN ('pg_catalog', 'information_schema')
              AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp_%'
            LIMIT 1
            """, connection))
        await using (var unsupportedReader = await unsupported.ExecuteReaderAsync(cancellationToken))
            if (await unsupportedReader.ReadAsync(cancellationToken))
                throw new NotSupportedException($"Partitioned and foreign tables are not supported in Stage 1 ({unsupportedReader.GetString(0)}.{unsupportedReader.GetString(1)}).");

        var tables = new Dictionary<(string Schema, string Name), TableBuilder>();
        await using (var command = new NpgsqlCommand("""
            SELECT n.nspname, c.relname
            FROM pg_catalog.pg_class c JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind = 'r' AND n.nspname NOT IN ('pg_catalog', 'information_schema')
              AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp_%'
            ORDER BY n.nspname, c.relname
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                var schema = reader.GetString(0); var name = reader.GetString(1);
                tables[(schema, name)] = new(schema, name);
            }

        await using (var command = new NpgsqlCommand("""
            SELECT n.nspname, c.relname, a.attname, pg_catalog.format_type(a.atttypid, a.atttypmod),
                   NOT a.attnotnull, pg_catalog.pg_get_expr(d.adbin, d.adrelid), a.attidentity::text, a.attgenerated::text
            FROM pg_catalog.pg_attribute a
            JOIN pg_catalog.pg_class c ON c.oid = a.attrelid
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            LEFT JOIN pg_catalog.pg_attrdef d ON d.adrelid = a.attrelid AND d.adnum = a.attnum
            WHERE c.relkind = 'r' AND a.attnum > 0 AND NOT a.attisdropped
              AND n.nspname NOT IN ('pg_catalog', 'information_schema')
              AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp_%'
            ORDER BY n.nspname, c.relname, a.attnum
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                var table = tables[(reader.GetString(0), reader.GetString(1))];
                var expression = reader.IsDBNull(5) ? null : reader.GetString(5);
                var identityCode = reader.GetString(6);
                var identity = identityCode.Length > 0;
                var generated = reader.GetString(7).Length > 0;
                table.Columns.Add(new(reader.GetString(2), reader.GetString(3), reader.GetBoolean(4), generated ? null : expression, identity, generated ? expression : null, identityCode == "a" ? "ALWAYS" : identityCode == "d" ? "BY DEFAULT" : null));
            }

        await using (var command = new NpgsqlCommand("""
            SELECT n.nspname, c.relname, co.conname, co.contype::text, pg_catalog.pg_get_constraintdef(co.oid, false)
            FROM pg_catalog.pg_constraint co
            JOIN pg_catalog.pg_class c ON c.oid = co.conrelid
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            WHERE c.relkind = 'r' AND n.nspname NOT IN ('pg_catalog', 'information_schema')
              AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp_%'
            ORDER BY n.nspname, c.relname, co.conname
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                tables[(reader.GetString(0), reader.GetString(1))].Constraints.Add(new(reader.GetString(2), reader.GetString(3)[0], reader.GetString(4)));

        await using (var command = new NpgsqlCommand("""
            SELECT n.nspname, c.relname, i.relname, pg_catalog.pg_get_indexdef(i.oid)
            FROM pg_catalog.pg_index x
            JOIN pg_catalog.pg_class c ON c.oid = x.indrelid
            JOIN pg_catalog.pg_namespace n ON n.oid = c.relnamespace
            JOIN pg_catalog.pg_class i ON i.oid = x.indexrelid
            LEFT JOIN pg_catalog.pg_constraint co ON co.conindid = i.oid
            WHERE c.relkind = 'r' AND co.oid IS NULL
              AND n.nspname NOT IN ('pg_catalog', 'information_schema')
              AND n.nspname NOT LIKE 'pg_toast%' AND n.nspname NOT LIKE 'pg_temp_%'
            ORDER BY n.nspname, c.relname, i.relname
            """, connection))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                tables[(reader.GetString(0), reader.GetString(1))].Indexes.Add(new(reader.GetString(2), reader.GetString(3)));

        return new(tables.Values.Select(t => new TableSchema(t.Schema, t.Name, t.Columns, t.Constraints, t.Indexes)).ToArray());
    }

    private sealed record TableBuilder(string Schema, string Name)
    {
        public List<ColumnSchema> Columns { get; } = [];
        public List<ConstraintSchema> Constraints { get; } = [];
        public List<IndexSchema> Indexes { get; } = [];
    }
}
