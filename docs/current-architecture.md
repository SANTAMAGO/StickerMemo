# StickerMemo 현재 아키텍처 분석 보고서

본 문서는 StickerMemo의 Rust 기반 리빌드를 위해 기존 C# (.NET 8 WPF) 기반 프로그램의 전체 구조, 데이터 모델, 동작 메커니즘 및 OS 종속 기능을 상세히 분석한 문서입니다.

---

## 1. 전체 디렉터리 및 기술 스택

### 1.1 기술 스택
- **런타임 / 언어**: C# 12 / .NET 8 (`net8.0-windows`)
- **UI 프레임워크**: WPF (Windows Presentation Foundation) + WinForms (System.Windows.Forms - 트레이 아이콘 및 멀티 모니터 Screen 정보 수집)
- **MVVM 툴킷**: `CommunityToolkit.Mvvm` (8.4.2)
- **데이터베이스**: SQLite (`Microsoft.Data.Sqlite` 10.0.11)
- **패키징 / 배포**: 단일 파일 자체 포함(Self-contained Single EXE) 실행 파일 (`PublishSingleFile=true`, `IncludeNativeLibrariesForSelfExtract=true`)

### 1.2 디렉터리 구조
```
Sticker-Memo/
├── App.xaml / App.xaml.cs          # 앱 진입점, 단일 인스턴스 뮤텍스, 전역 예외 처리
├── AssemblyInfo.cs                 # 어셈블리 메타데이터
├── StickerMemo.csproj              # .NET 프로젝트 파일 및 패키지 참조
├── Assets/
│   ├── app_icon.ico                # 윈도우 및 트레이 아이콘 (ICO)
│   └── app_icon.png                # 앱 리소스 아이콘 (PNG)
├── Models/
│   ├── NoteModel.cs                # 메모 도메인 엔티티 (ObservableObject)
│   └── ThemeColors.cs              # 6종 테마 색상 팔레트 정의 (Yellow, Pink, Mint, Blue, Purple, Kraft)
├── Services/
│   ├── AppIconHelper.cs            # WPF 윈도우 핸들에 아이콘 적용 헬퍼
│   ├── NoteStorageService.cs       # SQLite 로컬 DB CRUD 및 스키마 마이그레이션
│   ├── StartupService.cs           # Windows 시작 시 자동 실행 레지스트리 관리
│   ├── StickyNotesImporter.cs      # Windows Sticky Notes(plum.sqlite) 읽기 전용 가져오기
│   ├── StickyNoteTextParser.cs     # RTF 및 Sticky Notes 메타태그 정제 파서
│   └── WindowManager.cs            # 전체 윈도우 라이프사이클 및 시스템 트레이 관리자
├── Views/
│   ├── AppConfirmDialog.xaml(.cs)  # 커스텀 모달 다이얼로그 (삭제 확인, 경고창)
│   ├── EdgeDeckWindow.xaml(.cs)    # 우측 화면 가장자리 덱(Deck) 메인 윈도우
│   ├── FloatingNoteWindow.xaml(.cs)# 개별 독립 플로팅 메모 윈도우
│   └── TrayMenuWindow.xaml(.cs)    # 시스템 트레이 우클릭 커스텀 페이퍼 메뉴
└── Properties/
    └── PublishProfiles/            # 배포 프로필 (FolderProfile.pubxml 등)
```

---

## 2. 프로그램 진입점 및 라이프사이클 (`App.xaml.cs`)

1. **단일 인스턴스 보장 (Single-Instance)**:
   - 시스템 전역 명명된 뮤텍스 (`Global\StickerMemo_SingleInstance_Mutex_2026`) 생성.
   - 중복 실행 시 조용히 종료(`Current.Shutdown(0)`).
2. **진단 로그 (Startup Diagnostics)**:
   - `%TEMP%\StickerMemo-startup.log`에 프로세스 ID 및 타임스탬프와 함께 시작/종료 로그 기록.
   - 1MB 초과 시 `.old` 파일로 순환 백업.
