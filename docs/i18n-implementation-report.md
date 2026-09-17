# StickerMemo 다국어(i18n) 구현 완료 보고서

작업 브랜치: `feature/i18n-ko-en-ja`
기반 문서: `docs/i18n-localization-analysis-report.md`
작성일: 2026-09-17

이 문서는 `feature/i18n-ko-en-ja` 브랜치에서 수행한 (1) Deck Always-On-Top
회귀 수정과 (2) ko/en/ja 다국어 구현의 결과를 정리한 완료 보고서다.

## 1. 브랜치 및 커밋 구성

작업 시작 시점에 `rebuild/rust` 브랜치에 커밋되지 않은 변경 사항(오버사이즈
Deck 창을 실제 표시 영역 크기로 줄이는 클릭-스루 수정, 커스텀 삭제 확인
모달, About 창, 노트 라이프사이클 변경 등)이 남아 있었다. 이를 폐기하지
않고 `feature/i18n-ko-en-ja` 브랜치를 새로 만든 뒤 그대로 이어받아, 먼저
체크포인트 커밋으로 보존했다.

이후 작업은 아래 순서로 커밋했다:

1. `wip: carry over in-progress deck click-through fix and misc lifecycle work`
   — 작업 시작 전 존재하던 미커밋 변경 사항 보존.
2. `fix(deck): keep Deck always-on-top while dormant`
   — Always-On-Top 회귀만 담은 독립 커밋 (§7 참고).
3. `feat(i18n): add locale resource pipeline (ko/en/ja single source of truth)`
   — 로케일 JSON, Rust i18n 모듈, Windows 로케일 감지, Settings 테이블,
     capability 파일.
4. `feat(i18n): localize Rust-side commands, tray, window titles, and startup locale`
   — 트레이 메뉴, 창 제목, 커맨드, 임포터, 자동 시작 등 Rust 쪽 문자열 전면
     교체 및 로케일 저장/전환 로직.
5. `feat(i18n): wire up frontend translations and add Settings screen`
   — WebView(deck/note/about) 번역 연동, 신규 Settings 화면.

## 2. 변경된 파일 목록

체크포인트(`a6f26bd`) 이후 30개 파일, 총 1340줄 추가 / 175줄 삭제.

**Rust (`src-tauri/`)**
- `src/i18n.rs` (신규) — 번역 조회/치환/키 일치성 테스트
- `src/platform/locale.rs` (신규) — Windows UI 로케일 감지
- `src/platform/mod.rs` — `locale` 모듈 등록
- `src/main.rs` — 시작 시 로케일 해석, 트레이 메뉴 다국어화, Settings 창 추가
- `src/commands/mod.rs` — `get_locale_info`/`set_locale` 신설, 기존 커맨드
  문자열 교체
- `src/domain/note.rs` — 기본값 리터럴 제거(빈 문자열 센티널로 변경)
- `src/importer/sticky_notes.rs` — 임포터 메시지 다국어화
- `src/platform/autostart.rs` — 자동 시작 에러 메시지 다국어화
- `src/storage/db.rs` — `Settings` 키/값 테이블 추가(Notes와 완전 분리)
- `Cargo.toml` — `windows` 크레이트에 `Win32_Globalization` 피처 추가
- `tauri.conf.json` — Deck 창 `alwaysOnTop: true` 복원
- `capabilities/about-window.json` — 이벤트 리슨 권한 추가
- `capabilities/note-events.json` (신규) — `note-*` 창 이벤트 권한
- `capabilities/settings-window.json` (신규) — Settings 창 권한

**Frontend (`ui/`)**
- `lang/ko.json`, `lang/en.json`, `lang/ja.json` (신규) — 번역 단일 소스,
  로케일당 69개 키
- `i18n.js` (신규) — 공용 로더/치환/DOM 적용 유틸리티
- `deck.html`, `deck.js`, `deck.css` — 번역 연동, 드로어 헤더 오버플로 방어
- `note.html`, `note.js`, `note.css` — 번역 연동, 타임스탬프 오버플로 방어
- `delete-confirm.js` — 공용 삭제 확인 모달 번역 연동
- `about.html`, `about.css` — `<br>` 하드코딩 제거, 번역 연동
- `settings.html`, `settings.css`, `settings.js` (신규) — 언어/자동 시작 설정 화면

## 3. i18n 아키텍처

번역 데이터는 `ui/lang/{ko,en,ja}.json` 세 파일에만 존재하는 단일 소스다.
Rust는 `include_str!`로 동일한 JSON 세 개를 컴파일 타임에 그대로 임베드해서
읽으므로(`src-tauri/src/i18n.rs`), Rust용 번역 테이블을 별도로 손으로
중복 유지하지 않는다. WebView 쪽은 `ui/i18n.js`가 런타임에 `fetch()`로 같은
JSON을 읽는다.

