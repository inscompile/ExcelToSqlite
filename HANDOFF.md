# 인수인계 — ExcelToSqlite (2026-11-15 기준)

> 이 문서만 읽고 이어서 일할 수 있게 쓴다. 사용법·동작 규칙·배포 방법은 [README.md](README.md),
> 커밋·공개 저장소 규칙은 [CLAUDE.md](CLAUDE.md). 고친 내용은 이 문서와 README에 함께 적는다.

## 새 세션은 여기부터

- **무엇**: 엑셀 명명범위·텍스트 파일을 SQLite `.db` 로 적재하는 화면 앱(`ExcelToSqlite`)과 명령줄 도구(`xl2sqlite`). C# · .NET 10 · Avalonia 11.3
- **저장소**: GitHub `inscompile/ExcelToSqlite`. 2026-11-15에 지금 상태를 커밋 하나로 새로 시작했다 (예전 개발 기록은 넘기지 않음).
  공개로 바꾸기 전이다. 버전 `1.0.0`(`Directory.Build.props`), **태그는 아직 없다**
- **지금 상태**: 배포 준비 코드 일은 끝났다 — .NET 10, 윈도 exe 한 파일, 맥 `.app` zip, Actions(윈도·맥 빌드·실행 확인, 태그면 Releases),
  아이콘, 버전, 화면 확인, 오픈소스 고지 `THIRD-PARTY-NOTICES.txt`, 맥 번들 ID `io.github.inscompile.exceltosqlite`.
  빌드 경고 0개. 아래 "확인한 것·못 한 것"
- **바로 다음** (푸시·태그·공개 전환은 매번 사용자 확인 — 관리 세션이 물어보고 한다):
  1. 푸시 → Actions 통과 확인 (이번에 "오픈소스 고지가 최신인지" 단계가 새로 들어감)
  2. 태그 `v1.0.0` → Releases에 `ExcelToSqlite.exe`·`ExcelToSqlite-mac.zip`·`THIRD-PARTY-NOTICES.txt` 세 파일이 붙었는지
  3. 공개로 바꾸기. 그 뒤의 코드 일은 아래 "다음에 할 일"
- **확인 명령**:
  ```bash
  dotnet build -c Release                       # 경고 0개여야 함
  bash scripts/publish.sh all                   # dist/ 에 맥 .app·zip, 윈도 exe
  dotnet run scripts/Notices/Notices.cs -- --check   # 고지가 배포 파일과 맞는지
  ```

## 무엇을 만들고 있나

엑셀 명명범위와 텍스트 파일을 **SQLite `.db`** 로 적재하는 크로스플랫폼(Windows/macOS) 도구.
요율표처럼 엑셀에 있는 표 데이터를 SQL로 조회하기 위한 **적재 전용** 프로그램이다.

- `src/ExcelToSqlite.Core` — UI 의존성 없는 라이브러리. 로직 전부 여기 있음
- `src/ExcelToSqlite.Desktop` — Avalonia 11 GUI (드래그&드롭). 실행 파일 이름은 `ExcelToSqlite`
- `src/ExcelToSqlite.Cli` — 콘솔. 배치/스케줄러용

## 확정된 설계 결정 (되돌리지 말 것)

