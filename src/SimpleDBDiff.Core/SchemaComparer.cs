using System.Text;

namespace SimpleDBDiff.Core;

public static class SchemaComparer
{
    public static IReadOnlyList<SchemaDifference> Compare(SchemaSnapshot left, SchemaSnapshot right)
    {
        var result = new List<SchemaDifference>();
        var leftTables = left.Tables.ToDictionary(t => t.Key, StringComparer.Ordinal);
        var rightTables = right.Tables.ToDictionary(t => t.Key, StringComparer.Ordinal);
        foreach (var key in leftTables.Keys.Union(rightTables.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var hasL = leftTables.TryGetValue(key, out var l);
            var hasR = rightTables.TryGetValue(key, out var r);
            if (!hasL || !hasR)
            {
                result.Add(new("Table", key, hasL ? DifferenceKind.LeftOnly : DifferenceKind.RightOnly, hasL ? key : null, hasR ? key : null));
                continue;
            }
            var detailStart = result.Count;
            CompareObjects(l!.Columns, r!.Columns, c => c.Name, c => Describe(c), $"Column {key}", result);
            CompareObjects(l.Constraints, r.Constraints, c => c.Name, c => $"{c.Kind}: {Normalize(c.Definition)}", $"Constraint {key}", result);
            CompareObjects(l.Indexes, r.Indexes, i => i.Name, i => Normalize(i.Definition), $"Index {key}", result);
            var kind = result.Skip(detailStart).Any(d => d.Kind != DifferenceKind.Equal) ? DifferenceKind.Different : DifferenceKind.Equal;
            result.Insert(detailStart, new("Table", key, kind, key, key));
        }
        return result;
    }

    public static string Normalize(string? value)
    {
        if (value is null) return "";
        var text = value.Trim();
        var result = new StringBuilder(text.Length);
        char quoted = '\0';
        string? dollarTag = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (dollarTag is not null)
            {
                if (text.AsSpan(i).StartsWith(dollarTag))
                {
                    result.Append(dollarTag); i += dollarTag.Length - 1; dollarTag = null;
                }
                else result.Append(c);
                continue;
            }
            if (quoted != '\0')
            {
                result.Append(c);
                if (c == quoted)
                {
                    if (i + 1 < text.Length && text[i + 1] == quoted) result.Append(text[++i]);
                    else quoted = '\0';
                }
                continue;
            }
            if (c is '\'' or '"') { quoted = c; result.Append(c); continue; }
            if (c == '$')
            {
                var end = text.IndexOf('$', i + 1);
                if (end >= 0 && text.AsSpan(i + 1, end - i - 1).ToString().All(ch => char.IsLetterOrDigit(ch) || ch == '_'))
                {
                    dollarTag = text[i..(end + 1)]; result.Append(dollarTag); i = end; continue;
                }
            }
            if (char.IsWhiteSpace(c))
            {
                if (result.Length > 0 && result[result.Length - 1] != ' ') result.Append(' ');
            }
            else result.Append(c);
        }
        return result.ToString().Trim();
    }

    public static string Describe(ColumnSchema column) =>
        $"{Normalize(column.DataType)} {(column.IsNullable ? "NULL" : "NOT NULL")} DEFAULT {Normalize(column.DefaultExpression)} IDENTITY {column.IdentityGeneration ?? (column.IsIdentity ? "BY DEFAULT" : "NONE")} GENERATED {Normalize(column.GeneratedExpression)}";

    private static void CompareObjects<T>(IEnumerable<T> left, IEnumerable<T> right, Func<T, string> key, Func<T, string> describe, string type, List<SchemaDifference> result)
    {
        var l = left.ToDictionary(key, StringComparer.Ordinal);
        var r = right.ToDictionary(key, StringComparer.Ordinal);
        foreach (var name in l.Keys.Union(r.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var hasL = l.TryGetValue(name, out var leftObject);
            var hasR = r.TryGetValue(name, out var rightObject);
            var ld = hasL ? describe(leftObject!) : null;
            var rd = hasR ? describe(rightObject!) : null;
            var status = !hasL ? DifferenceKind.RightOnly : !hasR ? DifferenceKind.LeftOnly : ld == rd ? DifferenceKind.Equal : DifferenceKind.Different;
            result.Add(new(type, name, status, ld, rd));
        }
    }
}
