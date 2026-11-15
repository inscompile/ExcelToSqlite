# 인수인계 (2026-08-17, 2026-11-15 갱신)

> **2026-11-15 갱신** — 배포 준비. **.NET 10** 전환, 윈도 exe 한 파일·맥 .app zip 배포 스크립트,
> GitHub Actions, 아이콘, 버전. 화면 앱 프로젝트 이름을 `ExcelToSqlite.Desktop` 으로 바꿈.
> 자세한 내용은 아래 "빌드 이력" 7번.
>
> **2026-11-15** — 공개 저장소 `inscompile/ExcelToSqlite` 로 옮김. 이전 개발 기록은 넘기지 않고 커밋 하나로 새로 시작했다.

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

## 현재 상태

**.NET 10 빌드 통과** (Release 오류 0개, 경고 0개). 배포 파일 확인 (2026-11-15, 이 맥):
- 맥 `.app`: zip을 풀어 `open`으로 열면 12초 이상 유지, 서명 검사 통과
- 윈도 `ExcelToSqlite.exe`: 파일 하나(약 50MB), 아이콘 포함
- 명령줄 한 파일: CP949 텍스트의 `0100`·`똠햏` 이 그대로 들어감
- **GitHub Actions 첫 실행 통과** (2026-11-15, 공개 전 비공개 저장소에서, 2분 15초): 윈도·맥 모두 빌드 → 배포 파일 →
  CP949 적재 확인 → 화면 앱 15초 유지. 윈도 exe가 실제 윈도에서 켜지는 것은 이것으로 확인됐다

## 빌드 이력 (2026-08-17, 첫 빌드에서 고친 것)

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
  PK 개수 입력란은 스모크 테스트 때 값이 실제로 반영되는지 눈으로 확인할 것

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

## 스모크 테스트 (2026-08-17, 실제 업무용 통합 문서 35MB — 저장소에는 넣지 않음)

- `--list-names`: 명명범위 **136개**(사용 가능 134 + 단일 셀 2), 시트 **53개**. `#REF!` 깨진 이름 0개
- **명명범위 134개 전부 적재 성공 — 595,377행 / 135테이블, 6.0초**
- 시트 전체 적재(`--sheet-of`)도 정상(8,940행). 단 첫 컬럼에 빈 값이 있으면 PK NOT NULL에
  걸리므로 `--keys 0` 으로 넣는다 (의도된 동작)
- 전 컬럼 TEXT 적재 확인. 선행 0(`"0100"`)·19자리 숫자 코드가 원문 그대로 남는다

## 다음에 할 일

1. **GUI 스모크 테스트.** 창 제목 버전(`1.0.0`)·아이콘·드래그&드롭(새 API)은 사용자가 Rider에서 확인함 (2026-11-15).
   남은 것: PK 개수·인덱스 컬럼·컬럼 자동 추가 체크박스 (바인딩 오류는 런타임에만 드러남),
   DB 칸에 이름만 넣고 실행해 첫 번째 원본 파일 폴더에 생기는지
2. Releases에 올릴 때: `Directory.Build.props` 의 `<Version>` 과 같은 태그(`v1.0.0`)를 푸시 (푸시·태그는 사용자 확인)
2-1. **공개 전에** (2026-11-15 결정):
   - 오픈소스 고지 `THIRD-PARTY-NOTICES.txt` — 배포 파일에 실제로 들어가는 패키지(deps.json 기준)의 라이선스·저작권,
     SkiaSharp·HarfBuzzSharp 패키지 안의 고지(같은 내용이라 한 번만), SQLitePCLRaw(Apache-2.0 전문),
     ANGLE(윈도만, BSD), .NET 10 런타임의 LICENSE·THIRD-PARTY-NOTICES, 그리고 **Inter 글꼴(SIL OFL 1.1)** —
     `Avalonia.Fonts.Inter` 패키지는 MIT지만 안의 글꼴은 OFL이고 DLL에 라이선스 글이 없으므로 직접 넣는다
     ("Copyright (c) 2016 The Inter Project Authors (https://github.com/rsms/inter)" + OFL 1.1 전문).
     디버그·빌드·아이콘 생성기 전용 패키지는 뺀다. 저장소 맨 위에 두고, 맥 `.app/Contents/Resources` 와
     Releases에도 함께 올리고(윈도 exe는 한 파일이라 옆에 못 둠), README "라이선스"에 적는다.
     `.gitignore` 에는 이미 `!/THIRD-PARTY-NOTICES.txt` 가 있다
   - 맥 번들 ID `local.exceltosqlite.desktop` → `io.github.inscompile.exceltosqlite` (`scripts/publish.sh`). 한 번 내면 바꾸지 않는다
   - Releases에는 윈도 exe와 맥(Apple 칩) zip만 올린다. Intel 맥용·명령줄 `xl2sqlite` 는 올리지 않는다
3. 시험 프로젝트: 작은 xlsx(시험 코드가 직접 만들기, `.gitignore` 가 `*.xlsx` 를 막음)·CSV·CP949 텍스트 →
   앞자리 0, 19자리 코드, 1900 날짜 윤년 보정, 이름 하나 걸러 빠지던 버그(4번), 인코딩 이름(7번)
4. 개선 검토 대상: 적재 전 미리보기 그리드, 프로파일 저장/불러오기, 영어 화면

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
- 맥 최소 버전 12.0 (.NET 10 런타임의 하한). Apple 칩용만 만든다 — Intel은 `MAC_RID=osx-x64`
- 원인 불명의 실행 오류가 나면 빌드 산출물을 다른 로컬 경로로 복사해 실행해 보면
  가려져 있던 진짜 오류 메시지가 나오는 경우가 있다

## 참고

- 상세 사용법·규칙: `README.md`
- 배치 설정 예시: `sample/sources.json`
