# StickerMemo UI 다국어(Localization/i18n) 사전 분석 보고서

- **분석 대상**: `R:\AntiGravity Working\Sticker-Memo` 중 현재 활성 코드베이스인 `src-tauri/`(Rust/Tauri 백엔드) + `ui/`(Vanilla HTML/CSS/JS 프런트엔드)
- **분석 방식**: 코드 정적 분석(읽기 전용). **이 보고서 작성 과정에서 어떤 파일도 수정·생성(번역 파일 포함)하지 않았습니다.**
- **작성일**: 2026-09-17
- **목표 언어**: 한국어(ko) / English(en) / 日本語(ja), 향후 확장 가능한 구조 전제
- **참고 문서**: `docs/current-architecture.md`(구 WPF 버전 분석), `docs/rust-rebuild-plan.md`(Rust 리빌드 설계서), `docs/full-source-analysis-report.md`(Rust/Tauri 코드 품질 분석)

> 참고: 리포지토리 루트에는 과거 .NET/WPF 버전의 잔존 파일(`App.xaml`, `Models/`, `Services/`, `Views/` 등)이 함께 존재하지만, 본 보고서는 `docs/full-source-analysis-report.md`와 동일하게 **현재 활성 코드베이스(`src-tauri/`, `ui/`)만을 대상**으로 합니다.

---

## 0. 요약 (Executive Summary)

| 항목 | 내용 |
| --- | --- |
| 발견된 사용자 노출 문자열 | 약 **58개** (동일 개념 중복 표현 통합 기준) + 미사용 레거시 파일(`ui/dialog.html`) 5개 별도 |
| 하드코딩 위치 | `ui/*.html`, `ui/*.js` (프런트엔드) 및 `src-tauri/src/main.rs`, `commands/mod.rs`, `domain/note.rs`, `importer/sticky_notes.rs`, `platform/autostart.rs` (Rust 백엔드) |
| 추천 아키텍처 | JSON 기반 리소스 파일(`ui/lang/{locale}.json`, flat key) + Rust `include_str!` 임베딩으로 **Single Source of Truth** 구성 |
| 신규 언어 추가 난이도 | 리소스 파일 1개 추가 + 지원 언어 목록에 1줄 등록만으로 가능하도록 설계 (기존 로직 무수정) |
| 가장 시급한 레이아웃 리스크 | **About 창** — 고정 크기(320×310, `resizable:false`) + 하드코딩된 `<br>` 줄바꿈 위치. 사용자가 이미 겪은 "번역 문자열 때문에 클라이언트 영역 밖으로 잘리는" 문제의 재발 가능성이 가장 높은 지점 |
| DB 호환성 영향 | 없음 (Notes 테이블 무변경 전제. 신규 `Settings` 테이블은 순수 추가형) |
| 구현 난이도 | 전체 Medium — 프런트엔드 문자열 치환은 기계적 작업, Rust↔WebView locale 동기화와 언어 변경 UX가 상대적으로 신규 설계 영역 |

---

## 1. 현지화 대상 문자열 전수 조사

### 1.1 조사 범위 및 방법

`src-tauri/src/**/*.rs` 전체와 `ui/**/*.{html,js,css}` 전체를 직접 읽고, 한글 유니코드 범위(`\uAC00-\uD7A3`) 정규식 스캔으로 교차 검증했습니다. 문자열이 발견된 파일은 다음 11개로, 아래 표의 항목과 1:1로 대응합니다.

```
ui/about.html, ui/deck.html, ui/deck.js, ui/delete-confirm.js, ui/note.html, ui/note.js
src-tauri/src/main.rs, src-tauri/src/commands/mod.rs, src-tauri/src/domain/note.rs,
src-tauri/src/importer/sticky_notes.rs, src-tauri/src/platform/autostart.rs
```

`ui/dialog.html`은 이미 하드코딩된 영어 문자열("Delete this note?", "Cancel" 등)을 담고 있지만, `docs/full-source-analysis-report.md`가 밝힌 대로 **현재 어떤 창도 이 파일을 로드하지 않는 죽은 코드**입니다(실제 삭제 확인 UI는 `delete-confirm.js`가 각 창에 동적 주입). §1.9에서 별도로 다룹니다.

**번역 대상 제외 기준**: `console.error(...)`(개발자 콘솔 전용), `log_startup`/`log_front`를 통해 `%TEMP%\StickerMemo-rust.log`에만 기록되는 문자열(예: `"Existing instance detected..."`, `"Deck window positioned at..."`)은 사용자에게 절대 노출되지 않으므로 번역 대상에서 제외했습니다. 이 로그 문자열들은 원본 그대로(영어)를 유지할 것을 권장합니다.

### 1.2 Edge Deck (`ui/deck.html`, `ui/deck.js`)

| # | 현재 한국어 문자열 | 사용 위치 | 파일/코드 | 용도 | 번역 필요 | 제안 locale key | English | 日本語 |
| - | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 🗂️ 메모 서랍 (Drawer) | 서랍 패널 헤더 제목 | deck.html L16 | 제목 | ✅ | `deck.drawer.title` | All Notes | メモ一覧 |
| 2 | 닫기 (ESC) | 서랍 닫기 버튼 tooltip | deck.html L17 | Tooltip | ✅ | `deck.drawer.close_tooltip` | Close (Esc) | 閉じる (Esc) |
| 3 | Active | 세그먼트 탭 | deck.html L21 (이미 영문) | 버튼 | ✅(용어 통일용) | `deck.drawer.tab_active` | Active | アクティブ |
| 4 | Archived | 세그먼트 탭 | deck.html L22 (이미 영문) | 버튼 | ✅ | `deck.drawer.tab_archived` | Archived | アーカイブ |
| 5 | 메모 검색... | 검색창 placeholder | deck.html L27 | Placeholder | ✅ | `deck.drawer.search_placeholder` | Search notes… | メモを検索… |
| 6 | 총 0개의 메모 | 서랍 footer 초기값 | deck.html L33 | 상태 메시지 | ✅ | `deck.drawer.footer_default` | 0 notes total | メモ 合計0件 |
| 7 | 전체 메모 및 보관함 열기 | +N more 버튼 tooltip | deck.html L41 | Tooltip | ✅ | `deck.more_notes_tooltip` | Open all notes & archive | すべてのメモとアーカイブを開く |
| 8 | +0 more / `+${n} more` | +N more 버튼 텍스트 | deck.html L43, deck.js L187 | 상태 메시지 | ✅(형식 유지, 영어 관용구) | `deck.more_count` | +{n} more | +{n}件 |
| 9 | 새 메모 추가 (+) | 새 메모 버튼 tooltip | deck.html L47 | Tooltip | ✅ | `deck.add_note_tooltip` | Add new note (+) | 新しいメモを追加 (+) |
| 10 | 새 메모 | 새 메모 버튼 라벨 | deck.html L49 | 버튼 | ✅ | `deck.add_note_label` | New Note | 新規メモ |
| 11 | `총 ${n}개의 보관된 메모 중 ${m}개 표시` | 서랍 footer(보관함 탭) | deck.js L262-264 | 상태 메시지 | ✅ | `deck.drawer.footer_archived` | Showing {m} of {n} archived notes | アーカイブ済みメモ {n}件中 {m}件を表示 |
| 12 | `총 ${n}개의 활성 메모 중 ${m}개 표시` | 서랍 footer(활성 탭) | deck.js L262-264 | 상태 메시지 | ✅ | `deck.drawer.footer_active` | Showing {m} of {n} active notes | アクティブなメモ {n}件中 {m}件を表示 |
| 13 | 보관된 메모가 없습니다. | 서랍 빈 상태(보관함) | deck.js L272 | 빈 상태 문구 | ✅ | `deck.drawer.empty_archived` | No archived notes. | アーカイブされたメモはありません。 |
| 14 | 표시할 활성 메모가 없습니다. | 서랍 빈 상태(활성) | deck.js L272 | 빈 상태 문구 | ✅ | `deck.drawer.empty_active` | No active notes to show. | 表示できるアクティブなメモがありません。 |
| 15 | 보관 해제 및 활성 메모로 복원 | 서랍 항목 복원 버튼 tooltip | deck.js L288 | Tooltip | ✅ | `deck.drawer.restore_tooltip` | Restore to active notes | アクティブなメモに復元 |
| 16 | 메모 영구 삭제 | 서랍 항목 삭제 버튼 tooltip | deck.js L289 | Tooltip | ✅ | `common.delete_permanently_tooltip` | Delete permanently | 完全に削除 |
| 17 | `StickerMemo Deck` | 창 title 태그 | deck.html L5 | 창 제목(비가시) | ⛔ 낮음 | `window.deck_title` | StickerMemo | StickerMemo |

> 17번은 `decorations:false` + `skipTaskbar:true`로 실제 타이틀바/작업표시줄에 노출되지 않지만, 접근성 도구(스크린리더 등)가 창 제목을 조회할 가능성이 있어 낮은 우선순위로 포함했습니다.

### 1.3 Floating Note (`ui/note.html`, `ui/note.js`)

