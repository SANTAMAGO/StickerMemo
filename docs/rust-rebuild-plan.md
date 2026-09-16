# StickerMemo Rust 리빌드 설계 및 실행 계획서

본 문서는 StickerMemo의 Rust 리빌드를 위한 아키텍처 설계, 기술 스택 선정, 모듈 분리, 기존 기능 호환성 체크리스트 및 단계별 구현 계획을 정의합니다.

---

## 1. 기술 스택 선정 및 비교 분석

### 1.1 후보 프레임워크 비교

| 평가 항목 | **Tauri v2 (채택)** | **Slint** | **egui / iced** | **windows-rs (Win32 Native)** |
| :--- | :--- | :--- | :--- | :--- |
| **다중 창(Multi-Window) 지원** | **탁월** (독립 창 동적 생성/제어) | 보통 (다중 창 API 제약 있음) | 미흡 (단일 창 캔버스 위주) | 강력 (단, 모든 창 루프 수동 관리) |
| **투명/프레임리스 창 지원** | **탁월** (OS 레벨 그림자, 마우스 패스스루) | 보통 (플랫폼별 윈도우 튜닝 필요) | 어려움 (OS 투명 블렌딩 한계) | 강력 (DWM/DirectComposition 필요) |
| **한국어 IME (조합형 한글 입력)** | **완벽** (WebView2 네이티브 IME) | 일부 미흡 (조합 중 글자 깜빡임) | 미흡 (IME 조합창 위치 및 버그) | 복잡 (IMM32/TSF 직접 구현 필요) |
| **메모리 / 실행 속도** | 우수 (~30MB 기본 메모리) | **최상** (~15MB 네이티브) | **최상** (~20MB) | **최상** (~10MB) |
| **단일 실행 파일 배포** | **우수** (WebView2 런타임 공유로 5~10MB) | **최상** (단일 바이너리) | **최상** (단일 바이너리) | **최상** (단일 바이너리) |
| **UI/UX 재현율** | **100%** (CSS 그림자, 스프링 애니메이션) | 80% (스타일링 제약) | 60% (고정된 렌더러 스타일) | 85% (막대한 코드량 소요) |
| **개발 생산성 및 안정성** | **최상** (Rust 백엔드 + 모던 UI) | 보통 | 낮음 | 매우 낮음 |

### 1.2 선정 결과 및 이유: **Tauri v2 + Rust Core**
1. **안정적인 다중 창 아키텍처**: StickerMemo의 핵심 경험은 `EdgeDeckWindow`(화면 우측 가장자리 도킹 투명 창)와 N개의 `FloatingNoteWindow`(자유롭게 바탕화면에 떠 있는 개별 투명 창)가 유기적으로 연동되는 것입니다. Tauri v2는 `WebviewWindowBuilder`를 통해 투명, 프레임리스, 항상 위(`always_on_top`), 태스크바 미표시(`skip_taskbar`) 창을 자유롭게 동적 생성/파괴할 수 있습니다.
2. **한국어 IME 입력의 완벽성**: 스티커 메모는 텍스트 입력이 전부인 도구입니다. Rust 네이티브 GUI의 가장 취약한 점인 한글 완성형/조합형 입력 결함 없이, Windows 표준 크로미움 기반 WebView2를 통해 100% 무결점 한글 타이핑을 제공합니다.
3. **기존 데이터베이스 100% 호환**: 핵심 데이터 저장 및 비즈니스 로직은 Rust의 `rusqlite`(bundled SQLite3)를 사용하여 기존 `%APPDATA%\StickerMemo\AppNotes.db`를 한 글자의 데이터 손실도 없이 그대로 사용합니다.

---

## 2. Rust 프로젝트 아키텍처 및 모듈 분리

단순 일체형 코드가 아닌 관심사의 분리(Separation of Concerns) 원칙에 따라 모듈을 구성합니다:

