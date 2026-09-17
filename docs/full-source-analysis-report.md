# StickerMemo (Rust/Tauri) 전체 소스코드 분석 보고서

- **분석 대상**: `R:\AntiGravity Working\Sticker-Memo` (src-tauri/, ui/, docs/, tauri.conf.json 등)
- **분석 방식**: 코드 정적 분석(읽기 전용). 코드 수정/파일 생성·삭제 없음.
- **작성일**: 2026-09-17
- **참고 문서**: `docs/current-architecture.md`(구 WPF 버전 분석), `docs/rust-rebuild-plan.md`(Rust 리빌드 설계서)

> 본 리포지토리에는 과거 .NET/WPF 버전의 잔존 파일(App.xaml, Models/, Services/, Views/, StickerMemo.csproj 등)이 루트에 함께 존재합니다. 현재 활성 코드베이스는 `src-tauri/`(Rust 백엔드)와 `ui/`(Vanilla HTML/CSS/JS 프런트엔드)이며, 본 보고서는 이 Rust/Tauri 코드베이스만을 대상으로 합니다.

---

## 0. 코드베이스 개요 (본격 검토에 앞선 구조 파악)

```
src-tauri/
├── Cargo.toml / Cargo.lock / build.rs
├── tauri.conf.json                # "deck" 창 1개만 선언
├── capabilities/
│   ├── about-window.json          # "about" 창 전용, core:window:allow-destroy
│   └── deck-events.json           # "deck" 창 전용, core:event:allow-listen/unlisten
└── src/
    ├── main.rs                    # 엔트리포인트: 싱글 인스턴스, DB 초기화, 트레이, 창 복원, 전역 이벤트 핸들러
    ├── commands/mod.rs             # 모든 Tauri command (Note CRUD + 창 관리 + 설정 + import + 로깅) 단일 파일
    ├── domain/{note.rs,theme.rs}   # NoteModel, 미리보기 계산, 테마 정의(현재 미사용)
    ├── storage/db.rs               # rusqlite 기반 SQLite 접근 (단일 Mutex<Connection>)
    ├── importer/{sticky_notes.rs,rtf_cleaner.rs}  # Windows Sticky Notes 가져오기
    └── platform/{autostart.rs,single_instance.rs} # 레지스트리 자동시작, Win32 뮤텍스 기반 싱글 인스턴스

ui/
├── deck.html/css/js     # Edge Deck (우측 엣지 덱) — invoke + listen(core:event) 사용
├── note.html/css/js     # Floating Note — invoke만 사용 (event listen 없음)
├── dialog.html          # (현재 미사용으로 보이는 별도 다이얼로그 HTML, 실제 삭제 확인 UI는 delete-confirm.js가 각 창에 동적 주입)
├── delete-confirm.js/css # 삭제 확인 오버레이 (deck/note 공용, DOM 직접 삽입)
└── about.html/css       # 정보 창 (Rust에서 동적 WebviewWindowBuilder로 생성)
```

**실행 흐름 요약**: `main()` → 싱글 인스턴스 뮤텍스 획득 → SQLite 초기화(`Database::new`) → `AppState` 등록 → `tauri::Builder` 구성(핸들러 25개, 전역 `on_window_event`) → `setup()` 훅에서 wakeup 리스너 스레드 기동, deck 창 위치/크기 조정 및 표시, 트레이 생성, DB에 `IsFloating=true`로 남아있던 노트들을 `open_floating_note`로 순차 복원 → 이벤트 루프 진입.

노트는 3개 창 타입으로 표현됩니다: `deck`(정적 선언, 항상 1개), `note-{uuid}`(동적 생성, 노트당 최대 1개, 라벨 재사용), `about`(동적 생성, 온디맨드 1개).

---

## 1. 발견 사항 (심각도별 분류)

### 🔴 Critical

**C-1. 창을 닫을 때(도킹/보관/종료) 디바운스된 미저장 편집 내용이 유실될 수 있음**