| # | 현재 한국어 문자열 | 사용 위치 | 파일/코드 | 용도 | 번역 필요 | 제안 locale key | English | 日本語 |
| - | --- | --- | --- | --- | --- | --- | --- | --- |
| 18 | 글꼴 및 텍스트 서식 (Font) | 🔤 버튼 tooltip | note.html L18 | Tooltip | ✅ | `note.font_tooltip` | Font & text style | フォントと書式 |
| 19 | 테마 색상 변경 | 🎨 버튼 tooltip | note.html L19 | Tooltip | ✅ | `note.color_tooltip` | Change note color | テーマカラーを変更 |
| 20 | 보관함으로 이동 (Archive) | 📦 버튼 tooltip | note.html L20 | Tooltip | ✅ | `note.archive_tooltip` | Move to archive | アーカイブに移動 |
| 21 | 메모 영구 삭제 | 🗑️ 버튼 tooltip | note.html L21 | Tooltip | ✅ | `common.delete_permanently_tooltip`(#16 재사용) | Delete permanently | 完全に削除 |
| 22 | 닫기 및 Deck으로 복귀 (✕) | ✕ 버튼 tooltip | note.html L22 | Tooltip | ✅ | `note.close_tooltip` | Close and dock to Deck | 閉じてDeckに戻す |
| 23 | 서식 (Aa 옆 라벨) | 서식 팝업 헤더 | note.html L40 | 라벨 | ✅ | `note.format_label` | Format | 書式 |
| 24 | 글자 크기 축소 | `−` 버튼 tooltip | note.html L61 | Tooltip | ✅ | `note.decrease_size_tooltip` | Decrease font size | 文字サイズを縮小 |
| 25 | 글자 크기 확대 | `+` 버튼 tooltip | note.html L63 | Tooltip | ✅ | `note.increase_size_tooltip` | Increase font size | 文字サイズを拡大 |
| 26 | 굵게 (Bold) | Bold 칩 tooltip | note.html L66 | Tooltip | ✅ | `note.bold_tooltip` | Bold | 太字 |
| 27 | 굵게 | Bold 칩 텍스트 | note.html L67 | 버튼 라벨 | ✅ | `note.bold_label` | Bold | 太字 |
| 28 | 제목 없는 메모 | 제목 입력창 placeholder | note.html L74 | Placeholder | ✅ | `note.title_placeholder` | Untitled note | 無題のメモ |
| 29 | 내용을 입력하세요... | 본문 textarea placeholder | note.html L79 | Placeholder | ✅ | `note.content_placeholder` | Type your note… | 内容を入力してください… |
| 30 | 방금 수정됨 | 타임스탬프 초기/기본값 | note.html L84, note.js L127 | 상태 메시지 | ✅ | `note.timestamp_just_updated` | Just updated | たった今更新 |
| 31 | 크기 조절 | 리사이즈 그립 tooltip | note.html L85 | Tooltip | ✅ | `note.resize_tooltip` | Resize | サイズ変更 |
| 32 | `수정: ${m}/${d} ${h}:${min}` | 타임스탬프 동적 텍스트 | note.js L118-124 | 상태 메시지 | ✅ | `note.timestamp_updated_at` | Updated {m}/{d} {h}:{min} | 更新: {m}/{d} {h}:{min} |
| 33 | STICKER NOTE | 테이프 라벨 초기값(로드 전 짧게 노출) | note.html L11 | 라벨(과도기적) | ⛔ 낮음 | — | STICKER NOTE | STICKER NOTE |
| 34 | Yellow/Pink/Mint/Blue/Purple/Kraft | 색상 점 tooltip | note.html L27-32 (이미 영문) | Tooltip | ⚠️ 검토 필요(§2, §7) | `theme.<id>.display_name` | (그대로 유지 권장) | (그대로 유지 권장) |
| 35 | `Malgun Gothic (맑은 고딕)` 등 폰트명 | 글꼴 선택 드롭다운 옵션 6종 | note.html L47-53 | 옵션 라벨 | ⚠️ 검토 필요(§7) | — (폰트 실제 이름, 번역 대상 아님) | 로케일별 괄호 표기 재검토 | 로케일별 괄호 표기 재검토 |
| 36 | `StickerMemo` | 창 title 태그(초기값, Rust가 즉시 덮어씀) | note.html L5 | 창 제목(과도기적) | ⛔ 낮음 | — | — | — |

> 33, 36번은 JS 초기화가 완료되기 전 극히 짧은 순간에만 보이거나 Rust가 즉시 재설정하는 값이라 우선순위는 낮지만, "하드코딩된 한국어 없음"을 100% 보장하려면 함께 정리하는 것이 깔끔합니다. 34·35번은 §2(용어집)·§7(레이아웃)에서 별도로 다룹니다.

### 1.4 About 창 (`ui/about.html`)

| # | 현재 문자열 | 사용 위치 | 파일/코드 | 용도 | 번역 필요 | 제안 locale key | English | 日本語 |
| - | --- | --- | --- | --- | --- | --- | --- | --- |
| 37 | StickerMemo 정보 | HTML `<title>` | about.html | 창 제목(비가시, HTML단) | ⛔ 낮음(중복) | `about.window_title` | About StickerMemo | StickerMemo について |
| 38 | StickerMemo 정보 | 실제 OS 창 제목 (Rust `.title()`) | main.rs L220 | **창 제목(실제 노출)** | ✅ | `about.window_title`(37과 통합) | About StickerMemo | StickerMemo について |
| 39 | Version 1.0.0 | 버전 표시 | about.html | 상태 정보 | ✅(라벨만) | `about.version` | Version {v} | バージョン {v} |
| 40 | Rust Edition | 태그라인 | about.html | 상태 정보 | ⚠️ (§2 참고) | `about.edition_tag` | Rust Edition | Rust版 |
| 41 | Build: 2026-09-17 | 빌드 날짜 | about.html | 상태 정보 | ✅(라벨만) | `about.build_label` | Build: {date} | ビルド: {date} |
| 42 | Lightweight Desktop Sticky Notes | 태그라인 | about.html (이미 영문) | 상태 정보 | ✅ | `about.tagline` | Lightweight Desktop Sticky Notes | 軽量デスクトップ付箋アプリ |
| 43 | Originally built with .NET/WPF / Rebuilt with Rust + Tauri | 크레딧(작은 글씨, `<br>` 강제 줄바꿈 포함) | about.html | 상태 정보 | ✅ | `about.credits` | Originally built with .NET/WPF, rebuilt with Rust + Tauri | 元々 .NET/WPF で開発し、Rust + Tauri で再構築 |
| 44 | 닫기 | 닫기 버튼 | about.html | 버튼 | ✅ | `common.close` | Close | 閉じる |

> **38번이 §7 레이아웃 리스크의 핵심 항목**입니다 — About 창은 `inner_size(320,310)` + `resizable(false)`로 고정되어 있고, 43번 크레딧 텍스트는 영어 줄 길이에 맞춰 `<br>`로 수동 줄바꿈되어 있습니다.

### 1.5 Tray Menu (`src-tauri/src/main.rs`)

Rust가 `tauri::menu::MenuItem`으로 직접 생성하는, **Rust에서만 존재하고 웹뷰를 거치지 않는** 문자열입니다.

| # | 현재 한국어 문자열 | 사용 위치 | 파일/코드 | 용도 | 번역 필요 | 제안 locale key | English | 日本語 |
| - | --- | --- | --- | --- | --- | --- | --- | --- |
| 45 | ➕ 새 메모 | 트레이 메뉴 항목 | main.rs L165 | 메뉴 | ✅ | `tray.new_note` | New Note | 新規メモ |
| 46 | 🗂️ 덱 보이기 / 숨기기 | 트레이 메뉴 항목 | main.rs L166 | 메뉴 | ✅ | `tray.toggle_deck` | Show / Hide Deck | デックを表示 / 非表示 |
| 47 | 📥 Sticky Notes 가져오기 | 트레이 메뉴 항목 | main.rs L167 | 메뉴 | ✅ | `tray.import_sticky_notes` | Import Sticky Notes | Sticky Notes をインポート |
| 48 | 🚀 Windows 시작 시 실행 [✓ ON] | 트레이 메뉴 항목(자동시작 ON) | main.rs L172 | 메뉴(동적) | ✅ | `tray.autostart_on` | Start with Windows [✓ ON] | Windows 起動時に実行 [✓ ON] |
| 49 | 🚀 Windows 시작 시 실행 [OFF] | 트레이 메뉴 항목(자동시작 OFF) | main.rs L174 | 메뉴(동적) | ✅ | `tray.autostart_off` | Start with Windows [OFF] | Windows 起動時に実行 [OFF] |
| 50 | 🚪 StickerMemo 종료 | 트레이 메뉴 항목 | main.rs L179 | 메뉴 | ✅ | `tray.quit` | Quit StickerMemo | StickerMemo を終了 |
| 51 | StickerMemo 정보... | 트레이 메뉴 항목 | main.rs L180 | 메뉴 | ✅ | `tray.about` | About StickerMemo… | StickerMemo について... |

> 트레이 아이콘 자체의 `.tooltip("StickerMemo")`(main.rs L198)는 제품명 고유명사이므로 번역 대상에서 제외했습니다.

### 1.6 삭제 확인 모달 (`ui/delete-confirm.js`, deck/note 공용)

| # | 현재 한국어 문자열 | 사용 위치 | 파일/코드 | 용도 | 번역 필요 | 제안 locale key | English | 日本語 |
| - | --- | --- | --- | --- | --- | --- | --- | --- |
| 52 | 이 메모를 영구 삭제하시겠습니까? | 모달 제목 | delete-confirm.js L11 | 확인 문구 | ✅ | `delete_confirm.title` | Delete this note permanently? | このメモを完全に削除しますか? |
| 53 | 이 작업은 되돌릴 수 없습니다. | 모달 설명 | delete-confirm.js L12 | 경고 문구 | ✅ | `delete_confirm.description` | This action can't be undone. | この操作は元に戻せません。 |
| 54 | 삭제하지 못했습니다. 다시 시도해 주세요. | 삭제 실패 시 인라인 오류 | delete-confirm.js L13 | 오류 메시지 | ✅ | `delete_confirm.error` | Couldn't delete the note. Please try again. | 削除できませんでした。もう一度お試しください。 |
| 55 | 취소 | 취소 버튼 | delete-confirm.js L15 | 버튼 | ✅ | `common.cancel` | Cancel | キャンセル |
| 56 | 삭제 | 삭제 버튼 | delete-confirm.js L16 | 버튼 | ✅ | `common.delete` | Delete | 削除 |

### 1.7 Import 관련 UI 문구 (`src-tauri/src/importer/sticky_notes.rs`)

Rust `ImportResult.message`로 반환되지만, **현재 프런트엔드 어디에서도 이 메시지를 화면에 표시하지 않습니다**(§8·§9에서 재언급). 트레이 메뉴의 "가져오기" 핸들러(main.rs L209-211)도 결과를 `let _ = ...`로 버립니다. 즉 **아직 사용자에게 도달하지 않는 미래 대비 문자열**이지만, 문구 자체가 사용자 노출을 전제로 작성되어 있으므로 번역 대상에 포함합니다.

| # | 현재 한국어 문자열 | 사용 위치 | 파일/코드 | 용도 | 번역 필요 | 제안 locale key | English | 日本語 |
| - | --- | --- | --- | --- | --- | --- | --- | --- |
| 57 | Windows Sticky Notes 데이터를 찾을 수 없습니다. | 원본 DB 없음 | sticky_notes.rs L41 | 오류 메시지 | ✅ | `importer.source_not_found` | Couldn't find Windows Sticky Notes data. | Windows Sticky Notes のデータが見つかりませんでした。 |
| 58 | `임시 데이터베이스 복사 실패: {e}` | 임시 파일 복사 실패 | sticky_notes.rs L56 | 오류 메시지 | ✅ | `importer.temp_copy_failed` | Failed to copy the temporary database: {e} | 一時データベースのコピーに失敗しました: {e} |
| 59 | `Windows Sticky Notes에서 {n}개의 메모를 가져왔습니다. ({m}개 기존 메모 건너뜀)` | 가져오기 성공(중복 있음) | sticky_notes.rs L171-172 | 상태 메시지 | ✅ | `importer.imported_with_skipped` | Imported {n} notes from Windows Sticky Notes. ({m} already imported, skipped) | Windows Sticky Notes から{n}件のメモをインポートしました。(既存の{m}件をスキップ) |
| 60 | `Windows Sticky Notes에서 {n}개의 메모를 가져왔습니다.` | 가져오기 성공 | sticky_notes.rs L176-177 | 상태 메시지 | ✅ | `importer.imported` | Imported {n} notes from Windows Sticky Notes. | Windows Sticky Notes から{n}件のメモをインポートしました。 |
| 61 | `Sticky Notes 가져오기 실패: {e}` | 가져오기 실패(기타 오류) | sticky_notes.rs L192 | 오류 메시지 | ✅ | `importer.import_failed` | Failed to import Sticky Notes: {e} | Sticky Notes のインポートに失敗しました: {e} |

### 1.8 자동 시작 / 설정 관련 오류 메시지 (`src-tauri/src/platform/autostart.rs`)

`set_run_at_startup()`은 트레이 메뉴 핸들러(main.rs L212-215, 결과 버림)와 커맨드 `set_autostart_setting`(현재 프런트엔드에서 호출되는 곳 없음, §8·§9 참고)에서 쓰입니다. 마찬가지로 아직 사용자에게 실제로 도달하지 않지만 번역 준비 대상입니다.

| # | 현재 한국어 문자열 | 사용 위치 | 파일/코드 | 용도 | 번역 필요 | 제안 locale key | English | 日本語 |
| - | --- | --- | --- | --- | --- | --- | --- | --- |
| 62 | `레지스트리 키 열기 실패: {e}` | 자동시작 설정 중 오류 | autostart.rs L21 | 오류 메시지 | ✅ | `error.registry_open_failed` | Failed to open the registry key: {e} | レジストリキーを開けませんでした: {e} |
| 63 | `실행 파일 경로 확인 실패: {e}` | 자동시작 설정 중 오류 | autostart.rs L25 | 오류 메시지 | ✅ | `error.exe_path_failed` | Failed to resolve the executable path: {e} | 実行ファイルのパスを取得できませんでした: {e} |
| 64 | `자동 실행 값 설정 실패: {e}` | 자동시작 설정 중 오류 | autostart.rs L29 | 오류 메시지 | ✅ | `error.autostart_set_failed` | Failed to set the startup registry value: {e} | 自動起動の設定に失敗しました: {e} |

### 1.9 Rust에서 직접 생성하는 그 외 사용자 노출 문자열 (`commands/mod.rs`, `domain/note.rs`)

| # | 현재 한국어 문자열 | 사용 위치 | 파일/코드 | 용도 | 번역 필요 | 제안 locale key | English | 日本語 |
| - | --- | --- | --- | --- | --- | --- | --- | --- |
| 65 | 메모 (새 메모) | 플로팅 노트 창 제목(제목 없을 때) | commands/mod.rs L77-81, L211-215 | **창 제목(실제 노출)** | ✅ | `note.window_title_untitled` | Note (Untitled) | メモ(無題) |
| 66 | `메모 - {title}` | 플로팅 노트 창 제목(제목 있을 때) | commands/mod.rs L77-81, L211-215 | **창 제목(실제 노출)** | ✅ | `note.window_title_with_title` | Note - {title} | メモ - {title} |
| 67 | `메모를 찾을 수 없습니다: {id}` | `open_floating_note` 실패 시 반환 오류 | commands/mod.rs L195-198 | 오류 메시지(내부, §8 참고) | ⚠️ 낮음(현재 UI 미표시) | `error.note_not_found` | Note not found: {id} | メモが見つかりません: {id} |
| 68 | 새 메모 | 빈 메모의 기본 표시 제목 | domain/note.rs L82,118,121 | **빈 상태 문구** | ✅ (단, §2 참고 — "New Note" 버튼과 뜻이 갈릴 수 있음) | `note.default_title` | Untitled | 無題 |
| 69 | (내용이 비어 있습니다) | 호버 미리보기 빈 상태 | domain/note.rs L84,144 | 빈 상태 문구 | ✅ | `note.empty_preview` | (No content yet) | (内容がありません) |
| 70 | 반가워요! | 최초 실행 시 생성되는 웰컴 노트 제목 | commands/mod.rs L28 | **시드 콘텐츠**(§8 참고) | ⚠️ 특수 케이스 | `seed.welcome_title` | Welcome! | ようこそ! |
| 71 | `📌 StickerMemo Edge Deck입니다. ...` (온보딩 4문단) | 최초 실행 시 웰컴 노트 본문 | commands/mod.rs L29 | **시드 콘텐츠**(§8 참고) | ⚠️ 특수 케이스 | `seed.welcome_body` | (아래 §8 참고, 4문단 번역 필요) | (아래 §8 참고) |

### 1.10 `tauri.conf.json` 정적 창 제목

| # | 현재 문자열 | 사용 위치 | 파일/코드 | 용도 | 번역 필요 |
| - | --- | --- | --- | --- | --- |
| 72 | `"title": "StickerMemo"` | `deck` 창 정적 타이틀 | tauri.conf.json L14 | 창 제목(비가시) | ⛔ 고유명사, 번역 불필요 |

### 1.11 (참고, 번역 대상 제외) 미사용 레거시 — `ui/dialog.html`

현재 어떤 Rust 코드도 `dialog.html`을 `WebviewUrl`로 참조하지 않고(`tauri.conf.json`의 `windows` 배열에도, `commands/mod.rs`/`main.rs`의 동적 `WebviewWindowBuilder`에도 없음), `ui/*.js`에서도 로드하지 않습니다. 흥미롭게도 이 파일만 유일하게 **하드코딩된 영어**("StickerMemo Confirm", "Delete this note?", "This action cannot be undone.", "Cancel", "Delete")를 담고 있어, 프로젝트 초기 버전의 흔적으로 보입니다. i18n 작업 범위에서는 제외하되, 코드 정리(삭제) 후보로 별도 이슈화할 것을 권장합니다.

---

## 2. 용어집 (Glossary)

동일 개념이 화면마다 다른 표현으로 번역되지 않도록 기준 용어를 고정합니다. 일본어는 영어를 그대로 옮기지 않고, Windows 데스크톱 앱에서 자연스러운 표현을 선택했습니다.

| 한국어 | English | 日本語 | 비고 |
| --- | --- | --- | --- |
| 메모 | Note | メモ | 앱 전체의 핵심 명사, `note.*` 네임스페이스 기준 |
| 새 메모(액션) | New Note | 新規メモ | 버튼/메뉴의 "만들기" 행위 표기 |
| (빈 메모의 기본 제목) | Untitled | 無題 | "New Note"와 문자열은 같아도 **의미가 다른 두 번째 keyword**로 분리 권장(아래 참고) |
| 보관(동사) | Archive | アーカイブ(に移動) | 플로팅 노트 툴바의 동작 |
| 보관함(명사, 서랍) | All Notes | メモ一覧 | 개발 문서상의 "Drawer"는 내부 용어로만 사용, 사용자 노출 텍스트는 "전체 메모" 개념으로 통일 |
| 보관됨(상태) | Archived | アーカイブ済み | 서랍 세그먼트 탭 |
| 활성(상태) | Active | アクティブ | 서랍 세그먼트 탭 |
| 삭제 | Delete | 削除 | 일반 삭제 |
| 영구 삭제 | Delete Permanently | 完全に削除 | 되돌릴 수 없는 삭제임을 명시 |
| 복원 | Restore | 復元 | 보관함 → 활성 |
| 닫기 | Close | 閉じる | |
| 취소 | Cancel | キャンセル | |
| 검색 | Search | 検索 | |
| 서식 | Format | 書式 | |
| 굵게 | Bold | 太字 | |
| 설정 | Settings | 設定 | **현재 앱에 설정 화면 자체가 없음**(§5, §9 Phase 5에서 신설 예정) |
| 가져오기 | Import | インポート | 고유명사(Sticky Notes)와 결합 시 어순: "Sticky Notes 가져오기" → "Import Sticky Notes" → "Sticky Notes をインポート" |
| 정보(About) | About | 〜について | 창 제목은 "StickerMemo について" 형태(일본어 Windows 앱의 일반적 "About" 관례) |
| 종료 | Quit | 終了 | 트레이 메뉴는 "Quit {앱이름}" 관례(Windows/macOS 공통 트레이 앱 컨벤션) 채택 |
| 자동 시작 | Start with Windows | Windows 起動時に実行 | Windows 설정 앱이 실제로 쓰는 문구("スタートアップ" 앱 목록의 관용 표현)를 따름 |
| 덱(Deck) | Deck | Deck | 제품 고유 UI 개념명 — 3개 언어 모두 번역하지 않고 고유명사로 유지 권장(트레이 메뉴의 "덱 보이기/숨기기"만 "Show/Hide Deck"으로 동사만 번역) |
| 테마 색상 이름 (Yellow/Pink/Mint/Blue/Purple/Kraft) | 그대로 유지 | 그대로 유지 | §7에서 상세 검토 — 색상 고유명은 로케일 전환 없이 영어 유지를 권장(색상 국역 시 어색함, 예: "노랑" tooltip은 부자연스러움). DB의 `ThemeId` 저장값과도 동일해야 하므로 **표시 전용 레이어에서만** 다국어 라벨을 씌우거나, 아예 번역하지 않는 두 가지 옵션 중 결정 필요 |

> **"새 메모" 이중 의미 이슈**: 한국어에서는 "새 메모"라는 동일 문자열이 (a) 덱 하단의 "새 메모 추가" 버튼 라벨과 (b) 제목·본문이 모두 빈 메모의 기본 표시 제목(탭/서랍에 나타나는 이름)이라는 **두 가지 다른 의미**로 재사용되고 있습니다(`ui/deck.html`의 `add-label`과 `domain/note.rs`의 `display_main_title` 기본값). 영어/일본어에서는 이 둘이 자연스럽게 다른 단어("New Note" 버튼 vs. "Untitled" 탭 이름)가 되므로, **locale key 설계 단계에서 반드시 두 개의 독립된 key(`deck.add_note_label` / `note.default_title`)로 분리**해야 합니다. 한국어 번역만 우연히 동일한 문자열을 공유해도 무방합니다.

---

## 3. i18n 구조 설계

### 3.1 제약 조건 재확인

- `ui/`는 번들러/빌드 스텝이 없는 **순수 정적 HTML/CSS/JS**입니다(`package.json` 없음, `tauri.conf.json`의 `frontendDist`가 `../ui`를 그대로 가리킴). 따라서 JS `import`/`export` 모듈 문법에 의존하는 구조보다, 브라우저가 그대로 실행 가능한 형태가 안전합니다.
- Tauri v2 WebView(Windows에서는 WebView2/Chromium 기반)는 `fetch()`로 같은 오리진의 정적 리소스(`ui/lang/*.json`)를 문제없이 읽을 수 있습니다.
- Rust 쪽(트레이 메뉴, 창 제목, 일부 오류 메시지)도 동일한 번역 데이터가 필요합니다 → **Single Source of Truth** 요구사항이 핵심 설계 축입니다.

### 3.2 채택안: JSON 리소스 파일 + 얇은 로더

```
ui/
  lang/
    ko.json
    en.json
    ja.json
  i18n.js              # 로더: fetch, 캐시, t(key, params), applyI18n(root)
src-tauri/
  src/
    i18n.rs            # ko.json/en.json/ja.json을 컴파일 타임에 include_str!로 임베딩
                        # → serde_json으로 파싱해 OnceLock<HashMap<&str, HashMap<String,String>>> 캐싱
                        # → Rust 커맨드/트레이 메뉴/창 제목에서 t(locale, key) 형태로 사용
```

`ko.json`/`en.json`/`ja.json` 파일 3개가 **JS와 Rust 양쪽에서 동시에 참조되는 유일한 번역 데이터**가 됩니다(§6에서 구체적 메커니즘 설명). `ui/lang/*.json`은 `tauri.conf.json`의 `frontendDist`(`../ui`) 하위에 있으므로 앱 번들에 자동 포함되고, Rust는 `include_str!("../../ui/lang/ko.json")`처럼 빌드 시점에 파일 내용을 바이너리에 박아 넣어 런타임 파일 경로 문제(설치 후 리소스 디렉터리 위치 이슈 등)를 원천적으로 피합니다.

### 3.3 JSON 파일 형식 — Flat Key (평면 키)

```json
{
  "common.close": "닫기",
  "common.cancel": "취소",
  "common.delete": "삭제",
  "deck.drawer.title": "🗂️ 메모 서랍 (Drawer)",
  "note.window_title_with_title": "메모 - {title}"
}
```

중첩 JSON 객체(`{"common": {"close": "..."}}`) 대신 **평면(dot-path) 키**를 채택하는 이유:

1. Rust 쪽 `HashMap<String, String>` 파싱이 단순해짐(중첩 구조 파싱/순회 로직 불필요).
2. JS/Rust 양쪽에서 동일한 키 문자열(`"note.window_title_with_title"`)을 그대로 복사해 쓸 수 있어 오타·불일치 위험이 줄어듦.
3. §4에서 다루는 namespace.key 설계와 파일 포맷이 1:1로 대응되어 가독성이 좋음.

### 3.4 새 언어 추가 절차 (요구사항 검증)

향후 `fr`(프랑스어), `de`(독일어), `zh-CN`, `zh-TW` 등을 추가할 때 필요한 작업은 정확히 2가지로 제한됩니다:

1. `ui/lang/fr.json` (또는 `de.json`, `zh-CN.json` 등) 신규 리소스 파일 추가 — 기존 `en.json`을 템플릿으로 복사해 값만 번역.
2. 지원 언어 목록에 1줄 등록:
   - `ui/i18n.js`의 `SUPPORTED_LOCALES = ["ko", "en", "ja"]` 배열에 `"fr"` 추가.
   - `src-tauri/src/i18n.rs`의 동일한 목록 상수에 `"fr"` 추가(및 `include_str!("../../ui/lang/fr.json")` 한 줄 추가).
   - (있다면) 설정 화면의 언어 드롭다운 옵션 배열에 `"fr" → "Français"` 한 줄 추가.

`deck.js`, `note.js`, Rust 커맨드 코드 등 **로직 파일은 단 한 줄도 수정하지 않습니다** — 이 파일들은 항상 `t("some.key")`처럼 키로만 조회하기 때문에, 키 집합이 고정되어 있는 한 언어 개수와 무관하게 동작합니다. 이 무결성을 지키기 위한 유일한 규칙은 "**새 리소스 파일은 반드시 `en.json`과 동일한 키 집합을 모두 포함해야 한다**"이며, 이는 §9 Phase 3에서 제안하는 간단한 키 존재 검증(빌드 시 테스트 또는 스크립트)으로 강제할 수 있습니다.

### 3.5 로더(`ui/i18n.js`) 개요

```js
// 의사코드 — 실제 구현은 Phase 1에서
let dict = {};
async function loadLocale(locale) {
  const res = await fetch(`lang/${locale}.json`);
  dict = res.ok ? await res.json() : await (await fetch("lang/en.json")).json();
}
function t(key, params = {}) {
  let s = dict[key] ?? key; // 키 자체를 폴백으로 노출해 누락을 눈에 띄게 함
  for (const [k, v] of Object.entries(params)) s = s.replaceAll(`{${k}}`, v);
  return s;
}
function applyI18n(root = document) {
  root.querySelectorAll("[data-i18n]").forEach(el => el.textContent = t(el.dataset.i18n));
  root.querySelectorAll("[data-i18n-title]").forEach(el => el.title = t(el.dataset.i18nTitle));
  root.querySelectorAll("[data-i18n-placeholder]").forEach(el => el.placeholder = t(el.dataset.i18nPlaceholder));
}
```

`deck.html`/`note.html`/`about.html`의 정적 텍스트는 `data-i18n="deck.add_note_label"` 같은 속성만 추가하면 되고, `deck.js`/`note.js`의 동적 템플릿 리터럴(예: 서랍 footer 카운트, 타임스탬프)은 `t("deck.drawer.footer_active", {n, m})` 형태로 교체합니다.

---

## 4. Locale Key 설계

### 4.1 원칙

문자열 원문이 아닌 **의미 기반**의 안정적 key를 사용합니다(원문이 바뀌어도 key는 불변). Namespace는 화면/기능 단위로 나누고, 여러 화면이 공유하는 공통 UI 요소는 `common.*`에 둡니다.

### 4.2 네임스페이스 목록

| Namespace | 범위 | 예시 key |
| --- | --- | --- |
| `common.*` | 여러 창이 공유하는 범용 버튼/문구 | `common.close`, `common.cancel`, `common.delete`, `common.delete_permanently_tooltip` |
| `deck.*` | Edge Deck 및 All Notes 서랍 | `deck.add_note_label`, `deck.drawer.title`, `deck.drawer.footer_active` |
| `note.*` | Floating Note 창 | `note.font_tooltip`, `note.title_placeholder`, `note.window_title_with_title` |
| `about.*` | About 창 | `about.window_title`, `about.credits` |
| `tray.*` | 시스템 트레이 메뉴(Rust 전용) | `tray.new_note`, `tray.quit` |
| `delete_confirm.*` | 삭제 확인 모달(deck/note 공용) | `delete_confirm.title`, `delete_confirm.error` |
| `importer.*` | Sticky Notes 가져오기 결과 메시지 | `importer.imported`, `importer.source_not_found` |
| `error.*` | Rust 커맨드가 반환하는 오류 문자열 | `error.registry_open_failed`, `error.note_not_found` |
| `seed.*` | 최초 실행 시 생성되는 웰컴 노트 콘텐츠 | `seed.welcome_title`, `seed.welcome_body` |
| `settings.*` | (신설 예정) 언어/자동시작 설정 화면 | `settings.language_label`, `settings.language_system_default` |
| `theme.*` | (검토 중) 색상 테마 표시명 | `theme.yellow.display_name` 등 — §2·§7 결정 이후 확정 |

### 4.3 전체 key 매핑표

아래는 §1에서 조사한 문자열을 최종 key로 정리한 목록입니다(§1 각 표의 "제안하는 locale key" 열과 동일하며, 여기서는 namespace별로 재그룹핑해 한눈에 볼 수 있도록 정리했습니다).

```
common.close                         닫기 / Close / 閉じる
common.cancel                        취소 / Cancel / キャンセル
common.delete                        삭제 / Delete / 削除
common.delete_permanently_tooltip    메모 영구 삭제 / Delete permanently / 完全に削除

deck.drawer.title                    🗂️ 메모 서랍 (Drawer) / All Notes / メモ一覧
deck.drawer.close_tooltip            닫기 (ESC) / Close (Esc) / 閉じる (Esc)
deck.drawer.tab_active               Active / Active / アクティブ
deck.drawer.tab_archived             Archived / Archived / アーカイブ
deck.drawer.search_placeholder       메모 검색... / Search notes… / メモを検索…
deck.drawer.footer_default           총 0개의 메모 / 0 notes total / メモ 合計0件
deck.drawer.footer_active            총 {n}개의 활성 메모 중 {m}개 표시 / Showing {m} of {n} active notes / ...
deck.drawer.footer_archived          총 {n}개의 보관된 메모 중 {m}개 표시 / Showing {m} of {n} archived notes / ...
deck.drawer.empty_active             표시할 활성 메모가 없습니다. / No active notes to show. / ...
deck.drawer.empty_archived           보관된 메모가 없습니다. / No archived notes. / ...
deck.drawer.restore_tooltip          보관 해제 및 활성 메모로 복원 / Restore to active notes / ...
deck.more_notes_tooltip              전체 메모 및 보관함 열기 / Open all notes & archive / ...
deck.more_count                      +{n} more (모든 로케일 공통 형식 유지 가능)
deck.add_note_tooltip                새 메모 추가 (+) / Add new note (+) / ...
deck.add_note_label                  새 메모 / New Note / 新規メモ

note.font_tooltip                    글꼴 및 텍스트 서식 (Font) / Font & text style / ...
note.color_tooltip                   테마 색상 변경 / Change note color / ...
note.archive_tooltip                 보관함으로 이동 (Archive) / Move to archive / ...
note.close_tooltip                   닫기 및 Deck으로 복귀 (✕) / Close and dock to Deck / ...
note.format_label                    서식 / Format / 書式
note.decrease_size_tooltip           글자 크기 축소 / Decrease font size / ...
note.increase_size_tooltip           글자 크기 확대 / Increase font size / ...
note.bold_label                      굵게 / Bold / 太字
note.bold_tooltip                    굵게 (Bold) / Bold / 太字
note.title_placeholder               제목 없는 메모 / Untitled note / 無題のメモ
note.content_placeholder             내용을 입력하세요... / Type your note… / ...
note.timestamp_just_updated          방금 수정됨 / Just updated / たった今更新
note.timestamp_updated_at            수정: {m}/{d} {h}:{min} / Updated {m}/{d} {h}:{min} / ...
note.resize_tooltip                  크기 조절 / Resize / サイズ変更
note.window_title_untitled           메모 (새 메모) / Note (Untitled) / メモ(無題)
note.window_title_with_title         메모 - {title} / Note - {title} / メモ - {title}
note.default_title                   새 메모(빈 메모 기본 표시명) / Untitled / 無題
note.empty_preview                   (내용이 비어 있습니다) / (No content yet) / (内容がありません)

about.window_title                   StickerMemo 정보 / About StickerMemo / StickerMemo について
about.version                        Version {v} / Version {v} / バージョン {v}
about.edition_tag                    Rust Edition / Rust Edition / Rust版
about.build_label                    Build: {date} / Build: {date} / ビルド: {date}
about.tagline                        Lightweight Desktop Sticky Notes (공통 유지 권장)
about.credits                        Originally built with .NET/WPF, rebuilt with Rust + Tauri / ...

tray.new_note                        ➕ 새 메모 / New Note / 新規メモ
tray.toggle_deck                     🗂️ 덱 보이기 / 숨기기 / Show / Hide Deck / デックを表示 / 非表示
tray.import_sticky_notes             📥 Sticky Notes 가져오기 / Import Sticky Notes / Sticky Notes をインポート
tray.autostart_on                    🚀 Windows 시작 시 실행 [✓ ON] / Start with Windows [✓ ON] / ...
tray.autostart_off                   🚀 Windows 시작 시 실행 [OFF] / Start with Windows [OFF] / ...
tray.quit                            🚪 StickerMemo 종료 / Quit StickerMemo / StickerMemo を終了
tray.about                           StickerMemo 정보... / About StickerMemo… / StickerMemo について...

delete_confirm.title                 이 메모를 영구 삭제하시겠습니까? / Delete this note permanently? / ...
delete_confirm.description           이 작업은 되돌릴 수 없습니다. / This action can't be undone. / ...
delete_confirm.error                 삭제하지 못했습니다. 다시 시도해 주세요. / Couldn't delete the note. Please try again. / ...

importer.source_not_found            Windows Sticky Notes 데이터를 찾을 수 없습니다. / ...
importer.temp_copy_failed            임시 데이터베이스 복사 실패: {e} / ...
importer.imported                    Windows Sticky Notes에서 {n}개의 메모를 가져왔습니다. / ...
importer.imported_with_skipped       Windows Sticky Notes에서 {n}개의 메모를 가져왔습니다. ({m}개 기존 메모 건너뜀) / ...
importer.import_failed               Sticky Notes 가져오기 실패: {e} / ...

error.registry_open_failed           레지스트리 키 열기 실패: {e} / ...
error.exe_path_failed                실행 파일 경로 확인 실패: {e} / ...
error.autostart_set_failed           자동 실행 값 설정 실패: {e} / ...
error.note_not_found                 메모를 찾을 수 없습니다: {id} / Note not found: {id} / ...

seed.welcome_title                   반가워요! / Welcome! / ようこそ!
seed.welcome_body                    (4문단 온보딩 텍스트, §8 참고)

settings.language_label              언어 / Language / 言語               [신설]
settings.language_system_default     시스템 기본값 / System Default / システムの既定  [신설]
settings.language_ko                 한국어                               [신설]
settings.language_en                 English                              [신설]
settings.language_ja                 日本語                               [신설]
```

---

## 5. 언어 선택 방식 검토

### 5.1 최초 실행 시 자동 감지

**결론: 가능하지만, Rust 쪽에서 최소 1개의 Windows API 호출(또는 경량 크레이트) 추가가 필요합니다.**

트레이 메뉴(§1.5)와 About/Note 창 제목은 **Rust `setup()` 훅 안에서, 어떤 WebView도 아직 완전히 로드되지 않은 시점**에 만들어집니다(`main.rs`의 `setup_tray()` 호출이 창 표시보다 먼저 일어남). 따라서 "JS에서 `navigator.language`를 읽어 Rust에 알려주는" 방식은 **최초 실행 시점에는 시간상 늦어** 트레이 메뉴에 적용할 수 없습니다. Rust가 스스로 OS 언어를 감지해야 합니다.

- 권장안: `windows` crate에 `Win32_Globalization` feature를 추가하고 `GetUserDefaultLocaleName()`(예: `"ko-KR"`, `"ja-JP"`, `"en-US"`)을 호출해 앞 2글자를 `ko`/`ja`/`en`으로 매핑, 그 외는 `en`으로 폴백. 이미 `src-tauri/Cargo.toml`이 `windows` crate와 `Win32_UI_WindowsAndMessaging` 등 여러 feature를 쓰고 있으므로(§7의 `docs/full-source-analysis-report.md` M-7 항목 참고) 동일 crate에 feature 하나를 추가하는 정도로 새로운 외부 의존성 없이 구현 가능합니다.
- 대안: `sys-locale` 같은 소형 크레이트를 새로 추가(더 짧은 코드, 그러나 신규 의존성 추가).
- **비권장**: JS `navigator.language`만으로 최초 언어를 정하는 방식 — Deck/Note 창 로드 후에는 유효하지만, 트레이 메뉴가 이미 한국어(하드코딩 시절 기준)로 만들어진 뒤라 최초 실행 경험이 어긋납니다.

### 5.2 지원 언어 매핑 및 폴백

```
감지된 OS 언어 태그 → 매핑
  ko-KR, ko           → "ko"
  ja-JP, ja           → "ja"
  en-*, 그 외 전부      → "en"  (요구사항대로 English를 기본 폴백으로 사용)
```

### 5.3 수동 설정 및 영속화

- 요구사항대로 SQLite에 저장하는 방식을 권장합니다. 현재 `storage/db.rs`에는 노트 전용 `Notes` 테이블만 있으므로, **순수 추가형(additive)** 신규 테이블을 만듭니다:

```sql
CREATE TABLE IF NOT EXISTS Settings (
    Key   TEXT PRIMARY KEY,
    Value TEXT
);
-- 예: INSERT/UPDATE ("locale", "ko" | "en" | "ja" | "system")
```

  이는 `docs/full-source-analysis-report.md`가 이미 확인한 기존 마이그레이션 패턴(`ALTER TABLE ... ADD COLUMN`로 컬럼을 추가해온 것)과 같은 철학으로, **기존 `Notes` 테이블 스키마는 전혀 건드리지 않고** 새 테이블만 추가하므로 구버전 앱과의 DB 호환성에 영향이 없습니다(§8에서 상세 논의).
- `"system"` 값은 "시스템 기본값" 옵션을 선택했다는 의미로, 매 실행 시 §5.1 감지를 다시 수행합니다. `"ko"/"en"/"ja"`가 저장되어 있으면 그 값을 그대로 사용합니다.

### 5.4 설정 화면 UX

요청하신 형태:

```
Language
- System Default
- 한국어
- English
- 日本語
```

**중요한 발견**: 현재 코드베이스에는 이 UI를 놓을 **설정 화면 자체가 없습니다.** `get_autostart`/`set_autostart_setting` 커맨드는 이미 구현되어 있지만 `ui/*.js` 어디에서도 호출되지 않는 고아 커맨드입니다(자동시작 토글은 오직 트레이 메뉴 클릭 → Rust 직접 처리 경로만 존재). 즉 언어 설정 UI는 **자동시작 설정 UI와 함께 최초로 신설되는 화면**입니다. 두 가지 배치안이 있습니다:

| 옵션 | 설명 | 장단점 |
| --- | --- | --- |
| A. About 창 확장 | 기존 About 창(320×310, 고정 크기)에 언어 드롭다운 + 자동시작 체크박스 추가 | 창을 새로 만들 필요 없음(공수 최소화), 다만 "정보" 창과 "설정" 창의 책임이 섞임 + 고정 크기라 §7 레이아웃 리스크가 더 커짐 |
| B. 전용 `settings.html` 신설 | 트레이 메뉴에 "⚙️ 설정..." 항목 추가, `about`과 동일한 get-or-create 패턴의 새 창 | 책임 분리가 명확, 향후 설정 항목이 늘어도 확장 용이. 신규 창 1개 + 트레이 메뉴 1항목 추가 필요(공수 소폭 증가) |

이 규모의 앱에서는 **B(전용 설정 창)를 권장**합니다 — About 창은 순수 정보 표시용으로 남기고, 고정폭 About 창에 인터랙티브 컨트롤(드롭다운, 체크박스)까지 욱여넣는 것은 §7에서 지적하는 About 창의 레이아웃 취약성을 오히려 키우기 때문입니다. 다만 최종 배치는 구현 단계에서 사용자와 다시 확인이 필요한 **미결정 사항**으로 남겨둡니다.

### 5.5 언어 변경 시 반영 방식

세 가지 옵션을 비교합니다.

| 방식 | 설명 | 이 프로젝트에 대한 평가 |
| --- | --- | --- |
| ① 앱 재시작 필요 | 설정 변경 후 "적용하려면 재시작하세요" 안내만 표시 | 구현 가장 단순, 하지만 사용자 경험은 다소 아쉬움 |
| ② 열린 UI 즉시 갱신 | Rust가 트레이 메뉴를 `tray.set_menu()`로 재빌드하고, 열려 있는 모든 `note-*`/`about` 창 제목을 재설정하며, 모든 WebView에 `locale-changed` 이벤트를 `emit`. 각 창의 JS는 새 `lang/{locale}.json`을 다시 `fetch`하고 `applyI18n()`을 재호출해 이미 그려진 DOM 텍스트를 즉시 교체 | **권장**. 코드 규모가 작아(`deck.js` ~370줄, `note.js` ~260줄) 전체 재렌더 로직 도입 부담이 크지 않고, Tauri v2가 런타임 트레이 메뉴 교체(`TrayIcon::set_menu`)를 지원하므로 기술적으로 막힘이 없음 |
| ③ 새로 생성되는 창부터 적용 | 이미 열린 Floating Note/Deck은 그대로 두고, 이후 새로 여는 창부터 새 언어 적용 | 구현은 ②보다 쉽지만, 트레이 메뉴(항상 하나뿐이고 재사용됨)는 이 방식으로 처리할 수 없어 결국 트레이만은 즉시 갱신이 필요 — 일관성이 떨어짐 |

**권장: ②(즉시 갱신)**를 기본으로 하되, 구현 우선순위상 여유가 없다면 ①로 축소해도 무방한 수준의 앱 규모입니다. ③은 트레이 메뉴 때문에 어차피 부분적으로 ②를 구현해야 하므로 권장하지 않습니다.

---

## 6. Rust와 WebView 사이의 역할 분담

### 6.1 현황

- **WebView(JS) 전용 문자열**: Edge Deck, Floating Note, About 창의 정적 UI 텍스트/tooltip/placeholder, 삭제 확인 모달(§1.2~1.4, §1.6) — 총 39개.
- **Rust 전용 문자열**: 트레이 메뉴 7개(§1.5) — WebView를 전혀 거치지 않고 OS 네이티브 메뉴로 렌더링됨.
- **Rust가 생성해 WebView(창 제목)로 노출되는 문자열**: Floating Note/About 창 제목(§1.9의 65·66번, §1.4의 38번) — `WebviewWindowBuilder::title()` / `Window::set_title()`로 설정되며 HTML `<title>` 태그와는 무관.
- **Rust가 반환하지만 현재 프런트엔드가 표시하지 않는 문자열**: import 결과 메시지(§1.7), 자동시작 오류(§1.8), `error.note_not_found`(§1.9) — 향후 UI가 이를 표시하게 되면 즉시 다국어가 필요해지는 잠재 대상.

### 6.2 두 곳이 같은 로케일을 알아야 하는가?

**예.** 트레이 메뉴와 창 제목은 Rust가 단독으로 생성하므로, Rust 프로세스가 "현재 로케일이 무엇인지"를 반드시 인지해야 합니다. WebView는 이와 별개로 자체 `fetch` 기반 로딩을 하지만, **두 쪽이 서로 다른 번역 데이터를 참조하면 유지보수 시 불일치가 발생**합니다(이미 `docs/full-source-analysis-report.md`의 M-3이 테마 색상 정의가 JS/CSS/Rust 세 곳에 중복되어 있음을 지적한 것과 같은 유형의 리스크).

### 6.3 권장: 단일 진실 공급원(Single Source of Truth)

```
ui/lang/ko.json  ──┬─→ (런타임 fetch) ──→ ui/i18n.js  ──→ deck.js / note.js / about.html
ui/lang/en.json  ──┤
ui/lang/ja.json  ──┴─→ (컴파일 타임 include_str!) ──→ src-tauri/src/i18n.rs ──→ main.rs(트레이) / commands/mod.rs(창 제목, 오류 메시지)
```

- JSON 파일은 저장소상 **한 벌만 존재**하며(`ui/lang/` 아래), JS는 런타임에 `fetch`로 읽고 Rust는 빌드 타임에 `include_str!`로 파일 내용을 그대로 바이너리에 임베딩합니다. 즉 "복사"가 아니라 "같은 파일을 두 가지 방식으로 읽는" 구조이므로 실질적인 중복이 없습니다.
- Rust는 자신이 실제로 쓰는 키(`tray.*`, `note.window_title_*`, `about.window_title`, `error.*`, `importer.*`, `seed.*`)만 조회하면 되고, JS 전용 키(`deck.*`, `note.font_tooltip` 등)도 같은 파일에 함께 있어도 무해합니다(사용하지 않는 키는 그냥 무시).
- 대안으로 Rust 쪽에 별도의 작은 `match` 기반 정적 테이블을 중복 유지하는 방법도 있지만, 이는 §M-3과 동일한 유형의 유지보수 부채를 새로 만드는 것이므로 **비권장**.

### 6.4 파라미터 치환

Rust 쪽 문자열 중 `{n}`, `{m}`, `{e}`, `{id}`, `{title}`, `{date}` 같은 플레이스홀더가 포함된 것(예: `importer.imported_with_skipped`, `note.window_title_with_title`)은 JS의 `t(key, params)`와 동일한 규칙(`"{key}"` 리터럴 치환)을 쓰는 아주 단순한 Rust 헬퍼 함수 하나(`fn t(locale, key, params: &[(&str,&str)]) -> String`)로 충분합니다. `format!` 매크로의 위치 기반 인자와 달리 이름 기반 치환을 쓰는 것이, 언어별로 어순이 달라져도(예: 일본어의 "{n}件중 {m}件" vs 한국어의 "{n}개의 ... {m}개") 안전합니다.

---

## 7. 레이아웃 영향 분석

언어별 문자열 길이 차이(대략 한국어 대비 영어가 조사에 따라 ±20~30%, 일본어는 한자/가나 혼용으로 문자 수는 적지만 폭이 넓은 경우가 많음)로 인한 레이아웃 붕괴 가능 지점을 고정 크기/좁은 영역 위주로 점검했습니다.

### 7.1 🔴 About 창 — 가장 위험 (사용자가 이미 실제로 겪은 문제와 동일 유형)

- `tauri.conf.json`에는 없고 `main.rs` L220에서 동적 생성 시 `.inner_size(320.0, 310.0).min_inner_size(320.0, 310.0).resizable(false)`로 **완전히 고정된 크기**입니다. 스크롤도 없습니다.
- `about.html`의 크레딧 문구 `"Originally built with .NET/WPF<br>Rebuilt with Rust + Tauri"`는 영어 문장 길이에 맞춰 **줄바꿈 지점이 `<br>`으로 하드코딩**되어 있습니다. 번역 문자열의 길이/줄바꿈 자연 지점이 다르면:
  - 한 줄로 다 들어가는 언어는 `<br>` 때문에 불필요하게 2줄이 되어 세로 공간을 낭비하고,
  - 반대로 원문보다 긴 언어는 `<br>` 이후에도 각 줄이 감싸져(wrap) 예상보다 더 많은 줄을 차지하며,
  - 창이 `resizable:false` + 고정 높이이므로 **넘치는 내용은 그대로 잘려서(clipped) 보이지 않게 됩니다.**
- 이는 사용자가 "이번 About 창에서 경험한 것"으로 언급한 문제와 정확히 같은 구조적 원인입니다.
- **권장 조치**: (1) `<br>` 강제 줄바꿈 제거, 컨테이너 `max-width` + 자연 word-wrap으로 대체, (2) 3개 언어의 실제 번역문 기준 최악 케이스(가장 긴 조합)를 대입해 About 창 크기를 재산정(예: 320×310 → 340×340 등 여유 확보), (3) 가능하다면 콘텐츠의 `scrollHeight`를 읽어 창 생성 시 높이를 살짝 가변적으로 계산, 또는 최소한 `resizable(true)` + `min_inner_size`만 지정하는 방식으로 완화, (4) §9 Phase 7에서 3개 언어 모두 실제 렌더링 스크린샷 QA를 필수 게이트로 지정.

### 7.2 🟠 All Notes 서랍(Drawer) 헤더

- `.drawer-container { width: 310px }`로 고정, `.drawer-header { display:flex; justify-content:space-between }` 안에 `.drawer-title`(제목)과 닫기 버튼이 나란히 배치됩니다.
- `.drawer-title`에는 `white-space:nowrap`이나 `text-overflow:ellipsis`가 **없어**, 번역문이 길어지면(예: 원문 그대로 "All Notes (Drawer)"처럼 괄호 설명까지 직역할 경우) 제목이 2줄로 감싸지며 헤더 높이가 늘어나고 닫기 버튼과의 정렬이 흐트러질 수 있습니다.
- **권장**: 번역문을 짧게 유지(§2 용어집에서 "All Notes"/"メモ一覧"으로 이미 축약 제안)하는 동시에, 방어적으로 `.drawer-title`에 `white-space:nowrap; overflow:hidden; text-overflow:ellipsis; max-width: 230px` 정도를 추가해 어떤 언어가 와도 안전하게 만들 것을 권장.

### 7.3 🟡 Floating Note 툴바(Toolbar) 및 팝업

- 5개 툴바 버튼(`font/color/archive/delete/close`)은 모두 `width:24px; height:24px`의 아이콘 전용 버튼이라 **텍스트 길이 영향 없음**(이모지 1글자 고정). 각 버튼의 `title` 속성(네이티브 OS 툴팁)은 버튼 크기와 무관하게 별도 오버레이로 렌더링되므로 리스크 낮음.
- `.font-popup { width: 230px }` 내부의 `.font-control-row`는 `size-stepper`(−, "13.5 pt", +)와 `bold-chip`("B", "굵게"/"Bold"/"太字")를 `justify-content:space-between`으로 나란히 배치합니다. 두 언어 모두 텍스트가 짧아(Bold=4자, 太字=2자, 굵게=2자) 실제 리스크는 낮지만, 230px라는 좁은 폭 안에 두 그룹이 들어가므로 **QA 시 시각 확인 권장**.
- `.timestamp-text`(하단 타임스탬프)는 `overflow`/`white-space` 제어가 없는 상태로 `resize-grip`(14×14px 고정)과 같은 줄에 배치됩니다. 노트 창 최소 크기(280×180)에서 "Updated 09/17 14:32"류의 영문 타임스탬프가 그립 아이콘을 밀어낼 가능성은 실측상 낮지만(대략 240px 여유 공간 대비 문자열 폭은 100~110px 수준), **안전하게 `white-space:nowrap; overflow:hidden; text-overflow:ellipsis; min-width:0`을 방어적으로 추가**할 것을 권장.

### 7.4 🟢 삭제 확인 모달

- `.delete-confirm-card { width: min(300px, 100%); max-height: calc(100vh - 12px); overflow-y:auto }`로 **이미 가변 높이 + 스크롤을 지원**하므로 번역 문자열이 길어져도 잘림 없이 자연스럽게 세로로 늘어납니다. 리스크 낮음.

### 7.5 🟢 트레이 메뉴

- Windows 네이티브 컨텍스트 메뉴로 렌더링되며, 메뉴 폭은 OS가 가장 긴 항목 텍스트에 맞춰 자동 산정합니다. StickerMemo 코드가 클라이언트 영역을 직접 그리는 부분이 아니므로 **잘림 리스크 없음**.

### 7.6 🟡 폰트 선택 드롭다운

- `note.html`의 `<option>` 값들이 `"Malgun Gothic (맑은 고딕)"`처럼 폰트 실제 이름 + 한글 부기 설명으로 되어 있습니다. 이 부기 설명은 **번역 대상이 아니라 로케일별로 표시 여부/언어를 다시 결정해야 하는 항목**입니다: 예를 들어 일본어 UI에서 `"Gulim (굴림)"`의 한글 부기는 의미가 없으므로, en/ja 로케일에서는 괄호 설명을 생략하거나 각 언어 사용자에게 익숙한 표기로 바꾸는 편이 낫습니다. `.font-select { width:100%; }`이며 닫힌 상태의 `<select>`는 넘치는 텍스트를 말줄임 없이 자르는 것이 일반적인 브라우저 기본 동작이라, 부기 설명이 길어질수록 잘림 위험이 커집니다. **권장**: 로케일별로 괄호 설명을 아예 다른 콘텐츠로 취급해 `note.font_options.<font_id>` 같은 key로 분리 관리.

### 7.7 🟢 Edge Deck 탭 / 호버 프리뷰

- 탭 제목(`.tab-title-text`)과 호버 카드 제목(`.hover-title`)은 이미 `overflow:hidden; text-overflow:ellipsis; white-space:nowrap`이 적용되어 있어 안전합니다. 다만 이 영역에 표시되는 텍스트는 **사용자가 작성한 메모 제목(사용자 콘�텐츠)**이지 UI 번역 문자열이 아니므로, 애초에 i18n 영향권 밖입니다(§8에서 재확인).

---

## 8. 기존 사용자 데이터와의 관계

### 8.1 원칙

`Notes` 테이블에 저장된 `Title`/`Text`(메모 제목·본문)는 **사용자 콘텐츠**이며, 어떤 경우에도 로케일 전환 시 번역/변경 대상이 아닙니다. 이는 이미 코드상 올바르게 분리되어 있습니다.

### 8.2 DB에 저장되지만 실제로는 "코드"인 값들 — 점검 결과

| 컬럼 | 저장값 예시 | 실제 성격 | 로케일 영향 |
| --- | --- | --- | --- |
| `ThemeId` | `"Yellow"`, `"Pink"`, ... | 내부 열거형 키(영문 고정) | **없음** — 화면에 표시될 때만(§7.6, §2) 로케일별 라벨로 변환해야 하며, DB에는 항상 이 영문 키만 저장되어야 합니다. 현재 JS(`deck.js`의 `THEMES` 객체, `note.js`의 `setThemeClass`)는 이미 이 원칙을 지키고 있습니다(라벨이 아니라 키를 저장). |
| `FontFamily` | `"Malgun Gothic"`, `"Pretendard"`, ... | 실제 OS/웹 폰트 리소스 이름 | **없음** — 번역 불가/불필요 항목. 로케일이 바뀌어도 이 값 자체는 그대로 유지되어야 함(§7.6에서 다룬 것은 드롭다운의 "표시 부기 설명"일 뿐, 저장값과는 무관). |
| `IsArchived`/`IsFloating`/`IsCollapsed`/`IsTopmost` | `0`/`1` | 불리언 | 없음 |
| `SortOrder` | 정수 | 정렬 키 | 없음 |

**결론**: 스키마 전체를 훑어봐도 "UI 표시용 한국어 문자열이나 상태명"이 컬럼 값으로 저장되는 곳은 없습니다. 기존 사용자 DB와의 호환성 문제는 발생하지 않습니다.

### 8.3 예외 케이스 — 웰컴(온보딩) 시드 노트

`commands::get_active_notes()`가 **활성 노트와 보관된 노트가 모두 0개일 때만** `"반가워요!"` 제목과 4문단짜리 온보딩 본문을 가진 노트 하나를 실제로 `INSERT`합니다(commands/mod.rs L26-35). 이 노트는:

1. **생성되는 순간 Rust가 하드코딩한 한국어**이므로, §1.9(70·71번)에서 `seed.welcome_title`/`seed.welcome_body` key로 다국어화해야 합니다. 로케일 감지(§5.1)가 이 시드 로직보다 먼저 실행되도록 초기화 순서를 조정해, **최초 실행 시 감지된(또는 사용자가 고른) 언어로 온보딩 노트가 생성**되게 하는 것을 권장합니다.
2. **그러나 일단 DB에 저장되고 나면, 그 순간부터는 다른 사용자 메모와 완전히 동일한 "사용자 콘텐츠"가 됩니다.** 즉, 온보딩 노트가 한국어로 생성된 이후 사용자가 설정에서 언어를 English로 바꿔도, **이미 존재하는 온보딩 노트의 텍스트는 소급 번역되지 않습니다.** 이는 결함이 아니라 "사용자가 쓴 메모는 건드리지 않는다"는 원칙의 당연한 결과이며, QA 단계에서 "왜 언어를 바꿨는데 온보딩 메모가 그대로 한글이지?"를 버그로 오인하지 않도록 **문서화가 필요**합니다(§9 Phase 6/7에 QA 체크리스트 항목으로 포함 권장).
3. 구버전(현재 배포된 하드코딩 한국어 버전)을 이미 쓰던 사용자의 DB에는 이미 한국어 온보딩 노트가 저장되어 있을 수 있습니다 — 이는 그대로 두면 됩니다(사용자 콘텐츠로 취급, 마이그레이션 불필요).

### 8.4 설정 저장 위치

§5.3에서 제안한 신규 `Settings` 테이블은 `Notes` 테이블과 별개이며, 컬럼 추가가 아니라 **완전히 새로운 테이블**이므로 기존 `AppNotes.db`를 여는 구버전 앱(만약 사용자가 롤백한다면)에도 영향이 없습니다 — 구버전 앱은 이 테이블의 존재 자체를 모른 채 무시합니다. `docs/rust-rebuild-plan.md`가 명시한 "무결점 롤백 지원" 원칙과도 부합합니다.

---

## 9. 구현 계획

각 Phase는 이전 Phase의 산출물에 의존합니다. 파일 목록은 주로 수정/생성되는 대상이며, 위험도는 "기존 동작을 깨뜨릴 가능성"을 기준으로 평가했습니다.

### Phase 1 — Locale Resource 구조 확립

- **작업**: `ui/lang/ko.json`, `en.json`, `ja.json`(§4.3 key 전체를 채운 최초 버전, 우선 ko.json은 §1 조사 결과 원문을 그대로 옮기는 수준으로 시작 가능) 및 `ui/i18n.js`(§3.5 로더) 신규 작성.
- **위험도**: 낮음 — 아직 어떤 기존 파일도 참조를 바꾸지 않으므로 앱 동작에 영향 없음(순수 추가).

### Phase 2 — 기존 하드코딩 문자열 치환 (프런트엔드)

- **대상 파일**: `ui/deck.html`, `ui/deck.js`, `ui/note.html`, `ui/note.js`, `ui/about.html`, `ui/delete-confirm.js`.
- **작업**: §1.2~§1.4, §1.6의 모든 문자열을 `data-i18n*` 속성 또는 `t()` 호출로 치환. §2에서 지적한 "새 메모" 이중 의미를 `deck.add_note_label`/`note.default_title` 두 key로 분리.
- **위험도**: 중간 — 호출 지점이 많아(약 40곳) 누락 위험이 있음. §1의 전체 표를 체크리스트로 사용해 1:1 대조 권장. 기존 DOM 구조/이벤트 리스너 선택자(`document.getElementById(...)`)는 그대로 유지하고 텍스트 콘텐츠만 바꾸므로 로직 회귀 위험은 낮음.

### Phase 3 — Rust 측 문자열 치환 및 Single Source of Truth 연결

- **대상 파일**: 신규 `src-tauri/src/i18n.rs`, 수정 `src-tauri/src/main.rs`(트레이 메뉴, about 창 제목), `src-tauri/src/commands/mod.rs`(노트 창 제목, 웰컴 시드), `src-tauri/src/importer/sticky_notes.rs`, `src-tauri/src/platform/autostart.rs`.
- **작업**: `include_str!`로 3개 JSON 임베딩 → `serde_json`(기존 의존성 재사용) 파싱 → `OnceLock<HashMap<...>>` 캐시 → `t(locale, key, params)` 헬퍼(§6.4) 제공. 각 파일의 한국어 리터럴을 이 헬퍼 호출로 치환.
- **위험도**: 중간 — 컴파일 타임 임베딩이라 새 크레이트 불필요하지만, 세 JSON의 키 누락 시 런타임 폴백 처리(키 없으면 `en.json` 값, 그마저 없으면 키 문자열 자체 반환) 로직이 필요.

### Phase 4 — Windows 시스템 언어 감지 및 사용자 설정 저장

- **대상 파일**: `src-tauri/Cargo.toml`(`Win32_Globalization` feature 추가), 신규 `src-tauri/src/platform/locale.rs`, `src-tauri/src/storage/db.rs`(신규 `Settings` 테이블 + get/set 헬퍼), `src-tauri/src/commands/mod.rs`(신규 `get_locale`/`set_locale` 커맨드).
- **작업**: §5.1의 `GetUserDefaultLocaleName` 호출 + §5.2 매핑, §5.3의 `Settings` 테이블 CRUD.
- **위험도**: 중간 — 신규 Win32 API 호출 1개 추가(기존에도 `windows` crate를 직접 다루는 `platform/single_instance.rs` 선례가 있어 패턴은 검증됨), DB 스키마는 추가만 하므로 §8.4에 따라 호환성 리스크 낮음.

### Phase 5 — Tray/Rust 문자열 연결 및 설정 화면 신설

- **대상 파일**: `src-tauri/src/main.rs`(`locale-changed` 시 `tray.set_menu()` 재빌드, 열린 `note-*`/`about` 창 제목 재설정), `src-tauri/src/commands/mod.rs`(로케일 변경 커맨드가 모든 창에 이벤트 emit), 신규 설정 화면(§5.4의 옵션 A/B 중 결정 필요 — 신규 `ui/settings.html`+`ui/settings.js` 또는 `ui/about.html` 확장), 관련 `capabilities/*.json`(신규 창이면 캐퍼빌리티 파일도 추가 필요, `docs/full-source-analysis-report.md`의 M-2 항목과 동일한 패턴).
- **작업**: §5.5의 "즉시 갱신" UX 구현, 언어 드롭다운 + (기존에 방치되어 있던) 자동시작 체크박스를 이 화면에서 함께 노출.
- **위험도**: 중상 — 이 앱에서 유일하게 "새로운 화면/새로운 창 생명주기"가 추가되는 단계이며, §5.4의 배치 결정(옵션 A vs B)이 먼저 확정되어야 착수 가능.

### Phase 6 — ko/en/ja 번역 검수

- **대상 파일**: `ui/lang/*.json` 3개 뿐.
- **작업**: §1~§4에서 제안한 초안 번역을 원어민 수준으로 재검토(특히 일본어는 요구사항대로 영어 UI의 직역이 아니라 Windows 데스크톱 관용구 기준으로 재검토, §2 용어집 기준 준수 여부 확인), 각 key별 "최악 케이스 길이"(가장 긴 실제 번역문)를 Phase 7 QA 입력값으로 별도 표시.
- **위험도**: 낮음(콘텐츠 전용) — 단, Phase 7보다 반드시 선행되어야 함.

### Phase 7 — UI 레이아웃 QA

- **대상**: §7에서 식별한 고정폭/고정높이 영역(About 창 최우선, All Notes 서랍 헤더, Floating Note 툴바/팝업, 타임스탬프) 전체를 3개 언어 × 주요 창 크기(Floating Note 최소 280×180, 기본 350×350)에서 실제 렌더링 확인.
- **작업**: 문제 발견 시 §7의 권장 CSS 방어 조치(ellipsis, nowrap, max-width, About 창 크기 재산정) 적용. 이 Phase가 "About 창이 다시 잘리는" 재발을 막는 최종 게이트입니다.
- **위험도**: 낮음~중간(주로 CSS 조정), 그러나 **가장 중요한 사용자 경험 검증 단계**이므로 생략 불가.

---

## 10. 최종 요약

1. **현지화 대상 문자열 총 개수**: 사용자 노출 문자열 **약 58개**(동일 의미 중복 통합 기준, §1.1~1.10) + 향후 설정 화면 신설로 추가되는 **약 5개**(§4.3 `settings.*`) = 설계상 **총 약 63개 key**. 별도로 미사용 레거시 `ui/dialog.html`의 5개 문자열은 범위 밖(§1.11).
2. **현재 하드코딩 위치**: 프런트엔드 6개 파일(`ui/deck.html`, `deck.js`, `note.html`, `note.js`, `about.html`, `delete-confirm.js`) + Rust 5개 파일(`main.rs`, `commands/mod.rs`, `domain/note.rs`, `importer/sticky_notes.rs`, `platform/autostart.rs`). `storage/db.rs`, `domain/theme.rs`, `Cargo.toml`, `tauri.conf.json`, capability JSON에는 번역 대상 문자열 없음(§1 각 절 참고).
3. **추천 i18n 아키텍처**: JSON 리소스 파일을 유일한 진실 공급원으로 삼아, JS는 런타임 `fetch`, Rust는 컴파일 타임 `include_str!`로 동일 파일을 소비(§3, §6). 프런트엔드에 빌드 스텝이 없다는 제약과 가장 자연스럽게 맞물리는 구조입니다.
4. **추천 locale 파일 구조**: `ui/lang/{ko,en,ja}.json`, **평면(dot-path) key** 형식(중첩 객체 아님) — 제안하신 두 옵션(`lang.ko.js` 방식, `ko.json` 방식) 중 후자를 권장(§3.2~3.3).
5. **ko/en/ja 용어집**: §2 표 참고 — 특히 "새 메모"(버튼 vs 기본 제목)의 의미 분리, "덱(Deck)"의 고유명사 유지, 테마 색상명(Yellow 등)의 번역 여부는 구현 착수 전 확정이 필요한 결정 사항으로 별도 표시했습니다.
6. **향후 새로운 언어 추가 절차**: `ui/lang/{locale}.json` 1개 추가 + JS/Rust 양쪽의 지원 언어 목록에 1줄씩 등록만으로 완료(§3.4) — `deck.js`/`note.js`/Rust 커맨드 로직은 무수정.
7. **Rust ↔ WebView locale 공유 방식**: 동일 JSON 파일을 양쪽이 서로 다른 시점(컴파일 타임 vs 런타임)에 읽는 구조로 중복 없이 공유(§6). 최초 실행 시 언어 감지는 트레이 메뉴가 WebView보다 먼저 만들어지는 초기화 순서 때문에 **Rust 자체의 Win32 API 호출**(`GetUserDefaultLocaleName`, 신규 feature 1개 추가)이 필요합니다(§5.1).
8. **기존 DB 영향**: 없음. `ThemeId`/`FontFamily`는 이미 로케일 독립적인 키 값으로 저장되고 있으며(§8.2), 유일한 신규 저장소는 완전히 새로운 `Settings` 테이블(§5.3, §8.4)로 기존 `Notes` 스키마·구버전 호환성에 영향을 주지 않습니다. 단, 최초 실행 시 생성되는 웰컴 노트는 생성 시점 이후 "사용자 콘텐츠"로 취급되어 언어 변경이 소급 적용되지 않는다는 점을 QA 단계에서 명확히 문서화해야 합니다(§8.3).
9. **UI 레이아웃 위험**: **About 창(고정 320×310, `resizable:false`, 하드코딩 `<br>` 줄바꿈)이 최우선 리스크**이며, 사용자가 이미 실제로 겪은 문제와 원인이 동일합니다(§7.1). 그 외 All Notes 서랍 헤더(§7.2), Floating Note 타임스탬프/폰트 팝업(§7.3, §7.6)이 중간 위험으로 식별되었고, 삭제 확인 모달과 트레이 메뉴, Deck 탭/호버 프리뷰는 이미 안전한 구조입니다(§7.4, §7.5, §7.7).
10. **구현 순서와 예상 난이도**: Phase 1(리소스 구조, 低) → Phase 2(프런트엔드 치환, 中) → Phase 3(Rust 치환 + SSOT 연결, 中) → Phase 4(Windows 언어 감지 + 설정 저장, 中) → Phase 5(트레이 연동 + 설정 화면 신설, 中上 — 유일하게 새 창/새 UX가 필요한 단계) → Phase 6(번역 검수, 低) → Phase 7(레이아웃 QA, 低中이지만 필수 게이트). 전체적으로 **"문자열 치환" 자체는 기계적 작업**이고, **"설정 화면 신설"과 "About 창 레이아웃 재설계"가 실질적으로 새로운 설계가 필요한 두 지점**입니다.
</content>
