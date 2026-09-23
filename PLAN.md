# Keyboard Pet — 개발 계획서

작성일: 2026-09-22
대상 OS: Windows 10 / Windows 11 (x64)

---

## 1. 프로그램 개요

키보드 타이핑 이벤트를 **전역(Global)** 으로 감지하여, 모달이 아닌(non-modal) 작은 창에
여러 장의 이미지를 연속 재생해 **애니메이션처럼 보여주는 데스크톱 위젯**이다.
사용자가 어떤 프로그램에서 타이핑하든 반응해야 하므로, 포커스와 무관하게 키 입력을 받아야 한다.

### 핵심 기능 요약

| # | 기능 | 설명 |
|---|------|------|
| F1 | 전역 키 입력 감지 | 앱이 포커스를 갖지 않아도 키 다운 이벤트 수신 |
| F2 | 비모달 애니메이션 창 | 테두리 없음, 투명 배경, 드래그 이동, 작업표시줄 숨김 |
| F3 | 항상 위(Topmost) ON/OFF | 트레이 메뉴·설정창·단축키로 토글, 설정 저장 |
| F4 | 이미지 세트(프레임) 로드 | 여러 장의 PNG/JPG/BMP/GIF를 순서대로 로드하여 메모리 캐시 |
| F5 | 루프 애니메이션 | 마지막 프레임 후 첫 프레임으로 되돌아감 |
| F6 | 프레임 전환 모드 | ① 고정 간격(초) ② 랜덤 간격(최소~최대) ③ 타수 기반(N타마다 1프레임) |
| F7 | 키별 이미지 매핑 | 특정 키(또는 키 그룹) 입력 시 지정된 이미지 세트로 전환 |
| F8 | 설정 저장/복원 | JSON 파일로 영구 저장, 실행 시 자동 복원 |
| F9 | 시스템 트레이 | 트레이 아이콘 + 컨텍스트 메뉴(Topmost, 설정, 종료) |

### 개인정보/보안 원칙
- 전역 키 훅을 사용하지만 **어떤 키가 눌렸는지 기록·저장·전송하지 않는다.**
- 메모리에서도 "현재 키 코드"와 "타수 카운터"만 순간적으로 다루며, 로그 파일을 남기지 않는다.
- 이 원칙을 README와 앱 정보 화면에 명시한다(백신 오탐 대응 및 사용자 신뢰).

---

## 2. 개발 언어 및 기술 스택 추천

### 추천: **C# + WPF (.NET 10 LTS)**

| 판단 기준 | 평가 |
|-----------|------|
| 전역 키 훅 | `SetWindowsHookEx(WH_KEYBOARD_LL)` 를 P/Invoke 한 줄로 호출 가능. WPF 자체가 메시지 루프를 갖고 있어 별도 처리 불필요 |
| 투명·비모달·Topmost 창 | `WindowStyle=None`, `AllowsTransparency=True`, `Topmost` 속성으로 XAML만으로 해결 |
| 이미지/애니메이션 | `BitmapImage` + `DispatcherTimer`로 프레임 교체. GPU 합성으로 부드럽고 CPU 부담 적음 |
| 설정 UI | XAML 데이터 바인딩(MVVM)으로 설정창을 빠르게 구축 |
| 트레이 아이콘 | `H.NotifyIcon.Wpf` 패키지 또는 WinForms `NotifyIcon` 참조 |
| 배포 | 단일 exe(self-contained, 약 70MB) 또는 프레임워크 의존(약 2MB). MSIX/Inno Setup 가능 |
| 개발 도구 | Visual Studio 2022 Community 무료, 디자이너·디버거 완비 |
| 러닝커브 | C#은 문법이 평이하고 Windows 데스크톱 자료가 가장 풍부 |

### 후보 비교