- 관련 파일/함수: `ui/note.js` `triggerSave()`(L116-130, 400ms 디바운스), `scheduleWindowSave()`(L307-314, 500ms 디바운스), `closeBtn` 핸들러(L226-229), `archiveBtn` 핸들러(L211-214); `src-tauri/src/commands/mod.rs` `close_floating_note`(L274-298), `archive_note`(L103-130); `src-tauri/src/main.rs` `perform_clean_exit`(L28-37), `on_window_event`의 `CloseRequested` 처리(L93-111)
- 발생 조건: 사용자가 텍스트를 입력한 직후(400ms 이내) ①상단 `✕`(덱으로 복귀) 버튼 클릭, ②`📦` 보관 버튼 클릭, ③Alt+F4/작업관리자 등으로 개별 노트 창을 직접 닫음, ④트레이 메뉴 "🚪 StickerMemo 종료" 클릭 중 하나라도 발생하면, JS 쪽의 `setTimeout` 저장 타이머가 실행되기 전에 백엔드가 창을 `destroy()`합니다.
- 판단 근거: `close_floating_note`/`archive_note`/`on_window_event`의 `CloseRequested` 분기, `perform_clean_exit` 어디에도 "저장 대기 중인지 프런트엔드에 물어보고 flush를 기다린 뒤 닫는" 로직이 없습니다. 프런트엔드도 버튼 클릭 시 `clearTimeout`/즉시 저장 없이 곧바로 `invoke("close_floating_note"/"archive_note")`를 호출합니다. 특히 `perform_clean_exit`는 `app.webview_windows()`를 순회하며 무조건 `win.destroy()`를 호출한 뒤 50ms만 대기하고 `std::process::exit(0)`을 호출하므로, 여러 창이 동시에 열려 있으면 그중 어느 것도 flush 기회를 얻지 못합니다.
- 권장 수정 방향:
  1. 버튼 클릭 핸들러(닫기/보관/삭제)에서 `clearTimeout(saveTimer)` 후 대기 중인 변경사항이 있으면 즉시 `await invoke("save_note", {note})`를 완료하고 나서 닫기/보관 커맨드를 호출.
  2. OS 레벨 종료(Alt+F4, 트레이 종료)에 대응하려면 `CloseRequested`에서 `api.prevent_default()`로 즉시 종료를 막고, 해당 창에 "flush-and-close" 커스텀 이벤트를 `emit`한 뒤 프런트엔드가 flush 완료 후 스스로 `close()`/`destroy()`하도록 유도(단, 이를 위해서는 `note-*` 창에 `core:event:allow-listen` 권한이 필요 — capabilities 항목 C-3 참고). 프런트엔드 flush 완료 신호(예: `invoke("ready_to_close")`)를 받은 뒤에만 실제 destroy를 수행.
  3. `perform_clean_exit`도 동일하게 각 창에 flush 요청을 브로드캐스트하고, 타임아웃 예산(과거 WPF 버전의 1,500ms 방식처럼) 내에서 완료를 기다린 뒤 종료하도록 변경.

**C-2. `panic = "abort"` + 광범위한 `.unwrap()` 조합으로 인한 무경고 전체 프로세스 즉시 종료**

- 관련 파일: `src-tauri/Cargo.toml` L35 (`panic = "abort"`), `src-tauri/src/storage/db.rs` (7곳의 `self.conn.lock().unwrap()`), `src-tauri/src/main.rs` L194 (`app.default_window_icon().cloned().unwrap()`)
- 발생 조건: DB 락 관련 코드나 그 외 어떤 경로에서든 예기치 못한 패닉이 한 번이라도 발생하면(예: 향후 코드 변경으로 인덱스 초과, 예상치 못한 유니코드 처리 오류 등), release 빌드에서는 스택 언와인딩 없이 프로세스가 즉시 abort됩니다.
- 판단 근거: `panic = "abort"`는 `catch_unwind`로 개별 커맨드의 패닉을 격리할 수 없게 만들며, 열려 있는 모든 Floating Note의 미저장 편집(C-1과 결합 시 더 치명적)까지 동시에 날아갑니다. 메모 앱의 핵심 가치가 "데이터 보존"임을 고려하면 이 조합은 특히 위험합니다.
- 권장 수정 방향: (a) `panic = "abort"`를 제거하고 `unwind`로 전환해 `std::panic::catch_unwind`를 커맨드 디스패치 경계에 둘 수 있게 하거나, (b) 최소한 `std::panic::set_hook`을 등록해 패닉 발생 시 모든 창에 "긴급 저장" 이벤트를 브로드캐스트한 후 종료하도록 하거나(단, hook 실행 후에도 abort는 진행되므로 프런트엔드가 동기적으로 저장을 완료할 시간을 벌기 어려움 — 근본적으로는 (a)가 더 안전), (c) `Mutex` 락 관련 `.unwrap()`을 poison-안전 헬퍼로 감싸거나 `parking_lot::Mutex`(poisoning 없음)로 교체.

---

### 🟠 High

**H-1. Edge Deck 창이 파괴되면 재생성 경로가 전혀 없음**

- 관련 파일/함수: `src-tauri/src/main.rs` `on_window_event`(L93-111), `commands::toggle_deck`(L358-370), `tauri.conf.json`(deck는 정적 선언 1회성)
- 발생 조건: 사용자가 deck 창에 포커스를 준 상태에서 Alt+F4를 누르거나, 어떤 경로로든 deck 창에 `CloseRequested`가 발생하는 경우.
- 판단 근거: 전역 `on_window_event` 핸들러는 `label.starts_with("note-")`인 경우만 처리하고 `api.prevent_default()`를 호출하지 않습니다. Tauri v2에서 `CloseRequested`를 가로채고도 기본 동작을 막지 않으면 창은 그대로 파괴 절차를 진행합니다. `about` 창은 트레이 메뉴 핸들러에서 "존재하면 show, 없으면 새로 build"하는 self-healing 패턴(`main.rs` L215-221)을 갖고 있지만, `deck`은 `toggle_deck()`이 `app.get_webview_window("deck")`이 `None`이면 아무것도 하지 않고 조용히 무시합니다. 즉 deck 창이 한 번 파괴되면 트레이 아이콘 좌클릭/메뉴로도 복구가 불가능하고 앱을 완전히 재시작해야 합니다.
- 권장 수정 방향: `toggle_deck`(및 트레이 좌클릭 핸들러)을 `about`과 동일한 get-or-create 패턴으로 변경하거나, `on_window_event`에서 `deck` 라벨에 대해 `api.prevent_default()` 후 `hide()`로 대체(사용자가 원치 않는 종료를 방지).

