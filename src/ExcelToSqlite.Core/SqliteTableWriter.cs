using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;

namespace ExcelToSqlite.Core;

public static class SqliteTableWriter
{
    public const string ManifestTable = "_import_manifest";

    public static TableResult Write(
        SqliteConnection conn,
        TableData table,
        SourceSpec spec,
        ImportOptions opt,
        IProgress<ImportProgress>? progress,
        int sourceIndex,
        int sourceCount,
        CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        var keyCount = Math.Clamp(spec.KeyColumnCount, 0, table.Columns.Count);
        if (spec.KeyColumnCount > table.Columns.Count)
            throw new InvalidOperationException(
                $"[{table.TableName}] PK 지정 개수({spec.KeyColumnCount})가 컬럼 수({table.Columns.Count})보다 많습니다.");

        var keyColumns = table.Columns.Take(keyCount).ToList();

        using (var tx = conn.BeginTransaction())
        {
            if (opt.DropExistingTable)
            {
                Exec(conn, tx, $"DROP TABLE IF EXISTS {Naming.Quote(table.TableName)};");
            }
            else
            {
                // 추가(append) 모드: 기존 테이블이 있으면 컬럼 구성을 맞춰야 한다.
                var existing = GetExistingColumns(conn, tx, table.TableName);
                if (existing is not null && !ColumnsMatch(existing, table.Columns))
                {
                    if (!opt.AllowAddColumns)
                        throw new InvalidOperationException(
                            $"[{table.TableName}] 기존 테이블과 컬럼 구성이 다릅니다.\n" +
                            $"  기존: {string.Join(", ", existing)}\n" +
                            $"  신규: {string.Join(", ", table.Columns)}\n" +
                            "  '컬럼 자동 추가'를 켜거나, '데이터 교체' 모드를 쓰거나, 테이블명을 변경하세요.");

                    AddMissingColumns(conn, tx, table, existing, keyColumns);
                }
            }

            Exec(conn, tx, BuildCreateSql(table, keyCount, opt));

            var verb = opt.DuplicateKey switch
            {
                DuplicateKeyPolicy.Replace => "INSERT OR REPLACE",
                DuplicateKeyPolicy.Ignore => "INSERT OR IGNORE",
                _ => "INSERT"
            };

            var cols = string.Join(", ", table.Columns.Select(Naming.Quote));
            var prms = string.Join(", ", Enumerable.Range(0, table.Columns.Count).Select(i => "$p" + i));

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"{verb} INTO {Naming.Quote(table.TableName)} ({cols}) VALUES ({prms});";

            var parameters = new SqliteParameter[table.Columns.Count];
            for (var i = 0; i < parameters.Length; i++)
                parameters[i] = cmd.Parameters.Add("$p" + i, SqliteType.Text);

            cmd.Prepare();

            long done = 0;
            var total = table.Rows.Count;
            var reportEvery = Math.Max(1000, total / 100);

            foreach (var row in table.Rows)
            {
                ct.ThrowIfCancellationRequested();

                for (var i = 0; i < parameters.Length; i++)
                {
                    var v = ValueText.Normalize(i < row.Length ? row[i] : null, opt);
                    parameters[i].Value = (object?)v ?? DBNull.Value;
                }

                try
                {
                    cmd.ExecuteNonQuery();
                }
                catch (SqliteException ex)
                {
                    throw new InvalidOperationException(
                        $"[{table.TableName}] {done + 2}번째 행 적재 실패 " +
                        $"(키: {DescribeKey(row, keyCount)}) → {ex.Message}", ex);
                }

                done++;
                if (done % reportEvery == 0)
                    progress?.Report(new ImportProgress(sourceIndex, sourceCount, table.TableName, "INSERT", done, total));
            }

            CreateIndex(conn, tx, table, spec);

            if (opt.WriteManifest)
                WriteManifest(conn, tx, table, keyColumns, table.Columns.Count, done);

            tx.Commit();

            return new TableResult
            {
                TableName = table.TableName,
                SourcePath = table.SourcePath,
                SourceObject = table.SourceObject,
                KeyColumns = keyColumns,
                ColumnCount = table.Columns.Count,
                RowCount = done,
                Success = true,
                Elapsed = sw.Elapsed
            };
        }
    }

    private static string BuildCreateSql(TableData table, int keyCount, ImportOptions opt)
    {
        var sb = new StringBuilder();
        sb.Append("CREATE TABLE IF NOT EXISTS ").Append(Naming.Quote(table.TableName)).AppendLine(" (");

        for (var i = 0; i < table.Columns.Count; i++)
        {
            sb.Append("  ")
              .Append(Naming.Quote(table.Columns[i]))
              .Append(" TEXT");

            // SQLite는 (INTEGER PRIMARY KEY 예외를 빼면) PK 컬럼에도 NULL을 허용하므로 명시적으로 붙인다.
            if (i < keyCount && opt.KeyColumnsNotNull) sb.Append(" NOT NULL");

            if (i < table.Columns.Count - 1 || keyCount > 0) sb.Append(',');
            sb.AppendLine();
        }

        if (keyCount > 0)
        {
            // 지정된 개수만큼 복합 PK. 값 결합(column1|column2) 없이 컬럼을 그대로 유지한다.
            sb.Append("  PRIMARY KEY (")
              .Append(string.Join(", ", table.Columns.Take(keyCount).Select(Naming.Quote)))
              .AppendLine(")");
        }

        sb.AppendLine(");");
        return sb.ToString();
    }