| 언어/프레임워크 | 장점 | 단점 | 결론 |
|-----------------|------|------|------|
| **C# / WPF** | 위 표 참고. 요구사항 전부를 표준 API로 커버 | Windows 전용(요구사항상 문제 없음) | **1순위** |
| C# / WinUI 3 | 최신 UI, Win11 룩앤필 | 투명 창·훅 처리가 WPF보다 번거로움, 패키징 제약 | 차순위 |
| C++ / Win32 | 최고 성능, 최소 바이너리 | 창·이미지·설정 UI 전부 수작업, 생산성 낮음 | 비추천 |
| Python / PySide6 + pynput | 프로토타입 빠름 | 배포 용량 큼(100MB 이상), 훅 지연·안정성, 백신 오탐 잦음 | 비추천 |
| Electron / Tauri | 웹 기술 활용 | 전역 훅에 네이티브 애드온 필요, 메모리 과다(Electron), 투명 창 이슈 | 비추천 |
| Rust / egui·iced | 안전·성능 | 생태계 미성숙, 훅·트레이·투명 창 조합에 삽질 많음 | 비추천 |

### 확정 스택
- 언어: C# / .NET 10 (LTS). 환경 제약이 있으면 .NET 8 LTS로 낮춰도 코드 변경 없음
- UI: WPF (MVVM, `CommunityToolkit.Mvvm`)
- 트레이: `H.NotifyIcon.Wpf`
- 설정 직렬화: `System.Text.Json`
- 테스트: xUnit (Core 라이브러리 대상)
- 빌드/배포: `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true`

---

## 3. 아키텍처

### 3.1 솔루션 구조

```
KeyboardPet.sln
├─ src/
│  ├─ KeyboardPet.Core/            # WPF 의존 없는 순수 로직 (테스트 대상)
│  │  ├─ Animation/
│  │  │  ├─ FrameSet.cs            # 이미지 세트 메타(이름, 프레임 수, 원본 파일)
│  │  │  ├─ AnimationOptions.cs    # FrameMode + 모드별 파라미터(정규화 포함)
│  │  │  ├─ AnimationEngine.cs     # 현재 프레임 인덱스, 루프, 모드 전환
│  │  │  ├─ IFrameScheduler.cs     # 프레임 전환 정책 인터페이스
│  │  │  ├─ FixedIntervalScheduler.cs
│  │  │  ├─ RandomIntervalScheduler.cs
│  │  │  ├─ KeystrokeScheduler.cs
│  │  │  └─ AdaptiveScheduler.cs   # 타이핑 속도 연동(M6)
│  │  ├─ Input/
│  │  │  └─ AutoRepeatDetector.cs  # 키 반복(Auto-repeat) 추정 (LL 훅은 반복 여부를 주지 않음)
│  │  ├─ Rules/
│  │  │  ├─ KeyNames.cs            # 키 이름 ↔ 가상 키 코드 표
│  │  │  ├─ KeySpec.cs             # "Ctrl+S", "*" 스펙 파싱·매칭
│  │  │  ├─ KeyRule.cs             # 키 → 세트 매핑 규칙(HoldMs, ResetIndex)
│  │  │  ├─ KeyRuleController.cs   # 활성 세트 상태, 유지 시간 후 기본 세트 복귀
│  │  │  └─ RuleMatcher.cs         # 입력 키에 대한 규칙 탐색
│  │  ├─ Settings/
│  │  │  ├─ AppSettings.cs         # 설정 모델
│  │  │  └─ SettingsStore.cs       # JSON 로드/저장
│  │  └─ Abstractions/
│  │     ├─ IClock.cs / IFrameTimer.cs # 테스트용 시간 추상화
│  │     └─ IKeyboardSource.cs     # 키 이벤트 소스 추상화
│  └─ KeyboardPet.App/             # WPF 실행 프로젝트
│     ├─ App.xaml(.cs)             # 싱글 인스턴스, DI 구성, 트레이 초기화
│     ├─ Input/
│     │  └─ LowLevelKeyboardHook.cs   # WH_KEYBOARD_LL P/Invoke
│     ├─ Windows/
│     │  ├─ PetWindow.xaml         # 애니메이션 표시 창
│     │  └─ SettingsWindow.xaml    # 설정 창
│     ├─ ViewModels/
│     │  ├─ ShellViewModel.cs      # 트레이·PetWindow 공유 상태, 설정과 양방향 동기화
│     │  ├─ SettingsViewModel.cs   # 설정 창(5탭), 즉시 적용, 키 캡처
│     │  ├─ FrameSetItemViewModel.cs # 세트 행(이름·폴더·미리보기·상태)
│     │  └─ RuleItemViewModel.cs   # 규칙 행(키·세트·유지·검증)
│     ├─ Services/
│     │  ├─ SettingsService.cs     # 단일 설정 원천, Changed 이벤트, 0.5초 디바운스 저장
│     │  ├─ StartupService.cs      # 로그인 시 자동 실행(HKCU Run 레지스트리)
│     │  ├─ ImageCache.cs          # 폴더/내장 리소스 → Freeze된 프레임(GIF 전개, 자연 정렬)
│     │  ├─ AnimationService.cs    # 엔진+캐시 결합, 프레임 → ShellViewModel.CurrentFrame
│     │  ├─ KeyboardInputService.cs # 훅 구동, 키 이벤트 → 상태 반영
│     │  ├─ ContextMenuFactory.cs  # 트레이/창 공용 컨텍스트 메뉴
│     │  └─ TrayService.cs
│     └─ Assets/                   # 기본 샘플 이미지, 아이콘
├─ scripts/
│  └─ publish.ps1              # 단일 exe 게시 스크립트 (artifacts/win-x64)
├─ README.md
├─ tests/
│  └─ KeyboardPet.Core.Tests/
└─ PLAN.md (본 문서)
```