키는 문자열이 아니라 의미 단위로 설계했다(`deck.*`, `note.*`, `about.*`,
`tray.*`, `settings.*`, `delete_confirm.*`, `importer.*`, `error.*`,
`seed.*`, `common.*`). 요청하신 대로 한국어 "새 메모"가 가리키는 서로 다른
두 개념은 별도 키로 분리했다: `deck.add_note_label`(Deck의 "새 메모 추가"
버튼 라벨, 영문 "New Note")과 `note.default_title`(제목 없는 메모의 기본
표시 텍스트, 영문 "Untitled")는 한국어 값만 우연히 같을 뿐 완전히 다른
키다.

치환은 위치 기반이 아니라 `{name}` 형태의 이름 기반 플레이스홀더를 쓴다
(예: `"deck.drawer.footer_active": "Showing {shown} of {total} active notes"`).
언어별로 어순이 달라도 치환이 깨지지 않는다.

개발자 로그/진단 문자열(`log_startup`, `log_front`로 남기는 내부 로그)은
번역 대상에서 제외했고, 사용자가 직접 입력한 메모 제목/본문은 어떤
경우에도 번역하지 않는다 — 애초에 번역 파이프라인이 건드리는 대상이
아니다.

## 4. 로케일 감지 방식

트레이 메뉴는 WebView가 뜨기 전에 네이티브로 만들어지므로
`navigator.language`에 의존할 수 없다는 점을 고려해, Rust 쪽에서 Windows
API `GetUserDefaultLocaleName`(`windows` 크레이트, `Win32_Globalization`
피처)을 직접 호출해 감지한다(`src-tauri/src/platform/locale.rs`). 결과
로케일 태그("ko-KR", "ja-JP" 등)는 `map_locale_tag()`로 `ko`/`ja`/`en`
셋 중 하나로 정규화하고, 지원하지 않는 로케일(예: `fr-FR`, `zh-CN`)은 전부
영어로 폴백한다.

`main::resolve_initial_locale()`이 앱 시작 직후(트레이 생성 전, DB 초기화
직후) 이 감지 로직과 저장된 설정을 조합해 최초 로케일을 확정한다.

## 5. 로케일 저장 방식

`storage/db.rs`에 Notes와 완전히 분리된 `Settings` 키/값 테이블을
추가했다(`CREATE TABLE IF NOT EXISTS`이므로 기존 사용자 DB와 100% 호환).
`locale` 키에는 두 가지 값이 저장될 수 있다:

- 명시적 로케일 코드(`"ko"`, `"en"`, `"ja"`) — 사용자가 Settings에서 직접
  선택한 경우, 그 값을 그대로 쓴다.
- `"system"` — 최초 실행 시 기본으로 저장되는 값이며, Settings에서
  "시스템 기본값"을 선택해도 다시 이 값으로 돌아간다. 이 값이 저장되어
  있으면 매 실행마다 Windows UI 로케일을 다시 감지하므로, OS 언어를
  바꾸면 앱도 재선택 없이 따라간다.

## 6. 새 언어 추가 절차

향후 fr/de/zh-CN/zh-TW 등을 추가할 때 건드릴 곳은 다음 두 가지뿐이다.
`deck.js`/`note.js`/Rust 커맨드의 비즈니스 로직은 전혀 손댈 필요가 없다.

1. `ui/lang/<code>.json`을 만들어 `en.json`과 정확히 같은 키 집합으로
   채운다.
2. `src-tauri/src/i18n.rs`의 `SUPPORTED_LOCALES`에 `"<code>"`를 추가하고,
   `load_dicts()`에 `include_str!("../../ui/lang/<code>.json")` 한 줄을
   추가한다. `ui/i18n.js`의 `SUPPORTED_LOCALES` 배열도 동일하게 갱신한다.

키 누락/불일치는 `src-tauri/src/i18n.rs`의 `#[test] fn
all_locales_have_matching_keys`가 `cargo test` 실행 시 자동으로 잡아낸다
(en.json 기준으로 다른 모든 로케일의 키 집합을 비교해서, 하나라도
빠지거나 남으면 실패). 별도로 Python으로 세 JSON 파일의 키 집합을 직접
비교해 69개 키가 모두 일치함을 지금 확인했다(§9 참고).

## 7. Always-On-Top 회귀 수정 상세

