namespace ExcelToSqlite.Core;

/// <summary>A1 참조 문자열을 다룬다. "'손해율(위험률)'!$AG$21:$AK$8923" 같은 정의된 이름의 값이 대상.</summary>
public static class RefParser
{
    /// <summary>
    /// 여러 영역(콤마 구분)이면 <b>첫 영역</b>만 쓴다. #REF! 등으로 깨졌으면 false.
    /// </summary>
    public static bool TryParseFirstArea(string? refText, out string? sheetName, out CellArea area)
    {
        sheetName = null;
        area = default;

        if (string.IsNullOrWhiteSpace(refText)) return false;
        if (refText.Contains("#REF!", StringComparison.Ordinal)) return false;

        var first = SplitFirstArea(refText);
        if (first.Length == 0) return false;

        // 시트명은 따옴표로 감싸질 수 있고, 그 안에 '!' 나 ',' 가 들어갈 수 있다.
        var bang = FindSheetSeparator(first);
        if (bang < 0) return false;

        var sheet = first[..bang].Trim();
        var cells = first[(bang + 1)..].Trim();

        if (sheet.Length >= 2 && sheet[0] == '\'' && sheet[^1] == '\'')
            sheet = sheet[1..^1].Replace("''", "'");

        // 외부 통합문서 참조 "[1]Sheet1" 는 지원하지 않는다.
        if (sheet.StartsWith('[')) return false;

        sheetName = sheet;
        return TryParseArea(cells, out area);
    }

    /// <summary>"$A$1:$C$10" / "A1" 형태를 영역으로.</summary>
    public static bool TryParseArea(string cells, out CellArea area)
    {
        area = default;
        cells = cells.Replace("$", "");
        if (cells.Length == 0) return false;

        var colon = cells.IndexOf(':');
        var startText = colon < 0 ? cells : cells[..colon];
        var endText = colon < 0 ? cells : cells[(colon + 1)..];

        if (!TryParseCell(startText, out var r1, out var c1)) return false;
        if (!TryParseCell(endText, out var r2, out var c2)) return false;

        area = new CellArea(Math.Min(r1, r2), Math.Max(r1, r2), Math.Min(c1, c2), Math.Max(c1, c2));
        return true;
    }

    /// <summary>"AG21" → (21, 33). 열만/행만 있는 참조(A:A, 1:1)는 지원하지 않는다.</summary>
    public static bool TryParseCell(string text, out int row, out int col)
    {
        row = col = 0;
        var i = 0;

        while (i < text.Length && char.IsAsciiLetter(text[i]))
        {
            col = col * 26 + (char.ToUpperInvariant(text[i]) - 'A' + 1);
            i++;
        }

        if (col == 0 || i == text.Length) return false;

        for (; i < text.Length; i++)
        {
            if (!char.IsAsciiDigit(text[i])) return false;
            row = row * 10 + (text[i] - '0');
        }

        return row > 0;
    }

    /// <summary>"AG21" 에서 열 번호만. 실패하면 -1.</summary>
    public static int ColumnOf(string cellRef)
    {
        var col = 0;
        foreach (var ch in cellRef)
        {
            if (char.IsAsciiLetter(ch)) col = col * 26 + (char.ToUpperInvariant(ch) - 'A' + 1);
            else break;
        }
        return col == 0 ? -1 : col;
    }

    /// <summary>따옴표 밖의 첫 콤마까지를 잘라낸다.</summary>
    private static string SplitFirstArea(string refText)
    {
        var inQuote = false;
        for (var i = 0; i < refText.Length; i++)
        {
            if (refText[i] == '\'') inQuote = !inQuote;
            else if (refText[i] == ',' && !inQuote) return refText[..i].Trim();
        }
        return refText.Trim();
    }

    /// <summary>따옴표 밖의 마지막 '!' 위치.</summary>
    private static int FindSheetSeparator(string text)
    {
        var inQuote = false;
        var last = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\'') inQuote = !inQuote;
            else if (text[i] == '!' && !inQuote) last = i;
        }
        return last;
    }
}

/// <summary>숫자 서식 코드가 날짜인지 판정한다.</summary>
public static class NumberFormat
{
    /// <summary>엑셀 내장 날짜/시간 서식 ID.</summary>
    public static bool IsBuiltinDateId(int id)
        => id is (>= 14 and <= 22) or (>= 27 and <= 36) or (>= 45 and <= 47) or (>= 50 and <= 58);

    /// <summary>
    /// 사용자 정의 서식 코드에 날짜 토큰이 있는지. 따옴표 리터럴과 [빨강] 같은 대괄호 구간은 무시한다.
    /// </summary>
    public static bool IsDateFormatCode(string? code)
    {
        if (string.IsNullOrEmpty(code)) return false;

        var inQuote = false;
        var inBracket = false;

        for (var i = 0; i < code.Length; i++)
        {
            var ch = code[i];

            if (ch == '"') { inQuote = !inQuote; continue; }
            if (inQuote) continue;
            if (ch == '[') { inBracket = true; continue; }
            if (ch == ']') { inBracket = false; continue; }
            if (inBracket) continue;
            if (ch == '\\') { i++; continue; }       // 이스케이프된 리터럴 한 글자

            switch (char.ToLowerInvariant(ch))
            {
                case 'y':
                case 'd':
                case 'h':
                    return true;
                case 'm':
                    return true;   // 월/분 모두 날짜·시간 서식이다
                case 's':
                    return true;
            }
        }

        return false;
    }
}
