using System.Text.Json;
using System.Text.Json.Serialization;
using ExcelToSqlite.Core;

namespace ExcelToSqlite.Cli;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] is "-h" or "--help")
            {
                PrintUsage();
                return 0;
            }

            // 진단용: 엑셀의 이름/시트 목록 출력 (UI 드롭다운과 동일한 소스)
            if (args[0] == "--list-names")
            {
                var file = args[1];
                var (all, sheets) = ExcelSourceReader.Inspect(file);
                var usable = all.Where(n => !n.IsSingleCell).ToList();
                var singles = all.Where(n => n.IsSingleCell).ToList();

                Console.WriteLine($"[Defined Names] {usable.Count}개");
                foreach (var n in usable) Console.WriteLine("  " + n.Name);

                if (singles.Count > 0)
                {
                    Console.WriteLine($"[Skipped: 단일 셀] {singles.Count}개 — 테이블이 아니므로 적재 대상에서 제외");
                    foreach (var n in singles) Console.WriteLine("  " + n.Name);
                }

                Console.WriteLine($"[Sheets] {sheets.Count}개");
                foreach (var s in sheets) Console.WriteLine("  " + s);
                return 0;
            }

            var request = BuildRequest(args);
            if (request.Sources.Count == 0)
            {
                Console.Error.WriteLine("소스가 지정되지 않았습니다. --help 를 참고하세요.");
                return 2;
            }

            var progress = new Progress<ImportProgress>(p =>
            {
                if (p.RowsTotal > 0 && p.Stage == "INSERT")
                    Console.Write($"\r  {p.TableName}: {p.RowsDone:N0}/{p.RowsTotal:N0}   ");
            });

            var result = new ImportService().Run(request, progress);

            Console.WriteLine();
            Console.WriteLine($"DB: {result.DbPath}");
            foreach (var t in result.Tables)
            {
                Console.WriteLine(t.Success
                    ? $"  [OK]   {t.TableName,-28} {t.RowCount,10:N0} rows  " +
                      $"cols={t.ColumnCount}  pk=({string.Join(",", t.KeyColumns)})  {t.Elapsed.TotalSeconds:F1}s"
                    : $"  [FAIL] {t.TableName,-28} {t.Error}");
            }
            Console.WriteLine($"총 {result.Elapsed.TotalSeconds:F1}초");

            return result.Success ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("오류: " + ex.Message);
            return 3;
        }
    }

    private static ImportRequest BuildRequest(string[] args)
    {
        ImportRequest request = new();
        SourceSpec? current = null;

        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            string Next() => ++i < args.Length ? args[i] : throw new ArgumentException($"{a} 뒤에 값이 필요합니다.");

            switch (a)
            {
                case "--config":
                    var json = File.ReadAllText(Next());
                    request = JsonSerializer.Deserialize<ImportRequest>(json, JsonOpts)
                              ?? throw new InvalidOperationException("설정 파일을 해석할 수 없습니다.");
                    current = null;
                    break;

                case "--db":
                    request.DbPath = Next();
                    break;

                case "--excel":
                    current = new SourceSpec { FilePath = Next(), Kind = SourceKind.ExcelNamedRange };
                    request.Sources.Add(current);
                    break;

                case "--sheet-of":
                    current = new SourceSpec { FilePath = Next(), Kind = SourceKind.ExcelSheet };
                    request.Sources.Add(current);
                    break;

                case "--text":
                    current = new SourceSpec { FilePath = Next(), Kind = SourceKind.DelimitedText };
                    request.Sources.Add(current);
                    break;

                case "--name":
                case "--sheet":
                    Require(current, a).ObjectName = Next();
                    break;

                case "--table":
                    Require(current, a).TableName = Next();
                    break;

                case "--keys":
                    Require(current, a).KeyColumnCount = int.Parse(Next());
                    break;

                case "--index":
                    Require(current, a).IndexColumns = Next();
                    break;

                case "--delim":
                    Require(current, a).Delimiter = Next();
                    break;

                case "--encoding":
                    Require(current, a).Encoding = Next();
                    break;

                case "--skip":
                    Require(current, a).SkipRows = int.Parse(Next());
                    break;

                case "--no-header":
                    Require(current, a).HasHeader = false;
                    break;

                case "--on-duplicate":
                    request.Options.DuplicateKey = Enum.Parse<DuplicateKeyPolicy>(Next(), true);
                    break;

                case "--append":
                    request.Options.DropExistingTable = false;
                    break;

                case "--add-columns":
                    request.Options.AllowAddColumns = true;
                    break;

                case "--stop-on-error":
                    request.Options.ContinueOnError = false;
                    break;

                default:
                    throw new ArgumentException($"알 수 없는 옵션: {a}");
            }
        }

        return request;
    }

    private static SourceSpec Require(SourceSpec? s, string opt)
        => s ?? throw new ArgumentException($"{opt} 앞에 --excel / --text / --sheet-of 가 필요합니다.");

    private static void PrintUsage() => Console.WriteLine("""
        xl2sqlite - 엑셀 명명범위 / 텍스트 파일 -> SQLite

        사용법:
          xl2sqlite --db <out.db> [소스...] [옵션]
          xl2sqlite --config sources.json
          xl2sqlite --list-names <file.xlsx>

        소스:
          --excel <path>       엑셀 파일 (뒤에 --name 으로 명명범위 지정)
          --sheet-of <path>    엑셀 파일 (뒤에 --sheet 로 시트 지정, 생략 시 첫 시트)
          --text <path>        구분자 텍스트 파일

        소스별 옵션 (바로 앞 소스에 적용):
          --name <name>        엑셀 이름(Name). "Sheet1!Name" 형식도 가능
          --sheet <name>       시트명
          --table <name>       테이블명 (기본: 이름/파일명)
          --keys <n>           PK로 쓸 선두 컬럼 개수 (기본 1, 0이면 PK 없음)
          --index <cols>       인덱스를 걸 컬럼명. 콤마로 여러 개 = 복합 인덱스 1개
                               예: --index "product_key" / --index "product_key,plan_key"
          --delim <char>       구분자. "\t" 또는 tab / , / | (기본: 자동 감지)
          --encoding <enc>     utf-8, cp949 등 (기본: 자동 감지)
          --skip <n>           헤더 앞에서 건너뛸 행 수
          --no-header          첫 행이 헤더가 아님 (COL1..COLn 생성)

        전역 옵션:
          --on-duplicate <fail|replace|ignore>   PK 중복 처리 (기본 fail)
          --append             기존 테이블 유지하고 추가 (기본은 DROP 후 재생성)
          --add-columns        --append 시 소스에만 있는 컬럼을 기존 테이블에 추가
                               (기존 행은 NULL. 기본은 꺼져 있어 컬럼이 다르면 실패)
          --stop-on-error      첫 실패에서 중단

        모든 컬럼은 TEXT로 적재된다(타입 추론 없음).

        예:
          xl2sqlite --db pricing.db \
            --excel base.xlsx --name RiskRate_Dog --keys 2 \
            --excel base.xlsx --name Benefit --table BENEFIT \
            --text rate_2026.tsv --keys 3
        """);
}
