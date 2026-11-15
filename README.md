# ExcelToSqlite (C# / .NET 10)

엑셀 **명명범위(Name)** 와 **복수의 텍스트 파일**을 읽어 하나의 **SQLite `.db`** 파일로 적재한다.
Windows / macOS(Intel·Apple Silicon) / Linux 공용이며, **Excel 설치나 ODBC 드라이버, sqlite3 CLI가 필요 없다.**

## 내려받기

| 컴퓨터 | 받을 파일 |
|---|---|
| 윈도 (64비트) | [**ExcelToSqlite.exe**](https://github.com/inscompile/ExcelToSqlite/releases/latest/download/ExcelToSqlite.exe) — 파일 하나, 설치 없이 바로 실행 |
| 맥 (Apple 칩, macOS 12 이상) | [**ExcelToSqlite-mac.zip**](https://github.com/inscompile/ExcelToSqlite/releases/latest/download/ExcelToSqlite-mac.zip) — 압축을 풀고 `ExcelToSqlite.app` 실행 |

- 처음 열 때 막히면 아래 [처음 열 때](#처음-열-때-서명하지-않은-앱)를 본다.
- 지난 버전과 바뀐 내용: [Releases](https://github.com/inscompile/ExcelToSqlite/releases). Releases 아래쪽의 "Source code"는 소스 코드라 실행 파일이 아니다.
- 인터넷에 연결하지 않는다. 고른 파일을 읽어 고른 `.db` 에 쓰는 일만 한다.

## 구조

```
src/ExcelToSqlite.Core/     UI 의존성 없는 라이브러리 (여기에 로직 전부)
  ImportModels.cs           SourceSpec / ImportOptions / 진행률 / 결과   ← UI 바인딩 대상
  TableData.cs              메모리상의 테이블
  Naming.cs                 식별자 정리·인용, 헤더 중복 제거
  ValueText.cs              셀 값 -> 문자열 (전 컬럼 TEXT 적재)
  ExcelSourceReader.cs      .xlsx/.xlsm 리더 (명명범위/시트 -> TableData)
  XlsxPackage.cs            xlsx zip 파트 SAX 스트리밍 (외부 패키지 없음)
  RefParser.cs              A1 참조 파싱, 날짜 서식 판정
  DelimitedTextReader.cs    TSV/CSV/파이프 리더 (인코딩·구분자 자동 감지)
  SqliteTableWriter.cs      CREATE TABLE + 트랜잭션 INSERT
  ImportService.cs          오케스트레이션 (Run / RunAsync / Preview)
  SourceExpander.cs         파일 1개 -> 테이블 N개 전개 (엑셀=이름별, 텍스트=파일별)
src/ExcelToSqlite.Desktop/  Avalonia 11 데스크톱 UI (드래그&드롭, 실행 파일 이름 ExcelToSqlite)
src/ExcelToSqlite.Cli/      콘솔 진입점 (xl2sqlite) - 배치/스케줄러용
sample/sources.json         배치 설정 예시
Directory.Build.props       공통 설정: .NET 버전, 앱 버전(<Version>), Avalonia 버전
global.json                 .NET 10 SDK 고정
scripts/publish.sh          배포 파일 만들기 (윈도 exe 한 파일, 맥 .app zip)
scripts/IconGen/            앱 아이콘 만들기 (-> src/ExcelToSqlite.Desktop/Assets)
.github/workflows/build.yml 윈도·맥 자동 빌드, 켜지는지 확인, 태그를 올리면 Releases
```

## 역할 분리

이 프로그램은 **적재 전용**이다. 생성된 `.db`는 DB Browser for SQLite, DBeaver, JetBrains DataGrip 등
기존 SQLite 뷰어로 열어서 조회·편집한다. 앱 안에 쿼리 화면을 두지 않는다.

## 빌드 · 실행

```bash
dotnet build -c Release

# GUI
dotnet run --project src/ExcelToSqlite.Desktop

# CLI
dotnet run --project src/ExcelToSqlite.Cli -- --help

# 엑셀에 정의된 이름 목록 확인
dotnet run --project src/ExcelToSqlite.Cli -- --list-names ./data/기초서류.xlsx

# 배치 실행
dotnet run --project src/ExcelToSqlite.Cli -- --config sample/sources.json
```

## 배포

런타임까지 넣어서 만들므로 받는 컴퓨터에 .NET이 없어도 열린다.

```bash
bash scripts/publish.sh               # 맥 앱  -> dist/ExcelToSqlite.app, dist/ExcelToSqlite-mac.zip
bash scripts/publish.sh win           # 윈도   -> dist/win/ExcelToSqlite.exe (한 파일, 맥에서도 만들 수 있음)
bash scripts/publish.sh cli win-x64   # 명령줄 -> dist/cli-win-x64/xl2sqlite.exe (RID를 빼면 이 컴퓨터용)
```

- 윈도 exe와 명령줄 도구는 **파일 하나**다. 그림·글꼴·SQLite 네이티브 라이브러리도 안에 들어 있고 처음 열 때 풀어 쓴다.
- 맥은 `.app` 폴더 안에 그대로 담고(풀어 쓰는 단계가 없어 빨리 열림) 임시 서명 후 zip으로 묶는다. Intel 맥용은 `MAC_RID=osx-x64`.
- 스크립트 없이 `dotnet publish src/ExcelToSqlite.Desktop -c Release -r win-x64` 만 해도 한 파일이 나온다 (설정은 csproj).
- 버전은 `Directory.Build.props` 의 `<Version>` 하나만 고친다. 창 제목과 맥 `Info.plist` 에 들어간다.
- GitHub에 올리면 Actions가 윈도·맥에서 빌드하고 배포 파일을 만들어 15초 동안 켜 본다.
  태그 `v<버전>`(예: `v1.0.0`)을 올리면 Releases에 `ExcelToSqlite.exe` 와 `ExcelToSqlite-mac.zip` 이 붙는다.
  태그가 `<Version>` 과 다르면 실패한다. `-beta` 처럼 `-`가 붙으면 시험판으로 올린다.

### 처음 열 때 (서명하지 않은 앱)

- **윈도**: SmartScreen이 막으면 `추가 정보` → `실행`.
- **맥**: 처음 열면 막힌다. **시스템 설정 → 개인정보 보호 및 보안** 아래쪽의 `그래도 열기`를 누른다.
  macOS Sequoia(15)부터 Control-클릭 → 열기로 넘어가는 길은 없어졌다. 최소 macOS 12.0.

## 동작 규칙

| 항목 | 규칙 |
|---|---|
| 단일 셀 이름 | **무시.** 셀 하나만 가리키는 명명범위는 테이블이 아닌 스칼라 파라미터로 보고 적재 대상에서 제외 (`--list-names`에는 `[Skipped: 단일 셀]`로 표시) |
| 컬럼 자동 추가 | **기본 꺼짐.** 켜면 추가 모드에서 소스에만 있는 컬럼을 `ALTER TABLE ADD COLUMN` 으로 붙이고 기존 행은 NULL로 둔다. PK 컬럼은 추가 불가 |
| 인덱스 | `indexColumns`(GUI `인덱스 컬럼`, CLI `--index`)에 컬럼명을 적으면 인덱스 생성. 콤마로 여러 개를 적으면 **복합 인덱스 1개**. PK와 별개이며 재적재 때마다 다시 만들어진다 |
| 대상 DB 확장자 | 확장자가 없으면 `.db` 를 자동으로 붙인다. `.db`/`.sqlite`/`.sqlite3`/`.db3` 는 그대로 둔다 |
| 대상 DB 경로 | GUI에서 이름만(`pricing`, `out/요율.db`) 넣으면 **첫 번째 원본 파일 폴더** 기준으로 만든다. `~/` 는 홈 폴더. CLI는 현재 폴더 기준 |
| 첫 행 | layout(헤더). 컬럼명으로 사용하며 공백·중복은 `COL3`, `CODE_2` 로 자동 보정. 한글 컬럼명 허용 |
| 기본키 | `keyColumnCount` 개수만큼 **선두 컬럼**을 SQLite 복합 PK로 선언. 기본 1, `0`이면 PK 없음. **엑셀·텍스트 동일 적용** |
| 값 결합 | **하지 않음.** `PRIMARY KEY ("c1","c2")` 로 컬럼을 그대로 유지 |
| PK NULL | SQLite는 PK 컬럼에도 NULL을 허용하므로 `NOT NULL`을 명시적으로 부여 (`keyColumnsNotNull`) |
| 타입 | **전 컬럼 TEXT.** 타입 추론을 하지 않는다. 숫자·날짜 해석은 조회 시 `CAST` 로 |
| 코드값 보존 | 전부 TEXT이므로 선행 0(`"0100"`)·긴 숫자 코드가 원문 그대로 남는다 |
| 인코딩 | BOM → 엄격 UTF-8 → CP949(EUC-KR) 순 자동 판별. `encoding`으로 강제 지정 가능. `cp949`·`euc-kr`은 둘 다 CP949(949)로 읽는다 — .NET의 `euc-kr`(51949)은 확장 한글(똠·햏 등)을 깨뜨리기 때문 |
| 구분자 | 첫 줄에서 탭 / 콤마 / 파이프 / 세미콜론 중 자동 선택. 따옴표 안의 구분자·줄바꿈 보존 |
| 파일 잠금 | Excel에서 열려 있는 파일도 `FileShare.ReadWrite` 로 읽음 |
| 이력 | `_import_manifest` 테이블에 소스경로·PK·행수·시각 기록 |
| 저널 모드 | 적재 중에는 WAL(빠름), 끝나면 `DELETE`로 되돌려 `.db` 파일 하나만 남긴다 (`-wal`/`-shm` 없음) |

## 주의

- 구형 `.xls`는 미지원. `.xlsx` / `.xlsm` 으로 저장할 것.
- 셀 하나만 가리키는 이름은 목록에서 제외된다(스칼라 파라미터로 간주).
- `--list-names` 로 확인되는 이름만 `objectName`에 쓸 수 있다. 시트 범위 이름은 `Sheet1!Name` 형식.
- `#REF!` 로 깨진 이름은 오류로 보고된다.
- 빌드에는 **.NET 10 SDK**가 필요하다(`global.json`). 배포 파일에는 런타임이 들어 있어 받는 쪽은 필요 없다.
- Avalonia는 11.3 이상이어야 한다(`Grid.RowSpacing` 사용). `Avalonia.Controls.DataGrid` 는 번호를 따로 매기므로 함께 올릴 때 주의 (11.3 줄의 마지막은 11.3.13).

## GUI 사용법

1. **파일을 창 아무 곳에나 끌어다 놓는다.** 폴더를 놓으면 안의 파일이 모두 들어온다.
   - `.xlsx` / `.xlsm` → 파일 안의 **명명범위마다 한 줄**이 생기고, 테이블명 기본값은 **이름 그대로**
   - 텍스트 파일 → **파일당 한 줄**, 테이블명 기본값은 **파일명(확장자 제외)**
   - `.db` / `.sqlite` → 소스가 아니라 **대상 DB**로 지정된다
2. **대상 DB**를 지정한다. `기존 DB 선택…` 또는 `새 DB 만들기…`. 이름만 적으면 첫 번째 원본 파일 폴더에 만든다.
   기존 파일이면 모드를 고른다.
   - **데이터 교체** — 같은 이름 테이블을 DROP 후 재생성
   - **신규 INSERT** — 기존 행을 두고 추가. 컬럼 구성이 다르면 해당 테이블만 오류로 보고.
     `컬럼 자동 추가`를 켜면 소스에만 있는 컬럼을 기존 테이블에 붙이고 기존 행은 비워 둔다
   - 중복 PK 처리: 오류 / 덮어쓰기(`INSERT OR REPLACE`) / 건너뛰기(`INSERT OR IGNORE`)
3. 그리드에서 사용 여부·테이블명·PK 개수·인덱스 컬럼을 조정한다. `기본 PK 개수` + `전체 적용`으로 일괄 변경 가능.
4. `실행`. 진행률과 테이블별 결과가 아래 로그에 남는다.

### 화면 상태 유지

종료 시 파일 목록·테이블 설정·DB 경로·옵션·창 크기를 저장하고 다음 실행에서 복원한다.
저장 위치는 사용자 설정 폴더 — 윈도 `%APPDATA%\ExcelToSqlite\ui-state.json`, macOS `~/Library/Application Support/ExcelToSqlite/ui-state.json`.
컴퓨터·사용자 계정마다 따로이며 실행 파일에는 들어가지 않는다 (배포 파일을 받은 사람은 빈 화면으로 시작).
`화면 초기화` 버튼을 누르면 화면과 저장된 상태를 모두 지운다.
복원 시 원본 파일이 사라졌으면 목록에 `파일 없음`으로 표시된다.

## Core만 따로 쓸 때

```csharp
var svc = new ImportService();
List<SourceSpec> specs = SourceExpander.Expand(path, defaultKeyCount: 1);
TableData preview = svc.Preview(specs[0], 100);
ImportResult result = await svc.RunAsync(request, progress, cts.Token);
```

`ImportRequest` / `SourceSpec` / `ImportOptions` 는 그대로 JSON 직렬화되므로
GUI에서 만든 구성을 CLI용 `sources.json` 으로 옮겨 배치 실행할 수 있다.

## 라이선스

MIT 라이선스 — [LICENSE](LICENSE). Copyright (c) 2026 inscompile.