3. **초기화 시퀀스**:
   - `WindowManager.Instance.Initialize()` 호출:
     1) 트레이 아이콘 초기화 (`SetupTrayIcon()`)
     2) 우측 엣지 덱 윈도우 표시 (`EdgeDeckWindow.Show()`)
     3) 기존에 `IsFloating == true`였던 활성 메모들 복원 (`RestoreFloatingNotes()`)
4. **종료 시퀀스 (`WindowManager.ExitApplication`)**:
   - `IsShuttingDown = true` 플래그 설정
   - 열려 있는 모든 플로팅 메모의 변경 사항을 1,500ms 타임아웃 예산 내에서 동기/비동기 최종 플러시
   - SQLite 커넥션 풀 정리 (`SqliteConnection.ClearAllPools()`)
   - 트레이 아이콘 해제 및 WinForms 루프 종료
   - 윈도우 숨김 처리 후 `Application.Current.Shutdown(0)`

---

## 3. UI 구조 및 동작 방식

### 3.1 EdgeDeckWindow (우측 엣지 덱)
- **속성**:
  - `WindowStyle="None"`, `AllowsTransparency="True"`, `Background="Transparent"`
  - `Topmost="True"`, `ShowInTaskbar="False"`, `ResizeMode="NoResize"`
  - 크기: 폭 460px, 높이 `Math.Min(640, 작업영역 높이 * 0.75)`
  - 위치: 화면 오른쪽 끝 작업영역 중앙 정렬 (`Left = workArea.Right - Width`)
- **상태 전이 (State Machine)**:
  - **Dormant (비활성)**: 탭 폭 16px (StripeTabWidth). 화면 우측 끝에 얇은 색상 띠로 대기. 텍스트 숨김.
  - **Fan (활성)**: 탭 폭 135px (CompactTabWidth), 높이 34px. 마우스가 덱 위로 진입하면 부드럽게 펼쳐짐.
  - **Hover Preview (미리보기)**: 개별 탭에 마우스를 올리면 폭 250px, 높이 80px의 확장 카드로 변환되어 본문 2~3줄 미리보기 표시. 마우스 이탈 시 200ms 후 복귀. 마우스가 덱 전체를 벗어나면 350ms 후 Dormant로 전환.
- **주요 기능**:
  - 상위 최대 8개 활성 메모 탭 노출 (테마 색상 띠, 핀 아이콘, 제목).
  - 8개 초과 시 `+N more` 버튼 표시.
  - 하단 `➕ 새 메모` 버튼으로 즉시 새 메모 생성 후 플로팅 윈도우 오픈.
  - **All Notes Drawer (메모 서랍)**:
    - 덱 좌측에 310px 폭의 카드로 슬라이드 아웃.
    - `[Active]` / `[Archived]` 세그먼트 탭 전환.
    - 120ms 디바운스 실시간 검색창.
    - 보관된 메모 복원(↩) 및 영구 삭제(🗑️) 지원.

### 3.2 FloatingNoteWindow (플로팅 스티커 메모)
- **속성**:
  - 독립적인 투명 프레임리스 윈도우, 부드러운 그림자(DropShadow 22px blur).
  - 기본 크기: 350x350px (최소 280x180px).
  - 스냅 방지: `WM_SYSCOMMAND` + `SC_MAXIMIZE` 차단.
  - 이동: 상단 헤더 영역 드래그 (`DragMove`).
  - 크기 조정: 우측 하단 커스텀 크기 조절 그립(`Thumb` 드래그).
- **상단 툴바**:
  - 좌측: 테이프 배지 (각도 -0.6도, 테마 라벨).
  - 우측 툴바 버튼:
    - 🔤 **서식 메뉴**: 팝업에서 글꼴 패밀리 드롭다운, 글자 크기 스텝 버튼(`−`, `+`), 굵게(`B`) 토글 칩.
    - 🎨 **테마 팔레트**: 6가지 원형 색상 버튼 (Yellow, Pink, Mint, Blue, Purple, Kraft).
    - 📦 **보관 (Archive)**: 메모를 보관함으로 이동하고 플로팅 창 닫기.
    - 🗑️ **삭제 (Delete)**: 커스텀 컨펌 다이얼로그 후 영구 삭제.
    - ✕ **덱으로 복귀 (Dock back)**: 플로팅 창을 닫고 엣지 덱의 탭으로 수납 (`IsFloating = false`).