### 3.2 데이터 흐름

```
[OS 키보드] → LowLevelKeyboardHook (훅 콜백, 즉시 반환)
                 │  Channel<KeyEvent> (스레드 안전 큐)
                 ▼
          KeyboardInputService (Dispatcher 스레드)
                 │
    ┌────────────┼────────────────┐
    ▼            ▼                ▼
RuleMatcher  KeystrokeScheduler  타수 카운터
    │            │
    ▼            ▼
      AnimationEngine (활성 FrameSet, 현재 인덱스)
                 │  FrameChanged 이벤트
                 ▼
          PetWindow (Image.Source 교체)
```

### 3.3 핵심 컴포넌트 설계

#### (a) LowLevelKeyboardHook
- `SetWindowsHookEx(WH_KEYBOARD_LL, proc, hModule, 0)`
- 콜백은 **반드시 수 ms 안에 반환**해야 한다(Windows는 느린 훅을 자동 제거함). 콜백 안에서는
  `Channel.Writer.TryWrite(new KeyEvent(vk, isDown))` 만 수행하고 `CallNextHookEx` 로 넘긴다.
- 델리게이트를 필드에 보관하여 GC 수거를 방지한다.
- `WM_KEYDOWN`/`WM_SYSKEYDOWN` 만 타수로 계산. 키 반복(Auto-repeat) 포함 여부는 설정으로 제공.
- 앱 종료 시 `UnhookWindowsHookEx` 보장(`Dispose`, `ProcessExit`).
- 제약: 관리자 권한으로 실행 중인 다른 프로그램에 입력 중일 때는 UIPI 때문에 이벤트를 받지 못한다.
  → 설정에 "관리자 권한으로 재시작" 옵션 제공(선택 사항).

#### (b) AnimationEngine
```
상태: ActiveSet, FrameIndex, Scheduler
메서드:
  SetActiveSet(FrameSet set, bool resetIndex)
  Advance()            → FrameIndex = (FrameIndex + 1) % ActiveSet.Count ; FrameChanged 발생
  OnKeystroke()        → Scheduler.OnKeystroke() 전달
  ApplyMode(FrameMode) → Scheduler 교체
```
- 프레임 수가 1장이면 Advance 는 무동작.
- 세트 전환 시 인덱스를 0으로 초기화할지 유지할지는 규칙별 옵션.

#### (c) IFrameScheduler 구현 3종

| 모드 | 동작 | 주요 설정 |
|------|------|-----------|
| Fixed | 타이머가 `Interval` 마다 `Advance()` | `IntervalMs` (기본 200ms, 범위 16~10000) |
| Random | 프레임마다 `[MinMs, MaxMs]` 균등 난수로 다음 간격 재산출 | `MinMs`, `MaxMs`, `Seed`(선택) |
| Keystroke (기본값) | 키 다운 `N` 회마다 `Advance()`. 타이머 없음 | `KeysPerFrame`(기본 1), `IdleReturnMs`(무입력 시 0번 프레임 복귀, 0이면 비활성) |
| Adaptive | 최근 `WindowMs` 동안의 타수(타/초)로 간격을 `SlowMs`~`FastMs` 사이에서 선형 보간. 무입력 `IdleReturnMs` 후 0번 프레임으로 복귀·정지 | `AdaptiveSlowMs`(600), `AdaptiveFastMs`(80), `AdaptiveTargetKeysPerSecond`(6), `AdaptiveWindowMs`(2000), `IdleReturnMs` 공유 |