기존(이전 세션에서 미커밋 상태로 남아 있던) 클릭-스루 수정은 Deck 창을
460×640의 과대한 투명 영역에서 실제 표시 영역 크기로 줄이는 데는
성공했지만, 그 과정에서 `tauri.conf.json`의 정적 기본값이
`"alwaysOnTop": false`로 바뀌어 있었고 `set_deck_interaction_state`도
`dormant` 상태일 때 `set_always_on_top(false)`를 호출하도록 되어 있었다.
이 두 곳이 결합되어 Dormant(16px 스트라이프) 상태의 Deck이 다른 창 뒤로
가라앉는 회귀가 발생했다.

말씀하신 대로 원래 버그의 원인은 Always-On-Top 자체가 아니라 과대한
HWND였으므로, 이번 수정은 그 결론을 그대로 반영해서 최소한으로만
고쳤다:

- `tauri.conf.json`: Deck 창의 정적 `alwaysOnTop`을 `true`로 복원.
- `set_deck_interaction_state`: 상태와 무관하게 `set_always_on_top(true)`를
  무조건 호출하도록 변경. 기존의 `state != "dormant"` 조건은 이름을
  `expanding`으로 바꿔 "포커스를 주고 창을 보여줄지"만 결정하는 용도로만
  남기고, always-on-top 여부를 좌우하지 않게 분리했다.

경계/크기 계산 로직(`syncDeckNativeBounds`, 물리 좌표 변환, 오른쪽 화면
경계 고정)은 이미 올바르게 동작하고 있었으므로 전혀 건드리지 않았다.
Fullscreen 앱에서의 Always-On-Top 억제는 요청하신 대로 이번 범위에서
제외했다(§10 Known Issues).

## 8. 빌드 결과 — 환경 제약 안내

**중요**: 이 작업은 실제 Windows 머신에 연결된 세션을 통해 파일을
읽고 쓰지만, 셸 명령(`device_bash`)은 그 머신 위의 **격리된 Linux VM**
에서 실행됩니다. 이 VM에는 `cargo`/`rustc`가 설치되어 있지 않고, 애초에
Linux이기 때문에 WebView2·Win32 API·`windows_subsystem = "windows"`에
의존하는 이 Windows 전용 Tauri 앱을 컴파일하거나 실행할 수 없습니다.
따라서 요청하신 "Release 빌드"를 이 세션에서 직접 수행하는 것은
불가능하며, 실제 Windows 호스트에서 사용자가 직접 실행해야 합니다.

실제 머신에서 실행할 명령:

```
cd "<repo-root>/src-tauri"
cargo test
cargo build --release
```

`cargo test`는 `all_locales_have_matching_keys`를 포함한 모든 유닛
테스트를 실행하며, 여기서 실패가 나면 Release 빌드 전에 먼저 확인해야
한다. 빌드된 실행 파일은 평소와 같이
`src-tauri/target/release/sticker-memo.exe`에 생성된다.

## 9. 이 세션에서 실제로 수행한 자동 검증

컴파일/GUI 실행이 불가능한 환경 제약 안에서, 가능한 범위의 검증은
다음과 같이 실제로 수행했다.

- ko/en/ja 세 JSON 파일을 Python으로 파싱해 키 집합을 비교 — 69개 키
  전부 세 로케일에서 일치함을 확인.
- `src-tauri/src` 전체를 유니코드 정규식으로 스캔해 한글 리터럴이 남아
  있는지 확인 — 결과 0건 (모든 사용자 대면 문자열이 `i18n::t`/`tf` 호출로
  치환됨을 확인).
- `ui/*.html`에 남아 있는 한글 리터럴을 전수 확인 — 전부 `data-i18n*`
  속성이 붙은 JS 로드 전 폴백 텍스트이거나, 의도적으로 손대지 않기로 한
  글꼴 드롭다운의 한글 병기(§10)뿐임을 확인.
- 수정한 Rust 파일 9개에 대해 중괄호/괄호 개수 균형 검사 — 전부 일치
  (컴파일 보장은 아니지만 명백한 절단/누락은 배제).
- `Cargo.toml`의 `windows` 피처 배열에 `Win32_Globalization`이 문법
  오류 없이 추가되었는지, `platform/mod.rs`에 `locale` 모듈이 올바르게
  등록·재노출되었는지 직접 코드로 재확인.
- `git apply --cached --check`로 Always-On-Top 수정 패치가 커밋 히스토리
  상 다른 변경과 깔끔하게 분리 적용되는지 확인 후 커밋.
- `git status`가 4개 커밋 이후 완전히 clean한지 확인(누락된 미추적/미커밋
  파일 없음).

이 중 어느 것도 "실제로 컴파일되고 Windows에서 정상 동작한다"는 것을
증명하지는 못한다. §13에서 요청하신 20개 검증 항목은 아래 Human QA
체크리스트로 남긴다.

## 10. 남은 Known Issues / 투명하게 보고할 사항