**H-2. 디바운스 자동저장과 삭제/보관 사이의 경쟁 조건 — 삭제된 노트의 "부활"**

- 관련 파일/함수: `ui/note.js` `triggerSave()`(L116-130), `deleteBtn`/`archiveBtn` 핸들러(L211-223); `src-tauri/src/commands/mod.rs` `save_note`(L59-86), `delete_note`(L88-101); `src-tauri/src/storage/db.rs` `save_or_update_note`의 `INSERT ... ON CONFLICT(Id) DO UPDATE`(L186-214)
- 발생 조건: 사용자가 텍스트를 입력해 400ms 저장 타이머가 대기 중인 상태에서 즉시 삭제 확인 다이얼로그를 띄우고 확인을 누르는 타이밍(수백 ms 내)에, JS의 `setTimeout` 콜백이 `delete_note` 커맨드 완료 후 실행될 수 있습니다.
- 판단 근거: `save_or_update_note`는 순수 upsert(`INSERT ... ON CONFLICT DO UPDATE`)이므로, 행이 이미 삭제된 뒤 같은 `id`로 `save_note`가 호출되면 **DELETE가 아니라 새로운 INSERT로 처리되어 방금 삭제한 노트가 그대로 다시 생성**됩니다. `save_note` 커맨드는 노트 존재 여부를 검증하지 않고 무조건 upsert를 수행합니다.
- 권장 수정 방향: 삭제/보관 버튼 클릭 시 `clearTimeout(saveTimer)`로 대기 중인 자동저장을 취소(C-1 수정과 동일한 지점), 그리고/또는 백엔드 `save_note`를 `UPDATE ... WHERE Id = ?`로 변경해 존재하지 않는 id에 대해서는 아무 것도 하지 않도록(신규 생성은 `create_note` 경로로만 허용) 방어선을 이중으로 둘 것.

---

### 🟡 Medium

**M-1. `on_window_event`의 `CloseRequested` 처리가 메인/이벤트 스레드에서 동기 DB I/O 수행**

- 관련: `src-tauri/src/main.rs` L93-111 (`state.db.get_all_active_notes()` + `save_or_update_note()`를 윈도우 이벤트 콜백 안에서 직접 호출)
- Tauri의 `on_window_event` 콜백은 플랫폼 이벤트 루프(메인) 스레드에서 실행됩니다. 현재는 SQLite가 로컬이고 데이터량이 작아 체감 지연은 없지만, 노트 수가 많아지거나 디스크 I/O가 지연되는 환경(네트워크 드라이브, 안티바이러스 스캔 등)에서는 UI 프레임 드롭/입력 지연을 유발할 잠재적 지점입니다. `open_floating_note`만 `#[tauri::command(async)]`로 표시되어 있고 나머지 커맨드는 Tauri의 기본 스레드풀 오프로딩에 맡겨져 있어 커맨드 자체는 괜찮으나, 이 콜백은 커맨드 디스패치 경로가 아니라 이벤트 루프에 직접 박혀 있다는 점이 다릅니다.
- 권장: DB 조회/저장을 `tauri::async_runtime::spawn`으로 오프로드하거나 최소한 워스트케이스를 인지하고 유지.

**M-2. `note-*` 창에 대한 capability가 전혀 없음 — 향후 기능 확장 시 조용히 막힘**

- 관련: `src-tauri/capabilities/*.json`(about, deck만 존재), 동적 생성되는 `note-{id}` 라벨
- 현재는 `note.js`가 커스텀 command(`invoke`)만 사용하고 `core:*` 플러그인 API(창 이벤트 리스닝, 클립보드, 다이얼로그, 알림 등)를 전혀 쓰지 않기 때문에 지금 당장 오류가 나지는 않습니다(Tauri v2에서 앱이 직접 등록한 `#[tauri::command]`는 ACL 대상이 아니며, capability는 `core:`/플러그인 네임스페이스 API에만 적용됩니다). 다만 C-1의 권장 수정안(플로팅 노트가 백엔드로부터 "flush-and-close" 이벤트를 `listen`하게 만드는 것)이나, 향후 클립보드/알림/파일 드래그앤드롭 같은 기능을 note 창에 추가하려는 순간 이 capability 공백이 실제 장애로 나타납니다.
- 권장: `note-*` 패턴(Tauri capability의 `windows` 필드는 글롭 패턴을 지원)에 대한 capability 파일을 미리 준비해두거나, 최소한 이 제약을 문서화.

**M-3. 테마 색상 정의가 3곳에 중복**