| 항목 | 결정 | 이유 |
|---|---|---|
| 타깃 프레임워크 | **net10.0** (`Directory.Build.props`, SDK는 `global.json`) | .NET 9 지원 종료(2026-11-10). 10은 LTS, 2028-11까지 |
| 배포 형태 | 윈도 exe **한 파일**, 맥 **.app zip**, 둘 다 런타임 포함 | 받는 쪽에 .NET 불필요. `.app`은 원래 폴더라 묶지 않음(빨리 열림) |
| 버전 | `Directory.Build.props` 의 `<Version>` 한 곳 | 태그 `v<Version>` 과 다르면 Actions가 Releases를 막음 |
| Excel 읽기 | **자체 SAX 스트리밍 리더** (Interop·ClosedXML 금지) | macOS 지원, Excel 설치 불필요, 대용량 속도 |
| SQLite | `Microsoft.Data.Sqlite` (네이티브 엔진 내장) | ODBC 드라이버·sqlite3 CLI 불필요 |
| 기본키 | `KeyColumnCount`만큼 **선두 컬럼을 복합 PK로 선언** | 값 결합(`c1\|c2`) 방식은 폐기됨 |
| PK NOT NULL | 명시적으로 부여 | SQLite는 PK 컬럼에도 NULL 허용 |
| 테이블 단위 | 엑셀=명명범위별, 텍스트=파일별 | 테이블명 = 이름 / 파일명 |
| 인코딩 | BOM → 엄격 UTF-8 → CP949 자동 판별. 지정 이름 `cp949`·`euc-kr` 은 949로 | 국내 업무 시스템에서 뽑은 텍스트 파일은 흔히 EUC-KR. .NET의 `euc-kr`(51949)은 확장 한글을 깨뜨림 |
| 값 타입 | **전 컬럼 TEXT.** 타입 추론 없음 | 원본 보존 우선. 해석은 조회 시 `CAST` 로 |
| 대상 DB 확장자 | 없으면 `.db` 자동 부착 | 확장자 없는 파일이 만들어지는 것을 막음 |
| 대상 DB 상대 경로 (GUI) | 첫 번째 원본 파일 폴더 기준 | 배포 앱은 현재 폴더가 맥 `/`, 윈도 exe 폴더 |
| 컬럼 자동 추가 | **기본 꺼짐**, 체크박스로만 켬 | 엉뚱한 테이블에 대고 실행했을 때 스키마가 조용히 바뀌는 것을 막음 |
| 저널 모드 | 적재 중 WAL → 종료 시 `DELETE` 복귀 | 속도는 얻고 `-wal`/`-shm` 잔여 파일은 남기지 않음 |
| 인덱스 | 소스별 `IndexColumns` 문자열, 콤마 구분 = 복합 인덱스 1개 | PK로 못 잡는 조회 컬럼 대응. UNIQUE는 지원하지 않음 |
| 단일 셀 이름 | 적재 대상에서 **제외** | 셀 하나짜리 이름은 테이블이 아니라 스칼라 파라미터 |
| 조회 기능 | **넣지 않음** | 역할 분리. DB Browser/DataGrip으로 조회 |
| UI 상태 | 종료 시 저장 후 재실행 시 복원 + 초기화 버튼 | |
| Releases 파일 | 윈도 exe, 맥(Apple 칩) zip, `THIRD-PARTY-NOTICES.txt` 셋만 | Intel 맥용·명령줄 `xl2sqlite` 는 올리지 않는다 (2026-11-15 결정) |
| 맥 번들 ID | `io.github.inscompile.exceltosqlite` | 조직 주소를 거꾸로. 맥이 설정·권한을 이 ID로 기억하므로 **한 번 내면 바꾸지 않는다** |
| 오픈소스 고지 | `scripts/Notices/Notices.cs` 가 배포 파일의 deps.json으로 만든다. 손으로 고치지 않는다 | 실제로 들어가는 것만 (디버그·빌드·아이콘 생성기 전용 패키지 제외) |

## 확인한 것·못 한 것 (2026-11-15)

**확인한 것**
- 빌드: Release 오류 0개, 경고 0개. 빈 폴더에 새로 받아 빌드해도 된다
- 맥 `.app`: zip을 풀어 `open` 으로 열면 12초 이상 유지, `codesign --verify --deep --strict` 통과, 번들 ID·아이콘·고지 파일 들어 있음
- 윈도 `ExcelToSqlite.exe`: 파일 하나(약 50MB), 아이콘 포함. **실제 윈도에서 켜지는 것은 Actions(windows-latest)로 확인** —
  공개 전 리허설 저장소에서 main 푸시와 태그 `v1.0.0` 둘 다 통과했고, 태그 때 Releases에 exe·zip이 붙었다