- **본문 편집 및 자동 저장**:
  - 상단 1줄 제목 입력칸 + 하단 여러 줄 본문 텍스트 에디터.
  - 텍스트 변경 시 400ms 디바운스 자동 저장.
  - 창 이동/크기 변경 시 500ms 디바운스 좌표/크기 자동 저장.
  - 덱의 해당 탭 실시간 동기화 (`NotifyNoteTextChanged`).

### 3.3 TrayMenuWindow (트레이 메뉴)
- 트레이 아이콘 우클릭 시 마우스 커서 위치에 맞추어 표시되는 종이 카드 스타일 팝업.
- 멀티 모니터 및 모니터별 DPI 인식 (`GetDpiForMonitor`).
- 메뉴 항목:
  - ➕ 새 메모
  - 🗂️ / 🙈 Show Deck / Hide Deck
  - 🚀 Start with Windows (✓ ON / OFF 토글)
  - 📥 Import Sticky Notes
  - 🚪 Quit

---

## 4. 데이터 저장 방식 및 스키마 (`AppNotes.db`)

### 4.1 저장 경로
- `%APPDATA%\StickerMemo\AppNotes.db`
- 프로세스 전역 세마포어(`WriteLock`)를 통한 동시 쓰기 직렬화.

### 4.2 SQLite 테이블 스키마 (`Notes`)
```sql
CREATE TABLE IF NOT EXISTS Notes (
    Id TEXT PRIMARY KEY,
    RemoteId TEXT,
    Title TEXT,
    Text TEXT,
    X REAL,
    Y REAL,
    Width REAL,
    Height REAL,
    ExpandedHeight REAL,
    IsCollapsed INTEGER,
    IsArchived INTEGER DEFAULT 0,
    IsFloating INTEGER DEFAULT 0,
    SortOrder INTEGER DEFAULT 0,
    ThemeId TEXT,
    IsTopmost INTEGER,
    TapeAngle REAL,
    FontFamily TEXT DEFAULT 'Malgun Gothic',
    FontSize REAL DEFAULT 13.5,
    IsBold INTEGER DEFAULT 0,
    CreatedAt TEXT,
    UpdatedAt TEXT
);
```

### 4.3 쿼리 규칙
- **활성 메모**: `WHERE IsArchived = 0 ORDER BY SortOrder ASC, UpdatedAt DESC`
- **보관 메모**: `WHERE IsArchived = 1 ORDER BY UpdatedAt DESC`
- **업데이트**: `INSERT ... ON CONFLICT(Id) DO UPDATE SET ...`

---

## 5. Windows Sticky Notes 마이그레이션 로직

1. 원본 DB 위치:
   `%LOCALAPPDATA%\Packages\Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe\LocalState\plum.sqlite`
2. 안전한 읽기:
   - 원본을 직접 열지 않고 `%TEMP%\StickerMemo\Import\`에 `plum_{GUID}.sqlite` 및 `-wal`, `-shm` 파일을 임시 복사 후 `Mode=ReadOnly`로 오픈.
3. 스키마 적응형 파싱:
   - `PRAGMA table_info(Note)`로 동적 컬럼 탐색.
   - `StickyNoteTextParser`를 통해 RTF 제어문자(`\par`, `\u...`, `\metadata{...}`) 및 메타데이터를 순수 플레인 텍스트로 정제.
   - 기존 `RemoteId` 중복 건너뛰기.

---

## 6. OS 종속 및 윈도우 통합 기능

1. **시작 프로그램 등록**:
   - `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` 레지스트리에 `"StickerMemo" = "\"{exePath}\" --autostart"` 등록/삭제.
2. **윈도우 메시지 및 네이티브 API**:
   - `WM_SYSCOMMAND` / `SC_MAXIMIZE` 가로채기로 최대화 및 윈도우 스냅 차단.
   - 트레이 아이콘 (WinForms `NotifyIcon`).
   - `GetCursorPos`, `MonitorFromPoint`, `GetDpiForMonitor`로 멀티 DPI 좌표 산출.
   - 상단 드래그를 위한 Win32 캡처 해제 / 창 이동 메시지 전달.