- 관련: `src-tauri/src/domain/theme.rs`(`get_theme`, `#[allow(dead_code)]`로 실제 미사용), `ui/deck.js` L9-16(`THEMES` 객체), `ui/note.css` L39-85(`.theme-*` CSS 클래스)
- Rust 쪽 `theme.rs`는 어디에서도 호출되지 않는 죽은 코드이며, 실제 색상 진실 공급원은 JS와 CSS에 각각 하드코딩되어 있습니다. 테마를 추가/변경하려면 최소 2곳(JS, CSS)을 반드시 함께 수정해야 하고, 놓치면 Floating Note와 Deck 탭의 색상이 어긋나는 시각적 불일치가 발생합니다.
- 권장: 단일 진실 공급원을 정하고(예: Rust `theme.rs`를 커맨드로 노출해 프런트엔드가 부팅 시 fetch하거나, 반대로 JSON/CSS 변수 하나로 통일), 미사용 `theme.rs`는 실제로 연결하거나 제거.

**M-4. `commands/mod.rs`가 393줄 단일 파일에 여러 책임을 혼재**

- 관련: `src-tauri/src/commands/mod.rs` 전체 (Note CRUD + 창 생성/파괴/이동/크기 + 자동시작 설정 + import + 로깅이 한 파일에 공존)
- `docs/rust-rebuild-plan.md`(L58-62)는 애초에 `note_commands.rs`/`window_commands.rs`/`system_commands.rs`로 분리하는 계획이었으나 실제 구현은 단일 파일로 합쳐졌습니다. 현재 규모(약 20개 커맨드)에서는 아직 치명적이지 않지만, 기능이 추가될수록 diff 충돌과 가독성 저하가 누적됩니다.
- 권장: 계획대로 서브모듈 분리.

**M-5. 노트 창 파괴 로직이 3곳에서 각각 재구현됨**

- 관련: `close_floating_note`(L274-298), `archive_note`(L103-130), `on_window_event`의 `CloseRequested`(L93-111) — 세 곳 모두 "활성 노트 목록에서 찾기 → 플래그 갱신/저장 → 라벨로 창 찾기 → destroy → 이벤트 emit" 패턴을 미묘하게 다르게 반복 구현.
- 권장: 공용 내부 함수(예: `fn teardown_floating_window(app, state, id, reason)`)로 통합해 세 경로의 플래그 갱신/이벤트 emit 누락 위험을 줄일 것.

**M-6. Windows Sticky Notes 가져오기의 WAL/SHM 복사 경쟁 가능성**

- 관련: `src-tauri/src/importer/sticky_notes.rs` L61-68 (`plum.sqlite-wal`/`-shm`를 `let _ = fs::copy(...)`로 무시 가능한 실패로 복사)
- Windows Sticky Notes 앱이 동시에 `plum.sqlite`를 WAL 모드로 열어 쓰고 있는 상태에서 원본을 복사하면, 메인 DB 파일과 WAL 파일이 서로 다른 시점의 스냅샷이 되어 일관성이 깨질 수 있습니다(트랜잭션 torn read). 현재는 컬럼 존재 여부만 확인(`PRAGMA table_info`)하고 내용 무결성은 검증하지 않습니다.
- 영향은 제한적(가져오기 실패 시 사용자가 재시도 가능, 원본은 읽기 전용이라 손상 없음)이나, 드물게 깨진 텍스트가 가져와질 수 있음을 인지해둘 필요.

**M-7. `windows` crate의 일부 feature가 선언만 되고 미사용**

- 관련: `src-tauri/Cargo.toml` L21-28 — `Win32_UI_WindowsAndMessaging`, `Win32_Graphics_Gdi`, `Win32_UI_HiDpi` feature가 활성화되어 있으나 현재 `.rs` 코드 어디에서도 해당 API가 호출되지 않음(실사용은 `Win32_Foundation`, `Win32_System_Threading`뿐).
- 바이너리 크기에 미미한 영향과 함께, "이미 네이티브 창 제어가 구현되어 있다"는 오해를 줄 수 있습니다. 다만 아래 3장(Known Issue)에서 다룰 전체화면 감지 기능 구현 시 정확히 이 feature들이 필요하므로, 지금 당장 제거하기보다는 "구현 예정 자리"로 문서화하는 것을 권장합니다.

**M-8. CSP가 명시적으로 비활성화됨**

- 관련: `src-tauri/tauri.conf.json` L27 (`"csp": null`)
- 현재는 전적으로 로컬 정적 HTML/JS만 로드하고, `deck.js`가 사용자 입력을 렌더링할 때 `escapeHtml()`을 사용하고 있어(L432-440) 직접적인 XSS 경로는 확인되지 않았습니다. 다만 CSP 부재는 향후 실수로 `innerHTML`에 사용자 데이터(예: 가져온 Sticky Notes 텍스트, 노트 본문)를 직접 삽입하는 코드가 추가될 경우 방어선이 전혀 없다는 뜻입니다.
- 권장: 최소한의 `default-src 'self'` 수준 CSP 설정.

**M-9. 로그 파일이 무한 성장하며 회전(rotation) 로직이 없음**