- 명령줄 한 파일: CP949 텍스트의 `0100`·`똠햏` 이 그대로 들어감 (Actions에서도 윈도·맥 둘 다)
- 화면 (사람이 Rider에서): 창 제목 버전 `1.0.0`, 아이콘, 드래그 앤 드롭
- 화면 (헤드리스 자동 확인 — 아래 "빌드 이력" 8번): 기본 PK 개수 칸·전체 적용, 표의 PK수·인덱스 컬럼·테이블명 칸 편집,
  `컬럼 자동 추가` 체크박스(교체 모드에서 꺼짐, 신규 INSERT에서 켜짐), DB 칸에 이름만 넣고 실행 → 첫 번째 원본 파일 폴더에 생김.
  만든 DB에서 PK·인덱스·중간에 끼어든 컬럼(맨 뒤에 붙고 기존 행은 빈 값)까지 확인. 바인딩 오류·경고 0건
- 오픈소스 고지: 윈도·맥 배포 파일의 패키지 27개 + Inter 글꼴 + SQLite + .NET 런타임. `--check` 가 일부러 틀린 버전을 잡는 것 확인

**못 한 것**
- 이번에 고친 것(고지 확인 단계, Releases에 고지 파일 붙이기, 번들 ID)은 **아직 Actions에서 돌지 않았다** (푸시 전)
- 고지가 들어간 새 Releases는 아직 없다 (태그 전)
- 시험 프로젝트 없음 (아래 "다음에 할 일" 2)
- 영어 화면 없음

## 빌드 이력 (고친 것과 그 이유)

### 1. `ExcelToSqlite.App.app이 손상되었습니다` — 빌드 산출물 위치 (가장 중요)

처음에는 프로젝트를 Dropbox 동기화 폴더(`~/Library/CloudStorage/Dropbox/…`) 안에 두었는데,
그 경로에서 GUI 실행 파일을 실행하면 macOS가 **로그도 크래시 리포트도 없이
SIGKILL(137)** 했다. Rider는 이걸 `ExcelToSqlite.App.app이 손상되었습니다`로 표시한다.
(당시 `src/ExcelToSqlite.App` 폴더가 `.App` 확장자 탓에 macOS에서 앱 번들로 인식되어 이 이름이 붙었다 — 지금은 `ExcelToSqlite.Desktop`.
확인: `mdls -name kMDItemContentType src/ExcelToSqlite.App` → `com.apple.application-bundle`)

동일 소스·동일 런타임으로 위치만 바꿔 검증한 결과:

| 실행 위치 | 결과 |
|---|---|
| Dropbox 폴더 안 | **exit 137 (SIGKILL)** |
| Dropbox 밖 로컬 경로 | 정상 (죽지 않음) |

서명 문제가 아니다 — `codesign -v`는 valid이고 강제 재서명해도 137이 그대로다.
폴더명이 `.App`인 것도 원인이 아니다 — 로컬에 일부러 `fake.App/` 조상을 만들어도 정상 실행된다.

**해결: 프로젝트 전체를 동기화되지 않는 로컬 폴더로 옮겼다.** 산출물은 표준대로 각 프로젝트의 `bin/`에 생성된다.

> **다시 Dropbox·iCloud 등 동기화 폴더 안으로 옮기지 말 것.** 증상이 그대로 재발한다.
> 부득이 동기화 폴더에 둬야 한다면 루트에 `Directory.Build.props`를 만들어
> `<BaseOutputPath>$(HOME)/.dotnet-build/...</BaseOutputPath>` 로 **산출물만** 밖으로 빼면 된다
> (검증 완료). 단 `BaseIntermediateOutputPath`(obj)까지 옮기면 복원 단계에서 빌드가 깨진다.
>
> 백업이 필요하면 Git 저장소를 만들어 원격에 푸시하는 편이 낫다.

### 2. 타깃 프레임워크 net8.0 → net9.0 (7번에서 다시 net10.0)

위 SIGKILL 때문에 가려져 있던 별개의 문제. 산출물을 동기화 폴더 밖으로 복사해 실행하자
비로소 진짜 메시지가 드러났다:

```
You must install or update .NET to run this application.
Framework: 'Microsoft.NETCore.App', version '8.0.0' (arm64)
The following frameworks were found: 9.0.4
```

당시 개발 맥에는 SDK 9.0.203 / 런타임 9.0.4만 있고 **.NET 8 런타임이 없었다.**
빌드는 SDK 9가 해주지만 실행 시점에 8 런타임을 찾다 실패한다.
`<RollForward>LatestMajor</RollForward>`로 우회 가능하나 우회책이므로 폐기하고,
3개 프로젝트 전부 `net9.0`으로 리타깃했다. `Microsoft.Data.Sqlite`와
`System.Text.Encoding.CodePages`도 런타임에 맞춰 9.0.4로 올렸다.

> 배포 대상 PC에 .NET 9가 없으면 같은 증상이 재발한다.
> README의 `--self-contained -p:PublishSingleFile=true` 배포를 쓰면 런타임이 포함되어 문제없다.

### 3. Avalonia 11.2.3 → **11.3.0**

`MainWindow.axaml`의 `Grid.RowSpacing` / `ColumnSpacing`이 **Avalonia 11.3부터** 추가된
속성이라 5건의 `AVLN2000` 오류 발생. 패키지 6개를 11.3.0으로 일괄 상향해 해결.
(`StackPanel.Spacing`은 구버전에도 있어 무관했다.)

### 4. Excel 리더 교체 — ClosedXML → 자체 SAX 스트리밍

ClosedXML은 부분 로드를 지원하지 않아 이름 하나만 필요해도 통합문서 전체를 객체 모델로 올린다.
대상 파일은 압축 35MB지만 **압축 해제 340MB, 시트 53개**(가장 큰 시트 하나가 113MB / 셀 320만 개)라
**워크북 여는 데만 11초**가 걸렸다. 측정으로 병목을 특정했다 — 이름 1개 적재 11.6초,
이름 4개(행 8배) 적재 11.1초로 **읽는 양과 시간이 무관**했다. 셀 접근이 아니라 파싱이 원인.

`XlsxPackage`(zip + 파트별 스트리밍)와 `RefParser`를 직접 구현해 교체했다.
외부 패키지는 쓰지 않는다 — `System.IO.Compression` + `XmlReader` 만으로 충분하다.

- 필요한 시트 파트만 연다. 영역 밖 행은 XML 서브트리째 `Skip`, 마지막 행을 지나면 즉시 중단
- `sharedStrings` / `styles` 는 실제로 쓸 때만 로드 → 이름 목록만 필요하면 아예 안 읽는다
- 숫자는 XML 원문을 쓰되 지수 표기(`9.7000000000000003E-2`)만 최단 왕복 표기(`0.097`)로 정규화.
  정수 리터럴은 손대지 않아 19자리 코드값도 그대로 남는다

| 작업 | ClosedXML | 스트리밍 |
|---|---|---|
| `--list-names` | 11.5초 | **0.04초** |
| 이름 3개 적재 | 11.0초 | **0.5초** |
| 이름 134개 전부(595,377행) | — | **6.0초** |

동일 결과 검증: 이름·시트 목록이 이전과 완전히 일치(134/2/53), 적재 데이터도 세 테이블
20,287행 전수 비교에서 바이트 단위 일치.

**구현 중 실제로 잡은 버그 두 개** (재발 주의):
- `XmlReader.ReadElementContentAsString()` 은 리더를 다음 노드로 옮긴다. 루프에서 `Read()`를
  또 부르면 **이름이 하나 걸러 누락**된다(136 → 68로 반토막 났었다)
- 1900 날짜 체계의 윤년 버그 보정. 시리얼 60 미만은 기준일 `1899-12-31`,
  61 이상은 `1899-12-30` 을 쓴다. 한 기준일로 통일하면 **날짜가 하루씩 밀린다**

### 5. 값 타입 일괄 TEXT

