namespace ExcelToSqlite.Core;

/// <summary>리더가 만들어 낸 메모리상의 테이블(헤더 + 데이터 행).</summary>
public sealed class TableData
{
    public required string TableName { get; init; }
    public required string SourcePath { get; init; }
    public string? SourceObject { get; init; }
    public required List<string> Columns { get; init; }
    public required List<object?[]> Rows { get; init; }
}