- 관련: `src-tauri/src/main.rs` `log_startup()`(L18-26), 매 호출마다 `%TEMP%\StickerMemo-rust.log`를 append로 열고 씀.
- `docs/current-architecture.md`(L52)에 따르면 구 WPF 버전은 1MB 초과 시 `.old`로 순환 백업했으나, Rust 리빌드에서는 이 기능이 빠져 있습니다(기능 패리티 회귀). 장시간 실행 시(자동시작 사용자는 컴퓨터를 켤 때마다 실행) 로그가 계속 누적됩니다. 매 호출 시 파일을 열고 닫는 방식이라 I/O 오버헤드도 존재하지만 심각한 수준은 아닙니다.

---

### 🟢 Low

- **L-1.** `main.rs` L194 `app.default_window_icon().cloned().unwrap()` — 앱 시작 극초반(사용자 데이터 로드 전) 아이콘 미설정 시 패닉. 영향은 제한적이나 `unwrap_or_else`로 방어 가능.
- **L-2.** `platform::single_instance::SingleInstanceGuard`의 `Drop` 구현(L17-23, `CloseHandle` 호출)이 실제로는 절대 실행되지 않음 — 유일한 정상 종료 경로인 `perform_clean_exit`가 `std::process::exit(0)`을 호출하기 때문에 Rust 소멸자가 스킵됩니다. OS가 프로세스 종료 시 핸들을 자동 회수하므로 기능적 문제는 없지만, "RAII로 정리된다"는 코드상의 암묵적 가정이 실제로는 성립하지 않는 상태이므로 오해의 소지가 있습니다.
- **L-3.** `NoteModel`의 `is_topmost`, `tape_angle` 필드가 DB 스키마/구조체에는 존재하나 UI 로직에서 실질적으로 읽거나 분기에 사용되지 않음(테이프 각도는 CSS에서 전 노트 공통 `-0.6deg`로 고정). 구 WPF 버전에서 이어받은 흔적 컬럼으로 보이며, 정리하거나 실제로 연결할지 결정 필요.
- **L-4.** "+새 메모" 버튼(`deck.js` L327-336)이 클릭 중 비활성화되지 않아, 빠른 연속 클릭 시 두 개의 새 노트가 거의 동시에 생성될 수 있음(경합이라기보다 방어 부재).
- **L-5.** `import/sticky_notes.rs`의 임시 파일(`%TEMP%\StickerMemo\Import\plum_{uuid}.sqlite`)은 정상 흐름에서는 정리되지만, 프로세스가 패닉(`panic=abort`)으로 죽는 시점이 복사 이후·삭제 이전이면 정리되지 않고 누적됨. 영향은 미미(디스크 공간)하나 C-2와 연관.
- **L-6.** `about.html`의 인라인 `<script>`와 `note.js`/`deck.js` 상단의 `window.__TAURI__` 접근 방식이 파일마다 조금씩 다르게 방어적으로 작성되어 있음(일관성 부족, 기능상 문제는 아님).

---

### 🔧 개선 제안 (취향/스타일 성격, 장애 가능성 낮음)

- 싱글 인스턴스를 Win32 뮤텍스+이벤트로 직접 구현(`platform/single_instance.rs`)하는 대신 공식 `tauri-plugin-single-instance`로 대체 검토(단, 현재 커스텀 wakeup 로직이 이미 안정적으로 동작하는 것으로 보이므로 필수는 아님).
- `storage/db.rs`의 `std::sync::Mutex<Connection>`을 poison이 없는 `parking_lot::Mutex`로 교체 검토.
- `commands/mod.rs`를 계획대로 `note_commands.rs` / `window_commands.rs` / `system_commands.rs`로 분리.
- 이벤트 브로드캐스트 패턴 통일 — 현재 `note-updated`(payload 포함, optimistic patch)와 `notes-changed`(payload 없음, 전체 refetch)가 혼재. 상황별 기준을 정리하거나 하나로 통일.
- `domain/theme.rs`의 `get_theme`/`MemoTheme`를 실제로 연결하거나(단일 진실 공급원화) 제거.

---

## 2. Known Issue 심층 분석 — 전체화면 게임 위로 Floating Note/Deck이 표시되는 문제

### 2.1 관련 코드 위치

| 위치 | 내용 |
|---|---|
| `src-tauri/tauri.conf.json` L20 | `"alwaysOnTop": true` — `deck` 창 정적 설정 |
| `src-tauri/src/commands/mod.rs` L180 | 기존 노트 창 재표시 시 `existing.set_always_on_top(true)` |
| `src-tauri/src/commands/mod.rs` L222 | 신규 노트 창 빌드 시 `.always_on_top(true)` |
| `src-tauri/src/commands/mod.rs` L256 | 빌드 직후 재차 `win.set_always_on_top(true)` |
| `src-tauri/Cargo.toml` L21-28 | `windows` crate 의존성에 `Win32_UI_WindowsAndMessaging`, `Win32_Graphics_Gdi`, `Win32_UI_HiDpi` feature가 **이미 선언되어 있으나 미사용** |
| (없음) | 포그라운드 창 변경 감지, 전체화면 판정, 모니터 경계 비교 로직 — 코드베이스 전체에 **존재하지 않음** |

### 2.2 원인 분석

