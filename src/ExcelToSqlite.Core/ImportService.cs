using Microsoft.Data.Sqlite;

namespace ExcelToSqlite.Core;

/// <summary>여러 소스를 하나의 SQLite 파일로 적재하는 진입점. UI/CLI 공용.</summary>
public sealed class ImportService
{
    public ImportResult Run(ImportRequest request,
                            IProgress<ImportProgress>? progress = null,
                            CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.DbPath))
            throw new ArgumentException("DbPath가 비어 있습니다.", nameof(request));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = new ImportResult
        {
            DbPath = Path.GetFullPath(SourceExpander.EnsureDbExtension(request.DbPath))
        };

        var dir = Path.GetDirectoryName(result.DbPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        // 같은 파일의 소스를 붙여서 처리한다 — 파일당 워크북을 한 번만 열기 위함.
        // 파일 순서는 처음 등장한 순서, 파일 안에서는 원래 지정 순서를 유지한다.
        var sources = request.Sources
            .Where(s => s.Enabled)
            .Select((s, idx) => (Spec: s, Idx: idx))
            .GroupBy(t => FullPathOrRaw(t.Spec.FilePath), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Min(t => t.Idx))
            .SelectMany(g => g.OrderBy(t => t.Idx))
            .Select(t => t.Spec)
            .ToList();

        using var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = result.DbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString());

        conn.Open();
        ExecPragma(conn, "PRAGMA journal_mode = WAL;");
        ExecPragma(conn, "PRAGMA synchronous = NORMAL;");
        ExecPragma(conn, "PRAGMA temp_store = MEMORY;");

        // 현재 열려 있는 엑셀 워크북. 다음 소스가 같은 파일이면 재사용한다.
        XlsxPackage? openWb = null;
        string? openWbPath = null;

        try
        {
            for (var i = 0; i < sources.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var spec = sources[i];
                var label = spec.TableName ?? spec.ObjectName ?? Path.GetFileName(spec.FilePath);

                try
                {
                    progress?.Report(new ImportProgress(i, sources.Count, label, "READ", 0, 0));

                    TableData table;
                    if (spec.Kind == SourceKind.DelimitedText)
                    {
                        table = DelimitedTextReader.Read(spec);
                    }
                    else
                    {
                        var path = FullPathOrRaw(spec.FilePath);
                        if (openWb is null || !string.Equals(openWbPath, path, StringComparison.OrdinalIgnoreCase))
                        {
                            openWb?.Dispose();
                            openWb = null;
                            openWb = ExcelSourceReader.OpenWorkbook(spec.FilePath);
                            openWbPath = path;
                        }

                        table = ExcelSourceReader.Read(openWb, spec);
                    }

                    progress?.Report(new ImportProgress(i, sources.Count, table.TableName, "WRITE", 0, table.Rows.Count));

                    result.Tables.Add(
                        SqliteTableWriter.Write(conn, table, spec, request.Options, progress, i, sources.Count, ct));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    result.Tables.Add(new TableResult
                    {
                        TableName = Naming.Sanitize(label, "UNKNOWN"),
                        SourcePath = spec.FilePath,
                        SourceObject = spec.ObjectName,
                        Success = false,
                        Error = ex.Message
                    });

                    if (!request.Options.ContinueOnError) break;
                }
            }
        }
        finally
        {
            openWb?.Dispose();
        }

        ExecPragma(conn, "PRAGMA wal_checkpoint(TRUNCATE);");
        ExecPragma(conn, "PRAGMA optimize;");

        // 적재 중에는 WAL이 빠르지만, 끝나고 나면 -wal / -shm 곁딸린 파일이 남는다.
        // 저널 모드를 되돌리면 SQLite가 두 파일을 지워 .db 하나만 남는다.
        ExecPragma(conn, "PRAGMA journal_mode = DELETE;");

        result.Elapsed = sw.Elapsed;
        return result;
    }

    public Task<ImportResult> RunAsync(ImportRequest request,
                                       IProgress<ImportProgress>? progress = null,
                                       CancellationToken ct = default)
        => Task.Run(() => Run(request, progress, ct), ct);

    /// <summary>UI "미리보기"용: 상위 N행만 읽어 온다.</summary>
    public TableData Preview(SourceSpec spec, int maxRows = 100)
    {
        var table = spec.Kind == SourceKind.DelimitedText
            ? DelimitedTextReader.Read(spec)
            : ExcelSourceReader.Read(spec);

        if (table.Rows.Count > maxRows)
            table.Rows.RemoveRange(maxRows, table.Rows.Count - maxRows);

        return table;
    }

    /// <summary>그룹핑 키. 잘못된 경로 때문에 전체 실행이 죽지 않도록 실패하면 원문을 쓴다.</summary>
    private static string FullPathOrRaw(string path)
    {
        try { return Path.GetFullPath(path); }
        catch { return path; }
    }

    private static void ExecPragma(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
