// 오픈소스 고지 만들기 — bash scripts/publish.sh all 로 배포 파일을 만든 뒤 저장소 폴더에서:
//   dotnet run scripts/Notices/Notices.cs            → THIRD-PARTY-NOTICES.txt 를 새로 쓴다
//   dotnet run scripts/Notices/Notices.cs -- --check  → 배포 파일에 든 패키지·버전이 고지에 다 있는지만 본다 (Actions)
//
// 화면 앱 배포 파일(윈도 exe, 맥 .app)에 실제로 들어가는 패키지만 모은다 — 플랫폼별 deps.json 기준이라
// 디버그 전용(Avalonia.Diagnostics)·빌드 전용·아이콘 생성기 패키지는 들어가지 않는다.
// 패키지의 라이선스·저작권, 패키지 안의 제3자 고지(같은 내용은 한 번만), 패키지에 글이 없는 라이선스 전문
// (SQLitePCLRaw 의 Apache-2.0, Avalonia.Fonts.Inter 안 Inter 글꼴의 SIL OFL 1.1 — 이 폴더의 txt), .NET 런타임 고지.
// 패키지나 버전을 바꾸면 다시 돌린다.
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

var check = args.Contains("--check");
string? root = null;
for (var d = Directory.GetCurrentDirectory(); d != null && root == null; d = Path.GetDirectoryName(d))
    if (File.Exists(Path.Combine(d, "ExcelToSqlite.sln"))) root = d;
if (root == null) { Console.Error.WriteLine("저장소 폴더(ExcelToSqlite.sln 이 있는 곳)에서 돌리세요."); return 1; }
var here = Path.Combine(root, "scripts", "Notices");
var outPath = Path.Combine(root, "THIRD-PARTY-NOTICES.txt");
var nuget = Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } np ? np
    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

// 플랫폼별 배포 deps.json (윈도는 한 파일 exe 안에 묶이므로 obj 쪽 사본을 읽는다)
var depsFiles = new (string Label, string Path)[]
{
    ("윈도", Path.Combine(root, "src", "ExcelToSqlite.Desktop", "obj", "Release", "net10.0", "win-x64", "ExcelToSqlite.deps.json")),
    ("맥", Path.Combine(root, "dist", "ExcelToSqlite.app", "Contents", "MacOS", "ExcelToSqlite.deps.json")),
}.Where(f => File.Exists(f.Path)).ToList();
if (depsFiles.Count == 0 || (!check && depsFiles.Count < 2))
{
    Console.Error.WriteLine("배포 파일이 없습니다 — 먼저 bash scripts/publish.sh all");
    return 1;
}

var packages = new SortedDictionary<string, (string Version, SortedSet<string> Where)>(StringComparer.OrdinalIgnoreCase);
string? runtimeDir = null;
foreach (var (label, path) in depsFiles)
{
    using var doc = JsonDocument.Parse(File.ReadAllText(path));
    foreach (var lib in doc.RootElement.GetProperty("libraries").EnumerateObject())
    {
        var type = lib.Value.GetProperty("type").GetString();
        var parts = lib.Name.Split('/');
        if (type == "package")
        {
            if (!packages.TryGetValue(parts[0], out var p)) packages[parts[0]] = p = (parts[1], new SortedSet<string>());
            p.Where.Add(label);
        }
        else if (type == "runtimepack")   // runtimepack.Microsoft.NETCore.App.Runtime.<rid>/<버전>
            runtimeDir ??= Path.Combine(nuget, parts[0]["runtimepack.".Length..].ToLowerInvariant(), parts[1]);
    }
}