타입 추론(`ColumnTyper`)과 `--all-text` / "타입 자동 추론" 옵션을 전부 제거했다.
모든 컬럼을 `TEXT` 로 선언하고 원본 문자열을 그대로 넣는다. 숫자·날짜 해석은 조회 시 `CAST` 로 한다.
엑셀 **표시 서식이 아니라 내부 값**을 쓴다 — 요율이 `15%` 로 보여도 `0.15` 로 적재된다.
이 변경으로 담보코드 손상 위험(선행 0, 15자리 초과)이 구조적으로 사라졌다.

### 6. 기우로 끝난 것

- `NumericUpDown.Value`(`decimal?`) 바인딩 — 경고조차 없었음.
  단 `AvaloniaUseCompiledBindingsByDefault=false`라 **바인딩 오류는 컴파일이 아니라 런타임에만 드러난다.**
  PK 개수 입력란은 2026-11-15 헤드리스 확인(8번)에서 값이 실제로 반영되는 것을 확인했다

### 7. .NET 10 전환과 배포 준비 (2026-11-15)

- 3개 프로젝트 `net10.0`. 공통 설정은 `Directory.Build.props`, SDK는 `global.json`(10.0.100, latestFeature).
  개발 맥에는 SDK 9.0.203과 10.0.403이 함께 있다
- Avalonia 11.3.0 → 11.3.23. **`Avalonia.Controls.DataGrid` 는 번호가 따로**라 11.3.23이 없어 복원이 실패한다
  (NU1605). 11.3 줄의 마지막인 11.3.13을 쓴다
- `System.Text.Encoding.CodePages` 패키지 삭제 — .NET 10에 들어 있어 NU1510 경고가 난다
- 11.3.23에서 드래그 앤 드롭 옛 API(`e.Data`, `DataFormats.Files`)가 구식 경고 → `e.DataTransfer`, `DataFormat.File`, `TryGetFiles()`
- `CA2255` 해결: CP949 등록을 `[ModuleInitializer]` 에서 `DelimitedTextReader` 정적 생성자로 옮김
- 배포: `dotnet publish -r <RID>` 이면 런타임 포함(csproj). 윈도·명령줄은 한 파일, 맥은 `scripts/publish.sh` 가 `.app` 으로 묶고
  임시 서명 후 zip. Release는 pdb를 dll 안에 넣어(`DebugType=embedded`) 배포 폴더에 `.pdb` 가 따로 생기지 않는다
- `.github/workflows/build.yml`: 윈도·맥에서 빌드 → 배포 파일 → 명령줄로 CP949 적재 확인 → 화면 앱 15초 → 태그면 Releases
- 아이콘: `scripts/IconGen`(SkiaSharp)으로 `Assets/AppIcon.png·ico·icns` 생성. 윈도 exe·창 아이콘, 맥 `.app`
- 화면 앱 프로젝트 이름 `ExcelToSqlite.App` → `ExcelToSqlite.Desktop` (Finder가 `.App` 폴더를 앱으로 봄)

**이때 잡은 버그 두 개** (재발 주의):
- `--encoding cp949` 가 실패했다 — .NET 이름표에 `cp949` 가 없다. `euc-kr` 은 51949로 풀려 **`똠햏` 이 `c?` 로 깨져 들어갔다.**
  둘 다 949로 읽도록 고침. 자동 판별은 원래 949라 문제없었다
- GUI DB 칸에 이름만 넣으면 현재 폴더 기준이었다. Rider는 bin에서 실행해 드러나지 않았지만, 배포 앱은 맥 `/`(저장 실패),
  윈도 exe 폴더에 생긴다. 첫 번째 원본 파일 폴더 기준으로 고침

> csproj 같은 XML 주석 안에는 `--` 를 쓸 수 없다 (`dotnet run --project …` 를 적었다가 MSB4025).

### 8. 화면 확인·오픈소스 고지·번들 ID (2026-11-15, 공개 저장소로 옮긴 뒤)