    private static void WriteManifest(
        SqliteConnection conn, SqliteTransaction tx, TableData table,
        List<string> keyColumns, int colCount, long rowCount)
    {
        Exec(conn, tx, $"""
            CREATE TABLE IF NOT EXISTS {Naming.Quote(ManifestTable)} (
              "table_name"    TEXT NOT NULL PRIMARY KEY,
              "source_path"   TEXT,
              "source_object" TEXT,
              "key_columns"   TEXT,
              "column_count"  INTEGER,
              "row_count"     INTEGER,
              "imported_at"   TEXT
            );
            """);

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            INSERT OR REPLACE INTO {Naming.Quote(ManifestTable)}
              ("table_name","source_path","source_object","key_columns","column_count","row_count","imported_at")
            VALUES ($t,$p,$o,$k,$c,$r,$d);
            """;
        cmd.Parameters.AddWithValue("$t", table.TableName);
        cmd.Parameters.AddWithValue("$p", table.SourcePath);
        cmd.Parameters.AddWithValue("$o", (object?)table.SourceObject ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$k", string.Join(",", keyColumns));
        cmd.Parameters.AddWithValue("$c", colCount);
        cmd.Parameters.AddWithValue("$r", rowCount);
        cmd.Parameters.AddWithValue("$d", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    private static List<string>? GetExistingColumns(SqliteConnection conn, SqliteTransaction tx, string tableName)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT name FROM pragma_table_info($t) ORDER BY cid;";
        cmd.Parameters.AddWithValue("$t", tableName);

        var cols = new List<string>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) cols.Add(reader.GetString(0));

        return cols.Count == 0 ? null : cols;
    }

    /// <summary>
    /// 소스에만 있는 컬럼을 기존 테이블에 붙인다. 기존 행의 새 컬럼 값은 NULL이 된다.
    /// SQLite의 ADD COLUMN은 항상 <b>맨 뒤</b>에만 붙지만, INSERT를 컬럼명으로 하므로
    /// 소스에서 컬럼이 중간에 끼어들어도 값은 제자리에 들어간다(물리 순서만 달라진다).
    /// </summary>
    private static void AddMissingColumns(SqliteConnection conn, SqliteTransaction tx,
                                          TableData table, List<string> existing, List<string> keyColumns)
    {
        var have = new HashSet<string>(existing, StringComparer.OrdinalIgnoreCase);
        var missing = table.Columns.Where(c => !have.Contains(c)).ToList();

        // PK는 ALTER 로 바꿀 수 없다. 키 컬럼이 기존 테이블에 없으면 애초에 같은 테이블이 아니다.
        var missingKeys = keyColumns.Where(k => !have.Contains(k)).ToList();
        if (missingKeys.Count > 0)
            throw new InvalidOperationException(
                $"[{table.TableName}] PK 컬럼이 기존 테이블에 없습니다: {string.Join(", ", missingKeys)}\n" +
                "  PK는 나중에 추가할 수 없습니다. '데이터 교체' 모드를 쓰거나 테이블명을 변경하세요.");

        foreach (var col in missing)
        {
            // NOT NULL은 붙일 수 없다 — 기존 행을 채울 값이 없기 때문.
            Exec(conn, tx,
                 $"ALTER TABLE {Naming.Quote(table.TableName)} ADD COLUMN {Naming.Quote(col)} TEXT;");
        }
    }

    /// <summary>
    /// spec.IndexColumns 에 적힌 컬럼들로 복합 인덱스 하나를 만든다(PK와 별개).
    /// 재적재 때마다 다시 만들어지므로 교체 모드에서도 유지된다.
    /// </summary>
    private static void CreateIndex(SqliteConnection conn, SqliteTransaction tx,
                                    TableData table, SourceSpec spec)
    {
        var columns = ParseIndexColumns(spec.IndexColumns);
        if (columns.Count == 0) return;

        // 헤더 정리 후의 실제 컬럼명으로 맞춘다(대소문자 무시).
        var resolved = new List<string>(columns.Count);
        foreach (var wanted in columns)
        {
            var actual = table.Columns.FirstOrDefault(
                c => string.Equals(c, wanted, StringComparison.OrdinalIgnoreCase));

            if (actual is null)
                throw new InvalidOperationException(
                    $"[{table.TableName}] 인덱스 컬럼 '{wanted}' 이(가) 테이블에 없습니다.\n" +
                    $"  사용 가능한 컬럼: {string.Join(", ", table.Columns)}");

            resolved.Add(actual);
        }

        var indexName = "ix_" + table.TableName + "_" + string.Join("_", resolved);
        Exec(conn, tx,
             $"CREATE INDEX IF NOT EXISTS {Naming.Quote(Naming.Sanitize(indexName, "ix"))} " +
             $"ON {Naming.Quote(table.TableName)} ({string.Join(", ", resolved.Select(Naming.Quote))});");
    }

    /// <summary>콤마로 구분된 컬럼명을 잘라낸다. 공백과 빈 항목은 버린다.</summary>
    public static List<string> ParseIndexColumns(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? new List<string>()
            : text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                  .ToList();

    private static bool ColumnsMatch(List<string> a, List<string> b)
        => a.Count == b.Count
           && !a.Where((t, i) => !string.Equals(t, b[i], StringComparison.OrdinalIgnoreCase)).Any();

    private static string DescribeKey(object?[] row, int keyCount)
        => keyCount == 0
            ? "(PK 없음)"
            : string.Join(", ", row.Take(keyCount).Select(v => v?.ToString() ?? "NULL"));

    private static void Exec(SqliteConnection conn, SqliteTransaction tx, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