```
src-tauri/
├── Cargo.toml
├── src/
│   ├── main.rs                   # 앱 엔트리포인트 및 런타임 시작
│   ├── app/                      # 애플리케이션 수명 주기 및 코디네이터
│   │   ├── mod.rs
│   │   ├── state.rs              # AppState (DB 커넥션 풀, 창 레지스트리)
│   │   └── single_instance.rs    # 전역 명명된 뮤텍스 핸들러
│   ├── domain/                   # 비즈니스 도메인 모델
│   │   ├── mod.rs
│   │   ├── note.rs               # Note 구조체 및 미리보기 연산 로직
│   │   └── theme.rs              # 6종 테마 및 색상 정의
│   ├── storage/                  # 영속성 계층 (SQLite)
│   │   ├── mod.rs
│   │   ├── db.rs                 # rusqlite 연결 및 트랜잭션 관리
│   │   └── migration.rs          # 기존 AppNotes.db 스키마 호환 검증
│   ├── platform/                 # Windows OS 종속 기능
│   │   ├── mod.rs
│   │   ├── autostart.rs          # HKCU Run 레지스트리 관리
│   │   ├── tray.rs               # 시스템 트레이 아이콘 및 메뉴
│   │   └── screen.rs             # 화면 해상도, DPI, 작업영역 계산
│   ├── importer/                 # 외부 데이터 마이그레이션
│   │   ├── mod.rs
│   │   ├── sticky_notes.rs       # plum.sqlite 안전 복사 및 파싱
│   │   └── rtf_cleaner.rs        # RTF 제어문자 및 메타데이터 정제
│   └── commands/                 # 프론트엔드 통신 IPC 커맨드 핸들러
│       ├── mod.rs
│       ├── note_commands.rs      # CRUD 및 검색
│       ├── window_commands.rs    # 창 열기/닫기/이동/도킹
│       └── system_commands.rs    # 자동시작, 트레이, 가져오기
ui/                               # 클라이언트 프레젠테이션 (Vanilla HTML/CSS/JS)
├── index.html                    # EdgeDeck 창 UI
├── note.html                     # FloatingNote 창 UI
├── tray.html                     # 커스텀 페이퍼 트레이 메뉴 UI
├── dialog.html                   # 확인/경고 모달 다이얼로그 UI
├── styles/                       # 기존 WPF 스타일과 1:1 대응되는 CSS
└── js/                           # 창별 프론트엔드 컨트롤러
```

---

## 3. 기존 기능 호환성 체크리스트