Tauri의 `always_on_top(true)`는 Windows에서 결국 `SetWindowPos(hwnd, HWND_TOPMOST, ...)`로 귀결됩니다. 이는 해당 창을 Windows Z-order의 "topmost 밴드"에 **영구적으로** 배치하는 정적 설정일 뿐이며, "다른 어떤 창이 전체화면이 되면 자동으로 내려간다"는 동적 피드백 루프를 OS가 알아서 제공해주지 않습니다. 이런 협조적 동작(작업표시줄이 전체화면 게임 실행 시 스스로 내려가는 것과 동일한 종류)은 **각 topmost 애플리케이션이 스스로 구현해야 하는 로직**이며, StickerMemo는 현재 이 로직을 전혀 갖고 있지 않습니다 — `always_on_top`을 설정만 하고 이후 재평가하는 코드가 없습니다.

**Exclusive Fullscreen vs Borderless Fullscreen 차이:**

- **Exclusive Fullscreen(DirectX 독점 전체화면)**: 게임이 디스플레이의 프레젠테이션을 직접 장악하는 모드입니다. 이 모드에서는 원칙적으로 다른 topmost 창들이 화면에 나타나지 않아야 하나, 실제로는 사용자가 StickerMemo의 노트/덱에 클릭이나 포커스 이벤트를 발생시키는 순간(`open_floating_note`의 `set_focus()` 호출 등, L179, L255) Windows가 게임을 독점 모드에서 강제로 이탈시켜(흔히 "게임이 알트탭 시 창모드로 떨어진다"는 현상과 동일 메커니즘) 일반 창모드/합성 상태로 전환시키는 경우가 흔합니다. 이 경우 게임이 더 이상 독점 모드가 아니게 되므로 StickerMemo의 topmost 창이 정상적인 Z-order 규칙에 따라 위에 나타납니다.
- **Borderless Fullscreen(테두리 없는 창모드 전체화면)**: 실제로는 그냥 모니터 크기에 맞춘 **평범한 일반 창**입니다. DWM 합성이 정상적으로 계속 동작하며, 이 창은 스스로 topmost로 설정하지 않는 한 Z-order 상에서 아무 특권이 없습니다. 따라서 StickerMemo의 `HWND_TOPMOST` 창은 아무 개입 없이도 항상 이 위에 그려집니다. 사용자가 보고한 "마우스 오버만으로 Deck이 펼쳐지는 등 포커스 이동 없는 상황에서도 게임 위에 나타난다"는 현상(`deck.js`의 `onTabMouseEnter`/`transitionToFan`은 어떤 Tauri 커맨드도 호출하지 않는 순수 CSS/DOM 동작입니다, L141-151, L175-180)은 게임이 Borderless Fullscreen으로 실행 중일 가능성이 훨씬 높다는 강한 정황 증거입니다. 최신 게임 다수가 알트탭 속도 때문에 기본값으로 Borderless를 채택하는 경향이 있어, 실무적으로는 이 케이스가 지배적일 것으로 판단됩니다.

**결론**: 현재 구조상 "게임 위로 올라오는" 것은 버그라기보다는, StickerMemo가 애초에 "다른 창이 전체화면인지" 판단할 방법 자체를 구현하지 않았기 때문에 나타나는 **누락된 기능**입니다.

### 2.3 Tauri API만으로 가능한가?

**불가능합니다.** Tauri의 공개 API(`set_always_on_top`, `is_focused`, `primary_monitor`, `available_monitors`, `monitor_from_point`)는 다음을 제공하지 않습니다:

- OS 전체의 현재 포그라운드(활성) 창이 무엇인지 조회하는 기능(자기 자신의 포커스 여부만 `is_focused()`로 알 수 있음)
- 임의의 외부 프로세스 창이 "전체화면 상태인지" 판별하는 기능
- 포그라운드 창 변경을 구독하는 이벤트 훅

즉, Z-order 토글 자체(`set_always_on_top(bool)`)는 Tauri API로 충분하지만, **"언제 토글해야 하는가"를 판단하는 감지 로직은 반드시 Win32 API가 필요**합니다. 다행히 필요한 Win32 함수들의 feature flag(`Win32_UI_WindowsAndMessaging`, `Win32_Graphics_Gdi`)가 `Cargo.toml`에 이미 선언되어 있어 새 의존성 추가 없이 구현 가능합니다.

### 2.4 권장 설계 방향

핵심 아이디어: **"단순히 always_on_top=false로 바꾸는 것"이 아니라, 포그라운드 창이 전체화면으로 판단될 때만 일시적으로 topmost를 해제하고, 상황이 끝나면 다시 topmost로 복귀시키는 감시(watcher) 로직을 추가**합니다.