- **Fullscreen 앱에서의 Always-On-Top 억제**: 요청하신 대로 이번
  범위에서 구현하지 않음. 별도 Known Issue로 남겨둔다.
- **글꼴 선택 드롭다운의 한글 병기**: `note.html`의 `<option>` 값들
  (예: "Malgun Gothic (맑은 고딕)", "Gowun Dodum (고운돋움)")은 이전
  분석 보고서에서 "검토 필요"로 표시했던 항목으로, 이번에도 의도적으로
  손대지 않았다. 폰트 이름 자체는 어차피 번역 대상이 아니고, 괄호 안
  한글 설명만 다국어화하려면 별도 작업 판단이 필요해 범위 확장을 피했다.
- **`ui/dialog.html`**: 이전 분석 보고서에서 확인한 대로 어디서도
  참조되지 않는 미사용 레거시 파일. 이번에도 손대지 않았다.
- **`ui/lang/ko.json`의 `deck.drawer.title` 값 수정**: 이전 단계에서
  생성한 초기 값이 `"🗂️ 메모 서랍 (Drawer)"`로 되어 있어 en/ja
  값("All Notes"/"メモ一覧", 이모지 없음)과 형식이 어긋나 있었다. 이번에
  프론트엔드를 연결하면서 이모지는 HTML 쪽 정적 장식 요소로 분리하고
  ko.json 값 자체는 `"전체 메모"`로 정리했다. 사용자가 입력한 값이
  아니라 이전에 내가 생성한 초안이므로 임의로 정리했지만, 명시적으로
  보고한다.
- **자동 시작 트레이 라벨 즉시 갱신**: 트레이에서 자동 시작을 토글하면
  `[OFF]`/`[✓ ON]` 라벨을 그 자리에서 바로 갱신하도록 `apply_tray_texts`를
  추가로 호출했다. 요청 명세에는 없던 작은 개선이라 투명하게 보고한다.
- **`document.documentElement.lang` 갱신**: 각 창에서 로케일 확정/변경
  시 `<html lang>` 속성을 실제 로케일로 갱신하는 코드를 접근성 차원에서
  추가했다. 이 역시 명세 외 작은 추가라 보고한다.
- **이 세션은 Release 빌드와 GUI 실행 검증을 직접 수행할 수 없음**
  (§8 참고). 이는 이번 작업의 가장 중요한 한계이므로 최상단에 다시
  강조한다.

## 11. Human QA 체크리스트

아래 항목은 실제 Windows 머신에서 `cargo build --release`로 빌드한 뒤
사람이 직접 확인해야 한다.

1. 기존 DB가 정상적으로 로드되는가 (기존 사용자 데이터로 실행)
2. 기존 메모들이 정상적으로 표시되는가
3. 새 메모 생성이 정상 동작하는가
4. 메모 편집/저장(자동저장)이 정상 동작하는가
5. 보관(Archive)/복원(Restore)이 정상 동작하는가
6. 삭제 확인 모달이 뜨고, 확인 시 실제로 삭제되는가
7. Deck의 Dormant/Fan/Hover Preview/Drawer 네 가지 상태 전환이 모두
   정상 동작하는가
8. Dormant 상태(16px 스트라이프)에서 Deck이 항상 다른 창 위에 떠 있는가
   (다른 창으로 포커스를 옮겨도 가라앉지 않는지)
9. Deck 뒤쪽의 투명 영역을 클릭/스크롤했을 때 그 입력이 실제로 뒤에 있는
   다른 앱에 정상적으로 전달되는가 (16px 영역 바깥)
10. 트레이 메뉴의 모든 항목이 정상 동작하는가 (새 메모/덱 토글/가져오기/
    자동시작/설정/정보/종료)
11. About 창이 잘림 없이 정상적으로 표시되는가
12. Settings 창이 정상적으로 열리고 언어/자동시작 항목이 표시되는가
13. 한국어 로케일에서 전체 UI(Deck/노트/About/Settings/트레이) 표시 확인
14. 영어 로케일에서 전체 UI 표시 확인 (특히 About/Drawer/Settings의
    텍스트 잘림 여부)
15. 일본어 로케일에서 전체 UI 표시 확인 (동일 관점)
16. "시스템 기본값" 선택 시 실제 Windows 표시 언어를 따라가는가
17. 지원하지 않는 Windows 표시 언어(예: 중국어, 프랑스어)에서 영어로
    정상 폴백되는가
18. Settings에서 언어를 변경했을 때, 이미 열려 있는 Deck/Floating
    Note/About/트레이 메뉴가 재시작 없이 즉시 갱신되는가
19. 언어 선택이 앱 종료 후 재실행에도 유지되는가
20. Release 빌드된 exe가 정상적으로 실행되는가 (경고/크래시 없이)