- "Fixed + Keystroke 가속" 혼합 모드는 M6에서 Adaptive 모드로 구현했다.

#### (d) 키별 이미지 매핑 (RuleMatcher)
```json
{
  "rules": [
    { "keys": ["Enter"],            "frameSet": "jump",  "holdMs": 800,  "resetIndex": true },
    { "keys": ["Space"],            "frameSet": "blink", "holdMs": 300,  "resetIndex": true },
    { "keys": ["A","S","D","F"],    "frameSet": "left",  "holdMs": 0,    "resetIndex": false },
    { "keys": ["Ctrl+S"],           "frameSet": "save",  "holdMs": 1200, "resetIndex": true }
  ],
  "defaultFrameSet": "idle"
}
```
- 규칙은 **위에서부터 첫 매칭 우선**.
- `holdMs > 0` 이면 해당 시간이 지난 뒤 `defaultFrameSet` 으로 복귀. `0` 이면 다음 규칙 매칭 전까지 유지.
- `frameIndex`(M7): 값이 있으면 세트를 재생하지 않고 그 프레임 한 장만 정지 표시한다. 엔진은 고정(Pinned) 상태가 되어 스케줄러의 전진을 무시하며, 복귀 시 고정이 풀린다.
- 루프 프레임(M8): 세트의 `animationFrames`에 든 파일만 루프를 돌고, 나머지(키 전용)는 `frameIndex` 규칙으로만 표시된다. 엔진의 Advance/ResetToFirst는 `FrameSet.LoopFrames` 순서를 따른다.
- 복귀 프레임(M10): 세트의 `idleFrame`이 있으면 무입력 복귀 시 루프 첫 프레임 대신 그 프레임을 보여준다(키 전용 프레임 가능). 타수 기반·타이핑 속도 연동 스케줄러는 시작 시에도 복귀 프레임으로 대기한다.
- 세트 프로필(M11): 규칙과 애니메이션 옵션은 `setProfiles[기본 세트]`를 우선 적용하고 없으면 최상위 값을 쓴다. 모든 편집(설정 창·트레이 메뉴)은 현재 기본 세트의 프로필에 기록된다.
- 조합키(Ctrl/Shift/Alt) 지원: 훅에서 modifier 상태를 `GetAsyncKeyState` 로 함께 읽는다.
- 매칭되지 않는 키는 타수 카운트만 증가시키고 세트는 유지.

#### (e) PetWindow
- `WindowStyle="None" AllowsTransparency="True" Background="Transparent"
   ShowInTaskbar="False" ResizeMode="NoResize" Topmost="{Binding IsTopmost}"`
- 마우스 왼쪽 드래그로 이동(`DragMove`), 위치는 설정에 저장.
- 마우스 오른쪽 클릭 → 컨텍스트 메뉴(Topmost 토글, 설정, 종료).
- 옵션: 클릭 통과(`WS_EX_TRANSPARENT`), 배율(0.25~4.0), 불투명도.
- DPI: `PerMonitorV2` 선언, 이미지 원본 픽셀 기준 배율 적용.
- Topmost 유지 보강: 전체화면 앱 뒤로 밀리는 경우 대비하여 주기적(예: 2초) `SetWindowPos(HWND_TOPMOST)` 재적용 옵션.

#### (f) ImageCache
- 파일 → `BitmapImage`(`CacheOption=OnLoad`, `Freeze()`) 로 로드하여 UI 스레드 외에서도 안전하게 공유.
- GIF 파일은 `BitmapDecoder` 로 프레임 분해하여 세트로 변환.
- 세트 하나당 최대 프레임 수/총 메모리 상한(예: 200장, 256MB)을 두고 초과 시 경고.
- 파일 변경 감지(`FileSystemWatcher`) 로 세트 폴더 수정 시 자동 리로드(v1.1 후보).