1. **감지 방법 (Exclusive/Borderless 공통 처리)**
   - `GetForegroundWindow()`로 현재 활성 창 HWND 획득
   - 그 HWND가 StickerMemo 자신의 창이면 판정 스킵(자기 자신과 비교하지 않도록 주의 — 그렇지 않으면 사용자가 노트를 클릭하는 순간 스스로를 전체화면으로 오판할 위험)
   - `GetWindowRect(hwnd)`로 창 사각형 획득, `MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST)` + `GetMonitorInfoW`로 해당 창이 위치한 모니터의 전체 경계(`rcMonitor`) 획득
   - 창 사각형이 모니터 전체 경계와 정확히 일치(또는 거의 일치, 완충값 허용)하면 "전체화면 앱이 활성 상태"로 판정
   - 이 방식은 Exclusive/Borderless 모드를 구분하지 않고 동일하게 잡아냅니다 — 독점 전체화면 게임의 원래 HWND도 통상 모니터 크기와 일치하는 사각형을 갖기 때문에, 별도로 DXGI 스왑체인을 후킹하는 등 게임 내부까지 들여다볼 필요 없이 이 창 크기 비교 휴리스틱 하나로 충분합니다.
2. **감지 트리거 방식 — 두 가지 옵션**
   - **(권장, 우선 구현) 폴링 방식**: `tauri::async_runtime::spawn` + `tokio::time::interval`로 약 500ms~1000ms 주기로 위 판정 로직 실행. 구현이 단순하고 Tauri 자체의 메인 이벤트 루프와 충돌 없이 통합 가능. 체감 지연(전체화면 진입/해제 후 최대 1초 이내 반응)은 이 UX에서 허용 가능한 수준.
     - 부작용: 매우 미미한 상시 CPU 웨이크업 비용(무시할 수준). 사용자가 창을 빠르게 전환할 때 짧은 깜빡임(토글 churn) 가능 — 기존 코드에 이미 쓰인 디바운스 패턴(200ms/350ms)과 유사하게 300~500ms 디바운스를 상태 전환에 추가해 완화 권장.
   - **(향후 최적화 옵션) `SetWinEventHook(EVENT_SYSTEM_FOREGROUND, ...)` 이벤트 훅**: 포그라운드 변경 즉시 반응 가능해 지연이 거의 없음. 다만 Win32 메시지 펌프(`GetMessage`/`DispatchMessage`)가 도는 스레드에서 등록·처리되어야 하므로, Tauri(tao)가 이미 메인 스레드에서 돌리는 이벤트 루프와 별도의 경쟁하는 메시지 펌프를 만들지 않도록 통합에 주의가 필요합니다(단순히 `std::thread::spawn`으로 새 스레드에 훅을 걸면 해당 스레드에도 자체 메시지 루프가 필요). 구현 난이도가 폴링보다 높으므로 1차 구현에는 폴링을 권장하고, 추후 성능/반응성이 문제가 되면 전환.
3. **토글 대상 관리**: 감시 로직은 매 판정 시 `app.webview_windows()`(이미 `perform_clean_exit`에서 쓰인 패턴)를 순회하며 `deck`과 모든 `note-*` 창에 대해 일괄로 `set_always_on_top(bool)`을 적용해야 합니다 — 동적으로 열고 닫히는 Floating Note 개수에 항상 대응하기 위함입니다. 하드코딩된 창 목록을 두지 않도록 주의(M-7/구조적 부채와 연결).
4. **멀티 모니터 고려**: 전체화면 게임과 StickerMemo 창이 서로 다른 모니터에 있다면, 게임이 있는 모니터의 창만 판정 대상으로 삼고 다른 모니터의 StickerMemo 창은 topmost를 유지해야 합니다. 이를 위해 판정 시 각 StickerMemo 창이 현재 위치한 모니터와 전체화면 창이 위치한 모니터가 같은지 비교하는 로직이 필요합니다(창별로 개별 topmost 상태를 관리해야 하며, 전역 on/off 플래그 하나로는 부족).
5. **복귀 조건**: 전체화면 앱이 종료되거나 포커스가 벗어나면(포그라운드 창이 더 이상 모니터 전체를 덮지 않으면) 다음 폴링 주기(또는 훅 콜백)에서 자동으로 `set_always_on_top(true)`를 재적용 — 별도의 특별한 "복귀" 코드 경로 없이 동일한 판정 루프에서 자연히 처리됩니다.
6. **예상 부작용 정리**:
   - 폴링 주기 동안 게임이 전체화면으로 전환되는 찰나(최대 1초)에는 여전히 StickerMemo가 잠깐 위에 보일 수 있음 — 완전한 실시간성은 이벤트 훅 방식에서만 보장됨.
   - 창 크기 비교 휴리스틱은 "모니터 전체를 덮는 일반 창"을 모두 전체화면으로 간주하므로, 브라우저의 전체화면 동영상 재생, 프레젠테이션 도구 등도 동일하게 StickerMemo를 내리게 됩니다 — 이는 대체로 바람직한 부작용이지만 명시적으로 문서화할 필요.
   - 사용자가 StickerMemo 노트 자체를 최대화/화면 전체 크기로 만드는 극단적 케이스는 없음(리사이즈 가능하나 최대화 커맨드 없음)이므로 자기 자신 오판 위험은 낮으나, 판정 로직에서 "포그라운드 HWND == 자기 자신의 창"인 경우는 반드시 조기 제외해야 함.