if (check)
{
    // 배포 파일에 든 패키지·버전이 고지 목록에 다 있는지만 본다 (.NET 런타임 패치 번호는 빌드하는 컴퓨터마다 달라 보지 않음)
    var text = File.Exists(outPath) ? File.ReadAllText(outPath) : "";
    var missing = packages.Where(p => !text.Contains($"{p.Key} {p.Value.Version}")).Select(p => $"{p.Key} {p.Value.Version}").ToList();
    if (missing.Count == 0) { Console.WriteLine($"고지 최신 — 패키지 {packages.Count}개 ({string.Join("·", depsFiles.Select(f => f.Label))})"); return 0; }
    Console.Error.WriteLine("THIRD-PARTY-NOTICES.txt 에 없는 패키지: " + string.Join(", ", missing));
    Console.Error.WriteLine("bash scripts/publish.sh all 뒤 dotnet run scripts/Notices/Notices.cs 로 다시 만드세요.");
    return 1;
}

var pkgs = new List<Pkg>();
foreach (var (id, (ver, where)) in packages)
{
    var dir = Path.Combine(nuget, id.ToLowerInvariant(), ver);
    var spec = Directory.GetFiles(dir, "*.nuspec").FirstOrDefault();
    if (spec == null) { Console.Error.WriteLine($"없음: {id} {ver}"); return 1; }
    var x = XDocument.Load(spec);
    string El(string n) => x.Descendants().FirstOrDefault(e => e.Name.LocalName == n)?.Value.Trim() ?? "";
    var lic = x.Descendants().FirstOrDefault(e => e.Name.LocalName == "license");
    var license = lic?.Attribute("type")?.Value == "expression" ? lic.Value.Trim() : lic != null ? "라이선스 파일 (아래 전문)" : El("licenseUrl");
    var licFile = lic?.Attribute("type")?.Value == "file" ? Path.Combine(dir, lic.Value.Trim()) : null;
    var url = Regex.Replace(El("projectUrl"), @"\?utm_[^ ]*$", "");
    var extra = Directory.GetFiles(dir).Where(p => Regex.IsMatch(Path.GetFileName(p), @"^third-?party-?notices", RegexOptions.IgnoreCase)).ToList();
    var only = where.Count == depsFiles.Count ? "" : $" ({string.Join("·", where)})";
    pkgs.Add(new Pkg(id, ver, only, license, El("copyright"), url, licFile, extra));
}
if (runtimeDir == null || !File.Exists(Path.Combine(runtimeDir, "THIRD-PARTY-NOTICES.TXT")))
{
    Console.Error.WriteLine(".NET 런타임 팩을 찾지 못했습니다 — bash scripts/publish.sh all 을 먼저 하세요.");
    return 1;
}

static string Norm(string s) => s.Replace("\r\n", "\n").Replace("\r", "\n").TrimEnd();
string Line(char c = '=') => new string(c, 78);
var sb = new StringBuilder();
sb.AppendLine("ExcelToSqlite — 함께 들어 있는 오픈소스 소프트웨어 고지");
sb.AppendLine("THIRD-PARTY SOFTWARE NOTICES");
sb.AppendLine();
sb.AppendLine("이 앱(윈도 ExcelToSqlite.exe, 맥 ExcelToSqlite.app)에는 아래 오픈소스 소프트웨어가 들어 있습니다.");
sb.AppendLine("각 라이선스에 따라 저작권 표시와 라이선스 문구를 함께 나눠 드립니다.");
sb.AppendLine("This application includes the open-source software listed below. Their copyright notices and license texts follow.");
sb.AppendLine("(scripts/Notices/Notices.cs 가 만든 파일입니다 — 손으로 고치지 마세요)");
sb.AppendLine();
sb.AppendLine(Line());
sb.AppendLine("목록 / Components   ((윈도)·(맥) = 그 플랫폼 배포 파일에만 들어 있음)");
sb.AppendLine(Line());
foreach (var g in pkgs.GroupBy(p => (p.License, p.Copyright, p.Url)))
{
    sb.AppendLine($"- {string.Join(", ", g.Select(p => $"{p.Id} {p.Version}{p.Only}"))}");
    sb.AppendLine($"    License: {g.Key.License} · {g.Key.Copyright}{(g.Key.Url.Length > 0 ? " · " + g.Key.Url : "")}");
}
sb.AppendLine("- Inter 글꼴 3.19 (Avalonia.Fonts.Inter 안에 들어 있음)");
sb.AppendLine("    License: SIL Open Font License 1.1 · Copyright (c) 2016-2020 The Inter Project Authors · https://github.com/rsms/inter");
sb.AppendLine("- SQLite (SQLitePCLRaw.lib.e_sqlite3 의 e_sqlite3 안에 들어 있음)");
sb.AppendLine("    License: Public Domain · https://www.sqlite.org/copyright.html");
sb.AppendLine("- .NET 10 런타임 (앱에 함께 들어 있음 / self-contained)");
sb.AppendLine("    License: MIT · Copyright (c) .NET Foundation and Contributors · https://github.com/dotnet/runtime");
sb.AppendLine("- SkiaSharp·HarfBuzzSharp 네이티브 라이브러리에 든 Skia, HarfBuzz, libpng, zlib, FreeType, expat, libjpeg-turbo, libwebp 등");
sb.AppendLine("    License: 각 고지 참고 (BSD-3-Clause, MIT, zlib 등) — 아래 \"… — third-party notices\"");
sb.AppendLine();