#### (g) 설정 파일
- 경로: `%AppData%\KeyboardPet\settings.json`
- 이미지 세트는 폴더 경로 참조 방식(원본 유지) + "앱 데이터로 복사" 옵션.
```json
{
  "version": 1,
  "isTopmost": true,
  "window": { "x": 1500, "y": 800, "scale": 1.0, "opacity": 1.0, "clickThrough": false },
  "frameMode": "Fixed",
  "fixed":     { "intervalMs": 200 },
  "random":    { "minMs": 100, "maxMs": 600 },
  "keystroke": { "keysPerFrame": 1, "idleReturnMs": 2000, "countAutoRepeat": false },
  "frameSets": [   // frames(M7): 생략하면 폴더 전체를 파일명 순, 지정하면 그 파일만 그 순서로
    { "name": "idle",  "folder": "C:\\pets\\cat\\idle" },
    { "name": "jump",  "folder": "C:\\pets\\cat\\jump" }
  ],
  "defaultFrameSet": "idle",
  "rules": [ ],
  "startWithWindows": false
}
```
- 저장 실패/손상 시 기본값으로 복구하고 백업 파일(`settings.bak`) 생성.

---

## 4. 설정 화면 구성 (SettingsWindow)

| 탭 | 항목 |
|----|------|
| 일반 | 항상 위 ON/OFF, 창 배율, 불투명도, 클릭 통과, Windows 시작 시 실행, 키 반복 카운트 여부 |
| 이미지 세트 | 세트 목록(추가/삭제/이름 변경), 폴더 선택, 프레임 미리보기 스트립, 정렬 순서(파일명 자연정렬) |
| 애니메이션 | 모드 선택(고정/랜덤/타수), 각 모드 파라미터 슬라이더+숫자 입력, 미리보기 재생 |
| 키 매핑 | 규칙 목록(드래그로 우선순위 변경), 키 캡처 버튼("키를 누르세요"), 대상 세트, 유지 시간, 인덱스 초기화 |
| 정보 | 버전, 개인정보 원칙, 라이선스 |

- 설정창 역시 비모달로 열어 실시간으로 PetWindow에 반영(즉시 적용, 저장은 자동).

---

## 5. 개발 단계(마일스톤)