**요약**: `always_on_top=false`로의 단순 변경이 아니라, (1) Win32 API 기반의 "포그라운드 창이 모니터를 가득 채우는가" 폴링 감시자를 신설하고, (2) 감시 결과에 따라 Tauri의 `set_always_on_top(bool)`을 런타임에 동적으로 토글하며, (3) 모니터별/창별로 이를 관리하는 것이 요구사항(평소엔 topmost 유지, 전체화면 게임 위에서만 예외)을 만족하는 유일한 실현 가능 경로입니다. 새 외부 크레이트 추가 없이 이미 `Cargo.toml`에 선언된 `windows` crate feature만으로 구현 가능합니다.

---

## 3. 아키텍처 및 주요 이벤트 흐름 요약 (신규 개발자를 위한 개요)

**계층 구조**
- **domain**: `NoteModel`(직렬화 가능한 노트 엔티티 + 표시용 미리보기 3종 계산), `theme`(현재 미연결).
- **storage**: 단일 `Mutex<rusqlite::Connection>`을 감싼 `Database`. 스키마는 시작 시 `CREATE TABLE IF NOT EXISTS` + 컬럼별 `ALTER TABLE` 자동 마이그레이션으로 관리. 모든 쓰기는 `INSERT ... ON CONFLICT DO UPDATE` upsert 패턴.
- **commands**: 프런트엔드가 `invoke()`로 호출하는 유일한 진입점. `AppState{db}`를 통해 DB에 접근하고, 결과에 따라 `AppHandle::emit`으로 이벤트를 브로드캐스트.
- **platform**: Windows 전용 기능(자동시작 레지스트리, 싱글 인스턴스 뮤텍스+wakeup 이벤트).
- **importer**: Windows Sticky Notes DB를 안전하게 복사·파싱해 `NoteModel` 목록으로 변환.
- **main.rs**: 위 모든 것을 조립하는 부트스트랩 + 트레이 메뉴 + 전역 창 이벤트 핸들러 + 강제 종료 루틴.

**창(Window) 3종과 생명주기**
| 창 | 생성 시점 | 라벨 | 재사용 여부 | 종료 시 동작 |
|---|---|---|---|---|
| `deck` | 앱 시작 시 1회(정적 선언) | `deck` | 항상 1개, 재생성 로직 없음(H-1) | 없음 — 파괴되면 복구 불가 |
| `note-{uuid}` | 노트 클릭/생성 시 동적 | `note-{id}` | id당 1개, 이미 있으면 show+focus로 재사용 | 3개 경로(닫기/보관/OS 닫기)가 각각 파괴 처리 |
| `about` | 트레이 "정보" 클릭 시 동적 | `about` | get-or-create 패턴 | JS 또는 기본 CloseRequested로 파괴 |

**핵심 이벤트 흐름 (Deck ↔ Floating Note 동기화)**
1. 사용자가 Deck 탭 클릭 → `deck.js`가 `invoke("open_floating_note", {id})` → Rust가 창을 생성/재표시하고 DB `IsFloating=true` 갱신 후 `emit("floating-state-changed", (id, true))` → `deck.js`의 리스너가 해당 탭에 📌 표시.
2. Floating Note에서 타이핑 → 400ms 디바운스 후 `invoke("save_note", {note})` → Rust가 DB 저장 후 `emit("note-updated", note)` → `deck.js`가 해당 탭의 제목/미리보기를 즉시 갱신(전체 재조회 없이 optimistic patch).
3. 삭제/보관/서랍(Drawer)에서의 조작은 Deck 쪽에서 직접 `delete_note`/`archive_note`/`close_floating_note`를 순차 호출 → `emit("note-deleted", id)` 또는 `emit("notes-changed")` → `deck.js`가 로컬 배열 갱신 또는 전체 재조회.
4. Floating Note(`note.js`)는 어떤 Tauri 이벤트도 `listen`하지 않습니다(capability도 없음, M-2) — 오직 Deck만 `core:event:allow-listen` 권한을 가지고 4종 이벤트(`note-updated`, `notes-changed`, `note-deleted`, `floating-state-changed`)를 구독합니다. 노트 창끼리 또는 노트→덱 방향의 실시간 반영은 전부 "노트가 백엔드에 커맨드를 보내면 백엔드가 덱에 이벤트를 쏘는" 단방향 구조입니다.

**앱 종료 흐름**: 트레이 "🚪 종료" 또는 `exit_app` 커맨드 → `perform_clean_exit` → 모든 웹뷰 창 강제 `destroy()` → 50ms 대기 → `std::process::exit(0)`(Rust 소멸자 미실행, OS가 핸들 회수) — 이 경로가 C-1(미저장 데이터 유실)의 가장 흔한 실제 트리거입니다.

**싱글 인스턴스**: 이름 있는 전역 Win32 뮤텍스(`Global\StickerMemo_SingleInstance_Mutex_2026`)로 중복 실행을 막고, 중복 실행 감지 시 이름 있는 이벤트(`Global\StickerMemo_Wakeup_Event_2026`)를 시그널하여 기존 인스턴스의 백그라운드 리스너 스레드가 Deck 창을 show/focus 하도록 깨웁니다.
