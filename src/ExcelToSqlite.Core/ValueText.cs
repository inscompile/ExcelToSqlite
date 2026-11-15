using System.Globalization;

namespace ExcelToSqlite.Core;

/// <summary>
/// 모든 값을 문자열로 바꾼다. 이 프로그램은 타입 추론을 하지 않고 전 컬럼을 TEXT로 적재한다.
/// 숫자·날짜 해석은 적재된 뒤 SQL에서 `CAST` 로 하면 된다.
/// </summary>
public static class ValueText
{
    /// <summary>셀 값 → 적재할 문자열. null 과 (옵션에 따라) 빈 문자열은 NULL로 남긴다.</summary>
    public static string? Normalize(object? value, ImportOptions opt)
    {
        if (value is null) return null;

        var s = ToText(value);
        if (opt.TrimValues) s = s.Trim();

        return s.Length == 0 && opt.EmptyStringAsNull ? null : s;
    }

    /// <summary>
    /// 엑셀 표시 서식이 아니라 <b>내부 값</b>을 문자열로 만든다.
    /// 요율이 "15%"로 보여도 0.15로 적재되고, 반올림된 표시값 대신 원래 정밀도가 남는다.
    /// </summary>
    private static string ToText(object value) => value switch
    {
        string s => s,
        DateTime dt => dt.TimeOfDay == TimeSpan.Zero
            ? dt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        DateOnly d0 => d0.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        TimeSpan ts => ts.ToString(),
        bool b => b ? "1" : "0",
        double d => d.ToString("R", CultureInfo.InvariantCulture),   // 왕복 정밀도 유지
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        IFormattable fm => fm.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };
}