| 단계 | 목표 | 산출물 | 예상 기간 |
|------|------|--------|-----------|
| M0 ✅ 완료(2026-09-22) | 프로젝트 골격 | 솔루션/프로젝트 생성, DI·MVVM 세팅, 빈 트레이 앱 실행 | 0.5일 |
| M1 ✅ 완료(2026-09-22) | 전역 훅 + 투명 창 | 키 누르면 디버그 출력에 카운트, 투명 창에 정지 이미지 1장 표시, Topmost 토글 | 1일 |
| M2 ✅ 완료(2026-09-22) | 애니메이션 엔진 | FrameSet 로드, 고정/랜덤/타수 3모드 루프 재생, Core 단위 테스트 | 1.5일 |
| M3 ✅ 완료(2026-09-22) | 키별 매핑 | RuleMatcher, holdMs 복귀, 조합키 지원 | 1일 |
| M4 ✅ 완료(2026-09-22) | 설정 UI + 저장 | SettingsWindow 5개 탭, JSON 저장/복원, 즉시 반영 | 2일 |
| M5 ✅ 완료(2026-09-22) | 마무리 | 자동 시작, DPI/멀티모니터 검증, 단일 exe 배포, 아이콘/샘플 이미지, README | 1일 |
| M6 ✅ 완료(2026-09-22) | 타이핑 속도 연동 모드 | Adaptive 스케줄러(최근 타수 → 간격 보간, 무입력 복귀), 설정 탭·트레이 메뉴 연동, 기본 모드를 타수 기반으로 변경 | 0.5일 |
| M7 ✅ 완료(2026-09-22) | 프레임 편집 · 단일 프레임 규칙 | 세트 프레임 순서 드래그앤드롭·제외(FrameSetSettings.Frames), 규칙별 특정 프레임 정지 표시(KeyRule.FrameIndex, 엔진 고정 상태) | 0.5일 |
| R1 ✅ 완료(2026-09-22) | 코드 리뷰 1회 · 최적화 | 정확성 6건 수정(대체 세트 프레임 고정, 조합키 타수 제외, 틱 카운터 랩, 드래그 후보 잔존, DragMove 예외, 최소화 창 복원), 효율 3건(파일 단위 디코딩 캐시, 훅 콜백 경량화, 무이동 클릭 저장 생략), 미사용 필드 제거 | 0.5일 |
| M8 ✅ 완료(2026-09-22) | 애니메이션 프레임 선택 | 세트별 루프 참여 프레임 지정(FrameSetSettings.AnimationFrames → FrameSet.LoopFrames), 나머지는 키 전용 프레임. 타일 체크박스, 규칙 콤보 "키 전용" 표시 | 0.5일 |
| M9 ✅ 완료(2026-09-22) | Bootstrap 스타일 UI · 키보드 아이콘 | Themes/Bootstrap.xaml(버튼·입력·콤보·체크·라디오·슬라이더·탭·카드·컨텍스트 메뉴·툴팁·스크롤바 템플릿)을 App.xaml에 병합, 검은 키보드 app.ico | 0.5일 |
| R2 ✅ 완료(2026-09-22) | Windows 10 시작 크래시 수정 | H.NotifyIcon ForceCreate의 효율 모드(SetProcessInformation, Idle 우선순위) 비활성화, 트레이 생성 실패 시 대체 아이콘→트레이 없이 실행, 시작 단계별 예외 격리, 훅 콜백 예외 차단, AppDomain/Task 미처리 예외 crash.log 기록, DispatcherUnhandledException Handled 처리, app.ico BMP 항목 재생성 | 0.5일 |
| R3 ✅ 완료(2026-09-23) | 시작 진단 강화 · 포터블 배포 | 프로세스 시작 직후부터 startup.log 단계 추적(OS·런타임·인수 포함), --no-tray / --no-hook 옵션, 임시 폴더 추출이 없는 포터블 zip 게시(publish.ps1 -Portable) | 0.5일 |
| M10 ✅ 완료(2026-09-23) | 무입력 복귀 프레임 · 드래그앤드롭 가져오기 | 세트별 idleFrame(FrameSet.IdleFrameIndex, 엔진 ReturnToIdle, 스케줄러 시작 시 복귀), 세트 탭에 파일/폴더 드롭(폴더→세트, 파일→앱 폴더 복사 후 새 세트, 카드 위→해당 세트에 추가), publish.ps1 -All. 릴리스에는 항상 exe+포터블 zip | 0.5일 |
| M11 ✅ 완료(2026-09-23) | 빈 세트 만들기 · 세트별 설정 프로필 | "새 세트" 버튼(앱 관리 폴더 생성, 카드 드롭으로 채움, 빈 세트는 안내 표시), AppSettings.SetProfiles(세트 이름 → AnimationOptions·Rules)와 EffectiveAnimation/EffectiveRules, 기본 세트 전환 시 프로필 적용·없으면 현재 설정 복사, 세트 이름 변경 시 프로필 이동, 탭 상단 편집 대상 안내 | 0.5일 |
| M12 ✅ 완료(2026-09-23) | 설정의 이미지 세트 귀속 · 예시 세트 | 공통 Animation/Rules 제거(설정 v2, SettingsMigration으로 v1 자동 변환), 키 규칙에서 FrameSet 제거 → 사용 중인 세트의 프레임만 다룸(DisplayRequest), 세트 없는 프로필은 세트 기본값(예시 세트만 샘플 규칙), 세트 삭제 시 프로필 삭제, 애니메이션·키 매핑 탭 상단에 세트 선택. 내장 샘플은 jump 이미지만 남겨 '예시' 세트로 변경 | 0.5일 |
| **합계** | | | **약 7일** |

각 마일스톤 종료 시 실행 가능한 상태를 유지한다(항상 동작하는 빌드).

---

## 6. 테스트 계획

### 단위 테스트 (KeyboardPet.Core.Tests)
- `AnimationEngine`: 루프 wrap-around, 1장 세트, 세트 전환 시 인덱스 초기화 여부
- `FixedIntervalScheduler`: 가짜 타이머로 N틱 후 프레임 인덱스 검증
- `RandomIntervalScheduler`: 고정 시드로 간격이 `[min,max]` 범위 내인지, 매 프레임 재산출되는지
- `KeystrokeScheduler`: `keysPerFrame=3` 일 때 3타마다 Advance, idleReturn 동작
- `RuleMatcher`: 우선순위, 조합키, 미매칭 시 null
- `SettingsStore`: 라운드트립 직렬화, 손상 파일 복구, 버전 마이그레이션