| 기능 영역 | 기존 C# WPF 버전 | Rust 리빌드 버전 | 검증 및 테스트 방법 |
| :--- | :---: | :---: | :--- |
| **단일 인스턴스 실행** | `Global\StickerMemo_SingleInstance_Mutex_2026` | Windows API 명명된 뮤텍스 구현 | 중복 실행 시 기존 창 활성화 후 조용히 종료 확인 |
| **데이터베이스 호환** | `%APPDATA%\StickerMemo\AppNotes.db` | 동일 경로, 동일 `Notes` 테이블 스키마 직접 접근 | 기존 C# 버전에서 작성된 메모가 Rust 버전에서 즉시 로드되는지 확인 |
| **우측 엣지 덱 (Dormant)** | 우측 16px 얇은 띠(Stripe) 상태 | 투명 창 우측 도킹 + 16px 스트라이프 | 마우스 오버 전 화면 가림 최소화 확인 |
| **우측 엣지 덱 (Fan)** | 마우스 진입 시 135px 탭 펼침 | CSS/JS 부드러운 전이 애니메이션 | 마우스 진입 시 탭 목록 펼쳐짐 확인 |
| **호버 본문 미리보기** | 탭 마우스 오버 시 250x80px 확장 카드 | 팝업/카드 확장 및 본문 2~3줄 미리보기 | 마우스 이탈 시 정상 복귀 타이머 동작 확인 |
| **메모 서랍 (All Notes)** | `+N more` 클릭 시 310px Drawer | 슬라이드 아웃 서랍 패널 | Active / Archived 세그먼트 전환, 실시간 검색 |
| **플로팅 메모 창** | 드래그 이동, 우하단 크기 조절 | Frameless 창, 헤더 드래그, 리사이즈 | 바탕화면 자유 이동 및 최소 280x180 제약 |
| **메모 CRUD** | 생성, 수정, 삭제, 보관 | Rust SQLite CRUD | 실시간 디바운스 자동 저장 및 상태 보존 |
| **서식 및 테마** | 글꼴/크기/굵게, 6종 파스텔 테마 | 폰트 패밀리, pt 조절, 테마 변경 | 메모별 서식 및 테마 적용 저장 |
| **트레이 메뉴** | 우클릭 커스텀 페이퍼 메뉴 | 마우스 좌표 기반 커스텀 팝업 | Show/Hide Deck, 새 메모, 자동시작 토글 |
| **Windows 자동 시작** | HKCU Run 레지스트리 | Windows Registry API 연동 | 부팅 시 자동 실행 등록 및 해제 확인 |
| **Sticky Notes 가져오기**| `plum.sqlite` 읽기 전용 복사 후 파싱 | 동일한 안전 복사 및 RTF 파서 구현 | Windows 스티커 메모 가져오기 무결성 검증 |

---

## 4. 데이터 호환성 보장 원칙

1. **포맷 파괴 금지**: 기존 `AppNotes.db`의 SQLite 테이블 구조(`Notes`)를 100% 동일하게 사용합니다. 컬럼명, 데이터 타입, 정렬 기준(`SortOrder ASC, UpdatedAt DESC`)을 정확히 일치시킵니다.
2. **동시 안전성 및 자동 백업**: 최초 마이그레이션이나 앱 실행 시 만일의 사태를 대비하여 `AppNotes.db.backup` 복사본을 1회 생성한 후 연결합니다.
3. **무결점 롤백 지원**: Rust 버전으로 메모를 추가/수정하더라도 다시 기존 C# 버전으로 실행했을 때 완벽히 인식되도록 상호 호환성을 유지합니다.

---

## 5. 단계별 실행 로드맵 (Commit 단위)

- **Step 1: Git 브랜치 및 프로젝트 스캐폴딩** (`feat: initialize rust rebuild`)
  - `rebuild/rust` 브랜치 설정
  - Rust 프로젝트 및 Tauri v2 환경 구성
- **Step 2: 데이터 도메인 모델 및 SQLite 영속성 구현** (`feat: implement domain models and sqlite storage`)
  - `NoteModel`, `MemoTheme` 정의
  - `AppNotes.db` 연결 및 CRUD 단위 테스트 검증
- **Step 3: EdgeDeck 창 구현** (`feat: implement edge deck window and drawer`)
  - 화면 우측 도킹, Dormant/Fan 상태 전이, 호버 미리보기, All Notes Drawer
- **Step 4: FloatingNote 창 구현** (`feat: implement floating note windows`)
  - 투명 프레임리스 창, 드래그/리사이즈, 서식/테마 팝업, 자동 저장 디바운스
- **Step 5: 시스템 트레이 및 OS 통합** (`feat: implement system tray and windows integration`)
  - 트레이 아이콘, 커스텀 트레이 메뉴, 레지스트리 자동 시작, 단일 인스턴스 뮤텍스
- **Step 6: Windows Sticky Notes 가져오기 포팅** (`feat: implement sticky notes importer`)
  - `plum.sqlite` 임시 복사 및 RTF 클리너 파서 구현
- **Step 7: 최종 패키징, 데이터 상호 호환성 검증 및 빌드** (`build: complete rust release build`)
  - 릴리스 최적화 바이너리 생성 및 C# 버전과의 데이터 교차 검증
