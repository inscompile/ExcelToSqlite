// 앱 아이콘 그리기 — 초록 바탕에 흰 표(왼쪽 위)와 파란 DB 원통(오른쪽 아래): 표를 데이터베이스에 넣는다.
// 1024 기준으로 그린 뒤 크기마다 다시 그려 PNG를 만들고, 맥 .icns(iconutil)와 윈도 .ico(PNG 묶음)를 만든다.
using System.Diagnostics;
using SkiaSharp;

var dir = new DirectoryInfo(AppContext.BaseDirectory);
while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ExcelToSqlite.sln"))) dir = dir.Parent;
if (dir == null) { Console.Error.WriteLine("ExcelToSqlite.sln 이 있는 폴더를 찾지 못했습니다."); return 1; }
var assets = Path.Combine(dir.FullName, "src", "ExcelToSqlite.Desktop", "Assets");
Directory.CreateDirectory(assets);

byte[] Render(int size)
{
    using var surface = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul));
    var c = surface.Canvas;
    c.Clear(SKColors.Transparent);
    c.Scale(size / 1024f);

    // 바탕: 둥근 사각형 (CodeFlowTracer와 같은 틀)
    using (var bg = new SKPaint { IsAntialias = true })
    {
        bg.Shader = SKShader.CreateLinearGradient(new SKPoint(100, 80), new SKPoint(924, 960),
            [new SKColor(0x2B, 0xA8, 0x6E), new SKColor(0x0F, 0x5E, 0x3C)], SKShaderTileMode.Clamp);
        c.DrawRoundRect(new SKRoundRect(new SKRect(72, 72, 952, 952), 200), bg);
    }

    using var shadow = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 55), MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 14) };

    // 표: 흰 카드 + 머리줄 + 격자
    var sheet = new SKRect(176, 196, 616, 636);
    c.DrawRoundRect(new SKRoundRect(new SKRect(sheet.Left, sheet.Top + 14, sheet.Right, sheet.Bottom + 14), 40), shadow);
    using (var white = new SKPaint { IsAntialias = true, Color = SKColors.White })
        c.DrawRoundRect(new SKRoundRect(sheet, 40), white);
    using (var head = new SKPaint { IsAntialias = true, Color = new SKColor(0xC9, 0xEE, 0xD8) })
    {
        c.Save();
        c.ClipRoundRect(new SKRoundRect(sheet, 40), antialias: true);
        c.DrawRect(new SKRect(sheet.Left, sheet.Top, sheet.Right, sheet.Top + 110), head);
        c.Restore();
    }
    using (var grid = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 14, Color = new SKColor(0x8F, 0xD3, 0xAC) })
    {
        for (var i = 1; i <= 2; i++)
        {
            var x = sheet.Left + sheet.Width * i / 3;
            c.DrawLine(x, sheet.Top + 24, x, sheet.Bottom - 24, grid);
        }
        foreach (var y in new[] { sheet.Top + 110, sheet.Top + 220, sheet.Top + 330 })
            c.DrawLine(sheet.Left + 24, y, sheet.Right - 24, y, grid);
    }

    // DB 원통: 위 타원 + 몸통 + 아래 타원, 칸 구분선 두 줄
    float l = 470, r = 830, top = 500, bottom = 800, eh = 120;   // eh = 타원 높이
    var topOval = new SKRect(l, top - eh / 2, r, top + eh / 2);
    var bottomOval = new SKRect(l, bottom - eh / 2, r, bottom + eh / 2);
    using var body = new SKPath();
    body.AddRect(new SKRect(l, top, r, bottom));
    body.AddOval(topOval);
    body.AddOval(bottomOval);
    using var silhouette = body.Simplify() ?? body;

    c.Save(); c.Translate(0, 16); c.DrawPath(silhouette, shadow); c.Restore();
    using (var halo = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 36, StrokeJoin = SKStrokeJoin.Round, Color = new SKColor(0x15, 0x6E, 0x47) })
        c.DrawPath(silhouette, halo);   // 표와 겹치는 곳을 갈라 보이게
    using (var fill = new SKPaint { IsAntialias = true })
    {
        fill.Shader = SKShader.CreateLinearGradient(new SKPoint(l, 0), new SKPoint(r, 0),
            [new SKColor(0x4A, 0x8B, 0xF0), new SKColor(0x1D, 0x4E, 0xA8)], SKShaderTileMode.Clamp);
        c.DrawPath(silhouette, fill);
    }
    using (var band = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 14, Color = new SKColor(0xA9, 0xC8, 0xF7) })
    {
        foreach (var y in new[] { top + (bottom - top) / 3, top + (bottom - top) * 2 / 3 })
        {
            using var arc = new SKPath();
            arc.AddArc(new SKRect(l, y - eh / 2, r, y + eh / 2), 180, -180);   // 아래쪽 반 타원
            c.DrawPath(arc, band);
        }
    }
    using (var lid = new SKPaint { IsAntialias = true, Color = new SKColor(0x9C, 0xC2, 0xF8) })
        c.DrawOval(topOval, lid);
    using (var rim = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 10, Color = new SKColor(0xD6, 0xE6, 0xFD) })
        c.DrawOval(topOval, rim);

    using var img = surface.Snapshot();
    using var data = img.Encode(SKEncodedImageFormat.Png, 100);
    return data.ToArray();
}

// 창 아이콘·README용
File.WriteAllBytes(Path.Combine(assets, "AppIcon.png"), Render(256));

// 윈도 .ico: 크기마다 다시 그린 PNG를 그대로 담는다 (Vista부터 지원)
int[] icoSizes = [16, 24, 32, 48, 64, 128, 256];
var pngs = icoSizes.Select(Render).ToList();
using (var fs = File.Create(Path.Combine(assets, "AppIcon.ico")))
using (var w = new BinaryWriter(fs))
{
    w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)icoSizes.Length);
    var offset = 6 + 16 * icoSizes.Length;
    for (var i = 0; i < icoSizes.Length; i++)
    {
        var s = icoSizes[i];
        w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)(s >= 256 ? 0 : s));   // 256은 0으로 적는다
        w.Write((byte)0); w.Write((byte)0);
        w.Write((ushort)1); w.Write((ushort)32);
        w.Write(pngs[i].Length); w.Write(offset);
        offset += pngs[i].Length;
    }
    foreach (var p in pngs) w.Write(p);
}

// 맥 .icns: iconutil 이 정해진 이름의 PNG 묶음(.iconset)을 받는다
if (OperatingSystem.IsMacOS())
{
    var set = Path.Combine(Path.GetTempPath(), "ExcelToSqlite-AppIcon.iconset");
    if (Directory.Exists(set)) Directory.Delete(set, true);
    Directory.CreateDirectory(set);
    foreach (var s in new[] { 16, 32, 128, 256, 512 })
    {
        File.WriteAllBytes(Path.Combine(set, $"icon_{s}x{s}.png"), Render(s));
        File.WriteAllBytes(Path.Combine(set, $"icon_{s}x{s}@2x.png"), Render(s * 2));
    }
    using var p = Process.Start("iconutil", ["-c", "icns", set, "-o", Path.Combine(assets, "AppIcon.icns")]);
    p.WaitForExit();
    Directory.Delete(set, true);
    if (p.ExitCode != 0) { Console.Error.WriteLine("iconutil 실패"); return 1; }
}
else
{
    Console.WriteLine("맥이 아니라 AppIcon.icns 는 만들지 않았습니다.");
}

Console.WriteLine($"아이콘: {assets}");
return 0;
