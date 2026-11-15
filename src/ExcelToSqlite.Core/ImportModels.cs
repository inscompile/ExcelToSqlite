namespace ExcelToSqlite.Core;

/// <summary>입력 소스 종류.</summary>
public enum SourceKind
{
    /// <summary>엑셀 파일의 "이름(Name)"으로 정의된 범위.</summary>
    ExcelNamedRange,
    /// <summary>엑셀 시트 전체(사용 영역).</summary>
    ExcelSheet,
    /// <summary>구분자 텍스트 파일(TSV/CSV/파이프 등).</summary>
    DelimitedText
}

/// <summary>PK 중복 발생 시 처리 방식.</summary>
public enum DuplicateKeyPolicy
{
    /// <summary>오류로 중단.</summary>
    Fail,
    /// <summary>기존 행을 덮어씀(INSERT OR REPLACE).</summary>
    Replace,
    /// <summary>중복 행을 건너뜀(INSERT OR IGNORE).</summary>
    Ignore
}

/// <summary>가져올 소스 1건의 정의. UI 그리드 한 줄에 그대로 대응된다.</summary>
public sealed class SourceSpec
{
    public string FilePath { get; set; } = "";

    public SourceKind Kind { get; set; } = SourceKind.ExcelNamedRange;

    /// <summary>ExcelNamedRange = 이름(Name), ExcelSheet = 시트명. 텍스트면 무시.</summary>
    public string? ObjectName { get; set; }

    /// <summary>생성할 테이블명. 비우면 ObjectName 또는 파일명(확장자 제외)을 사용.</summary>
    public string? TableName { get; set; }

    /// <summary>
    /// PK로 사용할 선두 컬럼 개수. 기본 1(첫 번째 컬럼).
    /// 2 이상이면 SQLite 복합 기본키 PRIMARY KEY(c1, c2, ...)로 선언한다(문자열 결합 안 함).
    /// 0이면 PK를 만들지 않는다.
    /// </summary>
    public int KeyColumnCount { get; set; } = 1;

    /// <summary>
    /// 인덱스를 걸 컬럼명. 콤마로 여러 개를 적으면 그 컬럼들로 <b>복합 인덱스 하나</b>를 만든다.
    /// 예: "product_key" / "product_key, plan_key". 비우면 인덱스를 만들지 않는다.
    /// PK와 별개이며, 재적재할 때마다 다시 생성된다.
    /// </summary>
    public string? IndexColumns { get; set; }

    /// <summary>텍스트 전용. null이면 첫 줄에서 자동 감지(탭/콤마/파이프/세미콜론).</summary>
    public string? Delimiter { get; set; }

    /// <summary>텍스트 전용. null/"auto"이면 BOM → UTF-8 → CP949 순으로 자동 판별.</summary>
    public string? Encoding { get; set; }

    /// <summary>첫 행이 layout(헤더)인지 여부. false면 COL1..COLn 자동 생성.</summary>
    public bool HasHeader { get; set; } = true;

    /// <summary>헤더 앞에서 건너뛸 줄/행 수(주석·제목행 등).</summary>
    public int SkipRows { get; set; } = 0;

    /// <summary>UI 체크박스용. false면 실행에서 제외.</summary>
    public bool Enabled { get; set; } = true;

    public SourceSpec Clone() => (SourceSpec)MemberwiseClone();
}

/// <summary>전체 실행 옵션.</summary>
public sealed class ImportOptions
{
    /// <summary>동일 이름 테이블이 있으면 DROP 후 재생성.</summary>
    public bool DropExistingTable { get; set; } = true;

    public DuplicateKeyPolicy DuplicateKey { get; set; } = DuplicateKeyPolicy.Fail;

    /// <summary>PK 컬럼에 NOT NULL을 붙인다(SQLite는 PK라도 기본적으로 NULL 허용).</summary>
    public bool KeyColumnsNotNull { get; set; } = true;

    /// <summary>
    /// 추가(append) 모드에서 소스에만 있는 컬럼을 기존 테이블에 <c>ALTER TABLE ADD COLUMN</c> 으로
    /// 붙인다. 기존 행의 새 컬럼은 NULL이 된다. 기본은 꺼 둔다 — 엉뚱한 테이블에 대고 실행했을 때
    /// 스키마가 조용히 바뀌는 것을 막기 위함.
    /// </summary>
    public bool AllowAddColumns { get; set; }

    public bool TrimValues { get; set; } = true;

    /// <summary>빈 문자열을 NULL로 저장.</summary>
    public bool EmptyStringAsNull { get; set; } = true;

    /// <summary>_import_manifest 테이블에 적재 이력을 남긴다.</summary>
    public bool WriteManifest { get; set; } = true;

    /// <summary>소스 하나가 실패해도 나머지를 계속 처리.</summary>
    public bool ContinueOnError { get; set; } = true;
}

public sealed class ImportRequest
{
    public string DbPath { get; set; } = "";
    public List<SourceSpec> Sources { get; set; } = new();
    public ImportOptions Options { get; set; } = new();
}

/// <summary>UI 진행률 바인딩용.</summary>
public sealed record ImportProgress(
    int SourceIndex,
    int SourceCount,
    string TableName,
    string Stage,
    long RowsDone,
    long RowsTotal);

public sealed class TableResult
{
    public string TableName { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string? SourceObject { get; set; }
    public List<string> KeyColumns { get; set; } = new();
    public int ColumnCount { get; set; }
    public long RowCount { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
    public TimeSpan Elapsed { get; set; }
}

public sealed class ImportResult
{
    public string DbPath { get; set; } = "";
    public List<TableResult> Tables { get; } = new();
    public TimeSpan Elapsed { get; set; }
    public bool Success => Tables.Count > 0 && Tables.TrueForAll(t => t.Success);
}