void Section(string title, string text)
{
    sb.AppendLine(Line());
    sb.AppendLine(title);
    sb.AppendLine(Line());
    sb.AppendLine(Norm(text));
    sb.AppendLine();
}

var mit = new StringBuilder("다음 구성 요소에 적용됩니다 / Applies to:\n");
foreach (var g in pkgs.Where(p => p.License == "MIT").GroupBy(p => p.Copyright))
    mit.AppendLine($"  {string.Join(", ", g.Select(p => p.Id))} — {g.Key}");
mit.AppendLine("  .NET 10 런타임 — Copyright (c) .NET Foundation and Contributors");
mit.AppendLine("""

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
""");
Section("MIT License", mit.ToString());

var apache = pkgs.Where(p => p.License == "Apache-2.0").ToList();
if (apache.Count > 0)
    Section("Apache License 2.0",
        $"다음 구성 요소에 적용됩니다 / Applies to:\n" +
        string.Join("\n", apache.GroupBy(p => p.Copyright).Select(g => $"  {string.Join(", ", g.Select(p => p.Id))} — {g.Key}")) +
        "\n\n" + File.ReadAllText(Path.Combine(here, "Apache-2.0.txt")));

Section("Inter 글꼴 3.19 — SIL Open Font License 1.1", File.ReadAllText(Path.Combine(here, "Inter-OFL-1.1.txt")));

foreach (var p in pkgs.Where(p => p.LicenseFile != null && File.Exists(p.LicenseFile)))
    Section($"{p.Id} {p.Version}{p.Only} — {p.Copyright}", File.ReadAllText(p.LicenseFile!));

// 패키지 안의 제3자 고지: 같은 내용(SkiaSharp·HarfBuzzSharp 네이티브 패키지들이 같은 파일)은 한 번만
foreach (var g in pkgs.SelectMany(p => p.Extra.Select(e => (p, Text: Norm(File.ReadAllText(e))))).GroupBy(x => x.Text))
    Section($"{string.Join(", ", g.Select(x => x.p.Id))} — third-party notices", g.Key);

Section(".NET 10 런타임 — LICENSE", File.ReadAllText(Path.Combine(runtimeDir, "LICENSE.TXT")));
Section(".NET 10 런타임 — THIRD-PARTY-NOTICES", File.ReadAllText(Path.Combine(runtimeDir, "THIRD-PARTY-NOTICES.TXT")));

File.WriteAllText(outPath, sb.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
Console.WriteLine($"THIRD-PARTY-NOTICES.txt — 패키지 {pkgs.Count}개, {new FileInfo(outPath).Length / 1024}KB");
return 0;

record Pkg(string Id, string Version, string Only, string License, string Copyright, string Url, string? LicenseFile, List<string> Extra);