**화면 확인 (헤드리스)** — 사람이 누르지 않고 `Avalonia.Headless` 11.3.23으로 실제 `MainWindow` 를 띄워 확인했다.
시험 코드는 저장소에 넣지 않았다 (시험 프로젝트를 만들 때 옮겨 담을 것). 다시 할 때 알아 둘 것:
- 표(DataGrid) 칸은 사람처럼 편집해야 확인이 된다: `grid.SelectedItem`·`CurrentColumn` → `BeginEdit()` → 칸의 `TextBox` 에
  `SelectAll()` 후 `window.KeyTextInput("…")` → `KeyPress(Key.Enter …)`. `Avalonia.Logging.Logger.Sink` 를 갈아 끼워 경고 이상을 모으면 바인딩 오류가 보인다
- **`MainWindowViewModel` 생성자는 사용자의 실제 화면 상태 파일(`ui-state.json`)을 읽는다.** 맥의 .NET은 설정 폴더를
  정할 때 `HOME` 을 보지 않아 시험용으로 돌릴 수 없다. 저장은 `App` 의 데스크톱 수명 주기(`ShutdownRequested`·창 `Closing`)에서만
  하므로 헤드리스에서는 쓰지 않는다 — 불러온 내용은 메모리에서만 비울 것. **`ResetCommand` 는 그 파일을 지우므로 시험에서 부르지 말 것**

**오픈소스 고지** — `scripts/Notices/Notices.cs` (CodeFlowTracer 의 같은 도구를 본뜸, .NET 10 파일 하나짜리 프로그램)
- 입력: 윈도 `src/ExcelToSqlite.Desktop/obj/Release/net10.0/win-x64/ExcelToSqlite.deps.json`(한 파일 exe 안에 묶이므로 obj 사본),
  맥 `dist/ExcelToSqlite.app/Contents/MacOS/ExcelToSqlite.deps.json`. 그래서 `bash scripts/publish.sh all` 을 먼저 한다.
  플랫폼별 deps.json이라 리눅스용 네이티브 패키지나 디버그 전용 `Avalonia.Diagnostics` 는 들어가지 않는다. 한쪽에만 있는 패키지는 `(윈도)`·`(맥)` 표시
- 패키지에 라이선스 글이 없는 두 가지는 `scripts/Notices/` 의 원문을 넣는다: `Apache-2.0.txt`(apache.org, SQLitePCLRaw 용),
  `Inter-OFL-1.1.txt`(rsms/inter `v3.19` 의 LICENSE.txt)
- **Inter 글꼴은 3.19판이다.** `Avalonia.Fonts.Inter` 11.3.23 DLL 안 글꼴의 name 테이블이 `Version 3.019`,
  `Copyright 2020 The Inter Project Authors` 다. 최신판(4.x) 저장소의 문구(`Copyright (c) 2016 …`)가 아니라 3.19판 라이선스 파일
  (`Copyright (c) 2016-2020 The Inter Project Authors.`)을 쓴다. Avalonia를 올리면 글꼴 판도 다시 볼 것
- .NET 런타임 고지는 윈도·맥 팩이 줄바꿈만 다르고 같다 → 하나만 넣는다. 패치 번호는 빌드하는 컴퓨터마다 달라서 "10.0" 까지만 적는다
- `--check`: 배포 파일에 든 패키지·버전이 고지에 다 있는지만 본다 (런타임 고지는 보지 않음). Actions 윈도·맥 작업에서 돈다
- 두는 곳: 저장소 맨 위, 맥 `.app/Contents/Resources`(`publish.sh` 가 서명 전에 복사), Releases에 따로 한 파일 (윈도 exe는 한 파일이라 옆에 못 둠)

**번들 ID** `local.exceltosqlite.desktop` → `io.github.inscompile.exceltosqlite` (`scripts/publish.sh`). 고친 뒤 `.app` 이 열리는 것 확인

## 스모크 테스트 (2026-08-17, 실제 업무용 통합 문서 35MB — 저장소에는 넣지 않음)

- `--list-names`: 명명범위 **136개**(사용 가능 134 + 단일 셀 2), 시트 **53개**. `#REF!` 깨진 이름 0개
- **명명범위 134개 전부 적재 성공 — 595,377행 / 135테이블, 6.0초**
- 시트 전체 적재(`--sheet-of`)도 정상(8,940행). 단 첫 컬럼에 빈 값이 있으면 PK NOT NULL에
  걸리므로 `--keys 0` 으로 넣는다 (의도된 동작)
