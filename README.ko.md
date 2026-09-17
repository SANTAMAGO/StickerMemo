# StickerMemo

화면 가장자리에 머무는, 가벼운 Windows 스티커 메모 앱.

[English](README.md) · [한국어](README.ko.md) · [日本語](README.ja.md)

## 다운로드

**[StickerMemo v1.0.0 다운로드](https://github.com/SANTAMAGO/StickerMemo/releases/download/v1.0.0/StickerMemo-v1.0.0-win-x64-portable.zip)**
Windows x64 · Portable · 설치 프로그램 불필요

또는 [Releases](https://github.com/SANTAMAGO/StickerMemo/releases) 페이지에서 모든 버전을 확인할 수 있습니다.

### 빠른 시작

1. `StickerMemo-v1.0.0-win-x64-portable.zip`을 원하는 곳에 압축 해제합니다.
2. `StickerMemo.exe`를 실행합니다.

끝입니다 — 설치 프로그램도, 관리자 권한도 필요 없고, `%APPDATA%\StickerMemo\` 밖에는 아무것도 기록하지 않습니다.

## 주요 기능

- **Edge Deck** — 화면 오른쪽 가장자리에 고정되는 얇고 항상 위에 있는(always-on-top) 막대. 열려 있는 메모마다 탭이 하나씩 생기고, 탭에 마우스를 올리면 미리보기가, 클릭하면 플로팅 창이 열립니다.
- **Floating Notes** — 화면 어디로든 드래그할 수 있는 독립적인 크기 조절 가능한 메모 창. 위치, 크기, 색상, 글꼴 설정을 각각 기억합니다.
- **전체 메모 서랍(All Notes drawer)** — Deck에서 열리는 서랍 하나로 활성/보관 메모를 검색하고 둘러볼 수 있습니다.
- **Microsoft Sticky Notes 가져오기** — 기존 Sticky Notes 데이터베이스를 복사본(읽기 전용)으로 읽어와 StickerMemo로 메모를 가져옵니다. 원본은 수정하지 않습니다.
- **다국어 UI** — 한국어 / English / 日本語 지원. 최초 실행 시 Windows 표시 언어를 자동 감지하며, 이후에는 Settings에서 재시작 없이 즉시 언어를 전환할 수 있습니다.
- **Settings / About 창** — 언어 변경, "Windows 시작 시 자동 실행" 설정, 빌드/버전 정보 확인.
- **시스템 트레이 제어** — 새 메모, Deck 표시/숨김, 가져오기, 설정, 정보, 종료를 트레이 아이콘에서 바로 실행.
- **단일 인스턴스** — StickerMemo를 다시 실행하면 새 창이 뜨는 대신 이미 실행 중인 인스턴스에 포커스가 갑니다.
- **로컬 전용 저장** — 메모는 로컬 SQLite 데이터베이스에만 저장되며, 어디로도 전송되지 않습니다.
- **가벼움** — Rust + Tauri로 만들어져 OS에 내장된 WebView2를 그대로 사용하므로, 별도의 브라우저 런타임 없이 실행 파일 크기가 작습니다.

## Screenshots

_아직 저장소에 스크린샷이 없습니다 — 추후 업데이트에서 추가될 예정입니다._

## Project History (개발 배경)

StickerMemo는 Windows 기본 Sticky Notes를 사용하면서 느꼈던 불편함에서 시작했습니다. 메모가 완전히 열려 화면을 가리거나, 아예 숨어서 잊혀지기 쉬운 두 가지 상태만 존재하는 것이 아쉬웠습니다. 필요할 때 클릭 한 번으로 메모를 꺼내 보고, 필요하지 않을 때는 화면 가장자리에 눈에 띄지 않게 두고 싶다는 생각에서 출발했습니다.

첫 번째 동작 버전은 .NET 8 / WPF로 만들어져 실제로 사용하면서 발전했습니다. 여러 개의 메모 창, 색상 테마, 글꼴 서식, Microsoft Sticky Notes 가져오기, 시스템 트레이 연동 등이 이 시기에 추가되었습니다. 이 WPF 구현은 삭제하지 않고 이 저장소의 git history에 그대로 남겨두었습니다(`feat: complete rust rebuild of StickerMemo with Tauri v2` 커밋 이전 기록 참고) — StickerMemo가 실제로 시작된 방식이기 때문입니다.

이후 더 가볍고 반응성 좋은 앱을 만들기 위해 **Rust + Tauri v2**로 전체를 다시 구현했습니다. 이 과정에서 기존 메모 DB 경로와 테이블 스키마는 의도적으로 그대로 유지해, 기존 메모가 별도 마이그레이션 없이 그대로 이어지도록 했습니다. v1.0.0은 이 Rust/Tauri 버전의 첫 정식 릴리스로, 한국어/영어/일본어 다국어 지원(Windows 로케일 자동 감지 및 실시간 전환), Settings 창, 그리고 이번 portable 패키징이 추가되었습니다.

## 저장소 구조

```text
src-tauri/   Rust 백엔드 (Tauri v2): 커맨드, 저장소, i18n, 플랫폼 연동 — 현재 활성 구현
ui/          프런트엔드: 순수 HTML/CSS/JS, 별도 빌드 단계 없음 — 현재 활성 구현
ui/lang/     번역 원본 파일 (ko/en/ja)
docs/        WPF → Rust 리빌드 과정의 개발 기록(분석 보고서, 사용자 문서 아님)
```

기존 .NET/WPF 소스는 현재 `main`의 최신 상태에는 포함되어 있지 않으며, 위에서 언급한 리빌드 커밋 이전 git history에서 그대로 열람할 수 있습니다.

## 내 데이터

메모는 다음 위치에 로컬로 저장됩니다.

```text
%APPDATA%\StickerMemo\AppNotes.db
```

계정도, 클라우드 동기화도, 네트워크 접근도 없습니다. **Import Sticky Notes** 기능을 사용하면 StickerMemo는 Microsoft Sticky Notes 데이터베이스의 임시 복사본만 읽고 원본은 절대 수정하지 않습니다. 다만 중요한 데이터 이전 작업이 늘 그렇듯, 백업을 미리 해 두는 것을 권장합니다.

## 소스에서 빌드하기

요구 사항: [Rust](https://www.rust-lang.org/tools/install) (stable), Windows용 [Tauri v2 사전 준비 사항](https://v2.tauri.app/start/prerequisites/).

```powershell
cd src-tauri
cargo build --release
```

빌드된 실행 파일은 `src-tauri/target/release/sticker-memo.exe`에 생성됩니다.

## Known Issues

- 배포되는 실행 파일은 코드 서명이 되어 있지 않아, 처음 실행 시 Windows SmartScreen 경고("Windows에서 PC를 보호했습니다")가 나타날 수 있습니다 — **추가 정보 → 실행** 을 선택하면 됩니다.

## 기여하기

버그 리포트와 목적이 분명한 수정 제안을 환영합니다. StickerMemo는 의도적으로 작고 단순하게 유지되는 프로젝트이므로, PR은 이 범위를 벗어나지 않도록 부탁드립니다.

## 라이선스

MIT — [LICENSE](LICENSE) 참고. 서드파티 의존성 라이선스는 [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md)에 정리되어 있습니다.
