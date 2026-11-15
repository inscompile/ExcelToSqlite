# CLAUDE.md

엑셀 명명범위·텍스트 파일을 SQLite `.db`로 적재하는 도구 ExcelToSqlite (C# · .NET 10 · Avalonia).
공개 저장소 `inscompile/ExcelToSqlite`. 사용자에게는 **한국어로, 쉬운 말로** 답한다.

- 설계 결정·지난 버그·남은 일은 [HANDOFF.md](HANDOFF.md), 사용법·동작 규칙·배포는 [README.md](README.md). 고친 내용은 이 두 문서에 함께 적는다.
- **커밋은 이 폴더의 git 설정 그대로 한다** (`kjh321` / GitHub noreply 주소). 작성자·메일을 따로 넣지 말고, 다른 메일(개인 메일)을 넣지 말 것.
- **공개 저장소다.** 개인 경로(`/Users/…`, Dropbox·iCloud), 업무 파일·회사·고객 이름, 개인 사정을 코드·문서·커밋 메시지에 넣지 않는다. 시험·예제 데이터는 직접 만든 것만 (`.gitignore`가 루트를 화이트리스트로 막고 `*.xlsx`·`*.db`도 막는다 — 새 루트 파일은 `.gitignore`에 `!/이름`을 더해야 들어간다).
- 푸시·태그 올리기·저장소 설정 바꾸기는 매번 사용자 확인.
- 화면에 나오는 글은 한국어.

## 명령

```bash
dotnet build -c Release
dotnet run --project src/ExcelToSqlite.Desktop            # 화면 앱
dotnet run --project src/ExcelToSqlite.Cli -- --help      # 명령줄 (xl2sqlite)
bash scripts/publish.sh            # 맥 .app zip  (dist/)
bash scripts/publish.sh win        # 윈도 exe 한 파일 (dist/win/)
dotnet run --project scripts/IconGen   # 아이콘 다시 만들기 (src/ExcelToSqlite.Desktop/Assets)
```

- 버전은 `Directory.Build.props`의 `<Version>` 한 곳. 태그 `v<버전>`을 올리면 Actions(`.github/workflows/build.yml`)가 윈도·맥에서 빌드·실행 확인 후 Releases에 `ExcelToSqlite.exe`·`ExcelToSqlite-mac.zip`을 붙인다.
- 윈도 실행 확인은 Actions로만 할 수 있다 (개발은 맥에서 함).
- 프로젝트를 Dropbox·iCloud 같은 동기화 폴더 안에 두지 말 것 (맥에서 화면 앱이 SIGKILL로 죽음 — HANDOFF 1번).