### 수동/통합 테스트 체크리스트
- [ ] 메모장, 브라우저, 게임(전체화면 창 모드)에서 타이핑 시 반응
- [ ] Topmost ON: 다른 창 위에 유지 / OFF: 일반 창처럼 가려짐
- [ ] 랜덤 모드에서 간격이 실제로 변하는지 육안 확인
- [ ] 타수 모드에서 키 반복(길게 누름) 설정 ON/OFF 차이
- [ ] 100% / 150% / 200% DPI, 모니터 간 이동 시 크기 일관성
- [ ] 200장 세트 로드 시 메모리·시작 시간 측정
- [ ] 24시간 방치 후 메모리 누수 없음(훅 해제, 타이머 정리)
- [ ] 관리자 권한 앱(예: 관리자 CMD)에 입력 시 반응 없음 → 안내 문구 노출 확인
- [ ] 로그오프/절전 복귀 후 훅 정상 동작

---

## 7. 리스크 및 대응

| 리스크 | 영향 | 대응 |
|--------|------|------|
| 백신이 전역 키 훅을 키로거로 오탐 | 설치·실행 차단 | 키 값 미저장 원칙 명문화, 코드 서명 인증서 적용, 오픈소스 공개 |
| 트레이 라이브러리 효율 모드가 일부 Windows 10에서 실패 | 시작 직후 크래시(0xE0434352) | R2: 효율 모드 비활성화, 트레이 생성 실패 시 대체·무트레이 실행, 모든 미처리 예외 로그 |
| LL 훅 콜백 지연으로 훅이 자동 해제됨 | 어느 순간 반응 멈춤 | 콜백에서 큐 쓰기만 수행, 훅 생존 감시(Watchdog) 후 자동 재설치 |
| 관리자 권한 창 입력 미수신(UIPI) | 일부 상황 무반응 | 안내 + "관리자 권한으로 실행" 옵션 |
| `AllowsTransparency` 창의 렌더링 비용 | 고해상도 이미지에서 CPU 상승 | 이미지 사전 리사이즈, 프레임 상한, 배율 캐시 |
| 전체화면 게임 위에 표시 불가 | 게임 사용자 불만 | 독점 전체화면은 OS 제약임을 문서화, "테두리 없는 창 모드" 권장 |
| DispatcherTimer 정밀도(약 15ms) | 16ms 이하 간격 부정확 | 최소 간격 16ms로 제한, 필요 시 `CompositionTarget.Rendering` 사용 |

---

## 8. 향후 확장(v2 후보)
- ~~타이핑 속도(WPM)에 따라 간격이 자동으로 빨라지는 혼합 모드~~ → M6에서 구현
- 마우스 클릭/스크롤 이벤트 반응
- 세트를 zip 패키지로 배포/가져오기(커뮤니티 스킨)
- 다중 펫(창 여러 개) 동시 실행
- 사운드 효과 재생
- APNG/WebP/Lottie 애니메이션 포맷 지원

---

## 9. 시작 명령 (M0 착수)

```bash
dotnet new sln -n KeyboardPet
dotnet new classlib -n KeyboardPet.Core -o src/KeyboardPet.Core -f net10.0
dotnet new wpf      -n KeyboardPet.App  -o src/KeyboardPet.App  -f net10.0-windows
dotnet new xunit    -n KeyboardPet.Core.Tests -o tests/KeyboardPet.Core.Tests -f net10.0
dotnet sln add src/KeyboardPet.Core src/KeyboardPet.App tests/KeyboardPet.Core.Tests
dotnet add src/KeyboardPet.App reference src/KeyboardPet.Core
dotnet add tests/KeyboardPet.Core.Tests reference src/KeyboardPet.Core
dotnet add src/KeyboardPet.App package CommunityToolkit.Mvvm
dotnet add src/KeyboardPet.App package H.NotifyIcon.Wpf
```
