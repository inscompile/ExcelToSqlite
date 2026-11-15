using System.Text;

namespace ExcelToSqlite.Core;

public static class Naming
{
    /// <summary>SQLite 식별자 인용(내부 따옴표 이스케이프).</summary>
    public static string Quote(string identifier)
        => "\"" + identifier.Replace("\"", "\"\"") + "\"";

    /// <summary>테이블/컬럼명으로 쓸 수 있게 정리. 한글은 그대로 허용된다.</summary>
    public static string Sanitize(string? raw, string fallback)
    {
        var s = (raw ?? "").Trim();
        if (s.Length == 0) return fallback;

        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            sb.Append(char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_');

        s = sb.ToString().Trim('_');
        if (s.Length == 0) return fallback;
        if (char.IsDigit(s[0])) s = "_" + s;   // 숫자로 시작 금지
        return s;
    }

    /// <summary>헤더 배열을 유일하고 유효한 컬럼명으로 정규화.</summary>
    public static List<string> NormalizeHeaders(IReadOnlyList<string?> raw)
    {
        var result = new List<string>(raw.Count);
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < raw.Count; i++)
        {
            var name = Sanitize(raw[i], $"COL{i + 1}");

            if (seen.TryGetValue(name, out var n))
            {
                seen[name] = ++n;
                name = $"{name}_{n}";
            }
            else
            {
                seen[name] = 1;
            }

            result.Add(name);
        }

        return result;
    }
}