- 전 컬럼 TEXT 적재 확인. 선행 0(`"0100"`)·19자리 숫자 코드가 원문 그대로 남는다

## 다음에 할 일

1. **공개까지** (위 "새 세션은 여기부터"의 바로 다음 1~3): 푸시 → Actions 통과 → 태그 `v1.0.0` → Releases 세 파일 → 공개.
   푸시·태그·공개 전환은 매번 사용자 확인
2. **시험 프로젝트** (`tests/` 같은 새 루트 폴더는 `.gitignore` 에 `!/tests` 를 더해야 들어간다):
   - Core: 작은 xlsx(시험 코드가 직접 만들기 — `.gitignore` 가 `*.xlsx` 를 막음)·CSV·CP949 텍스트 → 앞자리 0, 19자리 코드,
     1900 날짜 윤년 보정, 이름 하나 걸러 빠지던 버그(4번), 인코딩 이름 `cp949`·`euc-kr`(7번), 상대 DB 경로(7번), 컬럼 자동 추가
   - 화면: 8번의 헤드리스 확인을 옮겨 담기. 그 전에 화면 상태 파일 위치를 시험에서 바꿀 수 있게 하면 좋다
     (지금은 `UiStateStore.FilePath` 가 사용자 설정 폴더로 고정)
   - Actions의 "빌드" 다음에 `dotnet test` 단계를 넣는다
3. 개선 검토: 적재 전 미리보기 그리드, 프로파일 저장/불러오기, 영어 화면, 앱 안 "정보" 창(버전·GitHub 주소·고지)
4. 패키지·버전을 바꾸면: `bash scripts/publish.sh all` → `dotnet run scripts/Notices/Notices.cs` → 고지도 함께 커밋.
   Avalonia를 올릴 때 `Avalonia.Controls.DataGrid` 는 번호가 따로이고(7번), Inter 글꼴 판이 바뀌었는지(8번) 볼 것

## 알려진 제약

- 구형 `.xls` 미지원 (`.xlsx`/`.xlsm`으로 저장 필요)
- `#REF!`로 깨진 명명범위는 해당 테이블만 실패로 보고됨
- 추가(append) 모드에서 기존 테이블과 컬럼 구성이 다르면 그 테이블만 실패 처리 (의도된 동작).
  `컬럼 자동 추가`(`--add-columns`)를 켜면 없는 컬럼을 붙여서 진행한다
- `ALTER TABLE ADD COLUMN` 은 SQLite 제약상 **항상 맨 뒤**에 붙는다. 소스에서 컬럼이 중간에
  끼어들어도 INSERT는 컬럼명으로 하므로 값은 제자리에 들어가고, 물리적 순서만 달라진다.
  물리 순서까지 맞추려면 테이블을 다시 만들어야 한다
- PK 컬럼은 `ALTER` 로 추가할 수 없다. 키 컬럼이 기존 테이블에 없으면 실패로 보고된다
- **프로젝트를 동기화 폴더(Dropbox/iCloud) 안에 두면 안 된다.** GUI가 SIGKILL(137)로 죽는다.
- 배포 파일은 서명하지 않는다(맥은 임시 서명). 처음 열 때 윈도 SmartScreen, 맥 `그래도 열기` 단계가 있다 (README)
- 맥 최소 버전 12.0 (.NET 10 런타임의 하한). Apple 칩용만 만든다 — Intel은 `MAC_RID=osx-x64` 로 만들 수 있지만 Releases에는 올리지 않는다
- 원인 불명의 실행 오류가 나면 빌드 산출물을 다른 로컬 경로로 복사해 실행해 보면
  가려져 있던 진짜 오류 메시지가 나오는 경우가 있다

## 참고

- 상세 사용법·규칙: `README.md`
- 배치 설정 예시: `sample/sources.json`
