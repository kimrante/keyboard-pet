# Keyboard Pet

키보드를 칠 때마다 반응하는 작은 데스크톱 펫입니다. 어떤 프로그램에서 타이핑하든 화면 위의 투명한 창에
여러 장의 이미지가 연속 재생되어 애니메이션처럼 보이고, 특정 키를 누르면 지정한 이미지 세트로 바뀝니다.

- 대상 OS: Windows 10 / 11 (x64)
- 기술: C# / WPF / .NET 10
- 설정 파일: `%AppData%\KeyboardPet\settings.json`

## 주요 기능

| 기능 | 설명 |
|------|------|
| 전역 키 입력 반응 | 앱이 포커스를 갖지 않아도 키 입력에 반응합니다. |
| 투명 비모달 창 | 테두리 없음, 투명 배경, 드래그로 이동, 작업표시줄에 표시되지 않음 |
| 항상 위 ON/OFF | 트레이 메뉴, 펫 창 우클릭 메뉴, 설정 창 어디서나 토글 |
| 이미지 세트 | 폴더 하나가 세트 하나. PNG/JPG/BMP/GIF를 파일명 순서대로 프레임으로 사용 (GIF는 프레임 단위로 전개) |
| 루프 애니메이션 | 마지막 프레임 다음에 첫 프레임으로 돌아갑니다. |
| 프레임 전환 4가지 | 고정 간격 / 랜덤 간격(최소~최대, 프레임마다 재산출) / **타수 기반**(기본값. N타마다 1프레임, 무입력 시 첫 프레임 복귀) / 타이핑 속도 연동(최근 타수에 따라 간격을 느린 값~빠른 값 사이에서 자동 조절) |
| 키별 이미지 매핑 | 예: `Enter` → jump 세트를 0.8초, `Ctrl+S` → save 세트, `*`(모든 키) → typing 세트 |
| 즉시 적용·자동 저장 | 설정 창의 모든 변경이 바로 반영되고 0.5초 뒤 저장됩니다. |
| 기타 | 배율, 불투명도, 클릭 통과, 타수 표시, Windows 로그인 시 자동 실행 |

## 개인정보

Keyboard Pet은 전역 키보드 훅으로 키 입력 **이벤트**만 받습니다.
어떤 키가 눌렸는지는 기록·저장·전송하지 않으며, 메모리에서도 규칙 매칭과 타수 계산에만 순간적으로 사용됩니다.
네트워크 통신을 하지 않습니다. 관련 코드는 `src/KeyboardPet.App/Input/LowLevelKeyboardHook.cs`와
`src/KeyboardPet.App/Services/KeyboardInputService.cs`에서 확인할 수 있습니다.

> 백신 프로그램이 전역 키 훅을 사용하는 프로그램을 의심 파일로 표시할 수 있습니다.
> 위 두 파일을 열어 보면 키 값을 저장하는 코드가 없음을 확인할 수 있습니다.

## 실행

### 배포된 exe로 실행

`KeyboardPet.exe`를 실행하면 트레이에 고양이 아이콘이 나타나고, 펫 창이 작업 영역 우하단에 표시됩니다.
자체 포함 빌드는 .NET 설치가 필요 없습니다.

### 소스에서 실행

[.NET 10 SDK](https://dotnet.microsoft.com/download)가 필요합니다.

```bash
dotnet run --project src/KeyboardPet.App
```

## 사용법

| 동작 | 방법 |
|------|------|
| 설정 열기 | 트레이 아이콘 왼쪽 클릭, 또는 트레이/펫 창 우클릭 → 설정 |
| 펫 옮기기 | 펫 창을 드래그 (위치는 자동 저장) |
| 항상 위 토글 | 우클릭 메뉴 → 항상 위 |
| 애니메이션 모드 바꾸기 | 우클릭 메뉴 → 애니메이션 모드, 또는 설정 → 애니메이션 탭 |
| 종료 | 우클릭 메뉴 → 종료 |

### 이미지 세트 만들기

1. 폴더 하나에 프레임 이미지들을 넣습니다. 파일명 순서(자연 정렬: `frame-2` < `frame-10`)가 재생 순서입니다.
2. 설정 → 이미지 세트 → **폴더 추가...** 로 폴더를 고릅니다. 폴더 이름이 세트 이름이 됩니다(수정 가능).
3. **기본 세트** 콤보박스에서 평소에 보여줄 세트를 고릅니다.

내장 세트 `idle`, `jump`, `typing`은 같은 이름의 사용자 세트가 없을 때만 쓰입니다.
같은 이름으로 세트를 추가하면 내장 세트를 대체합니다.
투명 배경 PNG를 권장하며, 표시 크기는 원본 픽셀 × 배율입니다(이미지 파일의 DPI 정보는 무시).

**프레임 순서 편집과 제외**: 세트 행의 썸네일 타일을 드래그해 순서를 바꾸고, 타일의 × 로 특정 프레임을 제외할 수 있습니다.
편집한 목록은 설정에 저장되며(`frames` 배열), 그 뒤 폴더에 새로 넣은 파일은 자동으로 포함되지 않습니다.
**폴더 순서로 되돌리기**를 누르면 편집을 버리고 폴더의 파일을 파일명 순서로 다시 읽습니다.

**애니메이션 프레임과 키 전용 프레임**: 타일의 체크박스를 풀면 그 프레임은 루프 애니메이션에서 빠지고
키 매핑의 프레임 선택에서만 쓰입니다(콤보박스에 "키 전용"으로 표시). 예를 들어 4장 중 1~3번만 체크해 두고
`Enter` → 4번 프레임 규칙을 만들면, 평소에는 1~3번이 반복되고 Enter를 칠 때만 4번이 나타납니다.
설정에는 `animationFrames` 배열로 저장되며, 생략하면 모든 프레임이 애니메이션에 참여합니다.

### 키 매핑 규칙

설정 → 키 매핑 탭에서 규칙을 추가합니다. **위에서부터 먼저 맞는 규칙**이 적용되므로
`Ctrl+S` 규칙은 `S` 규칙보다 위에 두어야 합니다.

| 항목 | 의미 |
|------|------|
| 키 | 쉼표로 여러 개. `Enter`, `Ctrl+S`, `Ctrl+Shift+A`, `*`(모든 키, 조합키 단독 제외) |
| 세트 | 전환할 이미지 세트 |
| 프레임 | "전체 애니메이션"이면 세트를 재생하고, 특정 프레임을 고르면 그 한 장만 정지 표시합니다(`frameIndex`, 0부터) |
| 유지(ms) | 이 시간이 지나면 기본 세트로 복귀. 0이면 다른 규칙이 맞을 때까지 유지 |
| 처음부터 | 전환할 때 첫 프레임부터 다시 재생 (전체 애니메이션일 때만 의미 있음) |

예를 들어 기본 세트의 3번 프레임이 "놀란 얼굴"이라면, `Enter` → 기본 세트 · 3번 프레임 · 500ms 규칙을 두면
Enter를 칠 때마다 0.5초 동안 놀란 얼굴이 보였다가 원래 애니메이션으로 돌아갑니다.

조합키를 쓰지 않은 규칙(`A`)은 Shift로 대문자를 쳐도 반응하고, 조합키를 명시한 규칙(`Ctrl+S`)은 조합 상태가
정확히 같을 때만 반응합니다. **키 캡처** 버튼을 누른 뒤 원하는 키를 누르면 이름을 몰라도 추가할 수 있습니다.

사용 가능한 키 이름: `A`~`Z`, `0`~`9`, `F1`~`F24`, `Enter`, `Space`, `Tab`, `Escape`, `Backspace`, `Delete`,
`Insert`, `Home`, `End`, `PageUp`, `PageDown`, `Up`/`Down`/`Left`/`Right`, `NumPad0`~`NumPad9`, `CapsLock`,
`한영`(`Hangul`), `한자`(`Hanja`), `Semicolon`, `Comma`, `Period`, `Slash`, `Minus`, `Equals` 등.
표에 없는 키는 `0x41`처럼 가상 키 코드를 16진수로 적을 수 있습니다.

### 설정 파일

`%AppData%\KeyboardPet\settings.json` 에 저장됩니다. 직접 편집해도 되며(주석과 후행 쉼표 허용),
손상된 경우 `.corrupt-날짜` 이름으로 보관하고 기본값으로 시작합니다. 이전 파일은 `settings.json.bak`으로 남습니다.

```json
{
  "isTopmost": true,
  "window": { "scale": 1.0, "opacity": 1.0, "clickThrough": false, "showCounter": true },
  "animation": { "mode": "Keystroke", "fixedIntervalMs": 200, "randomMinMs": 100, "randomMaxMs": 600,
                 "keysPerFrame": 1, "idleReturnMs": 2000,
                 "adaptiveSlowMs": 600, "adaptiveFastMs": 80, "adaptiveTargetKeysPerSecond": 6, "adaptiveWindowMs": 2000 },
  "countAutoRepeat": false,
  "startWithWindows": false,
  "frameSets": [ { "name": "cat", "folder": "C:\\pets\\cat",
                   "frames": [ "idle-1.png", "idle-2.png", "blink.png" ],
                   "animationFrames": [ "idle-1.png", "idle-2.png" ] } ],
  "defaultFrameSet": "cat",
  "rules": [
    { "keys": ["Enter"], "frameSet": "jump",   "holdMs": 800, "resetIndex": true },
    { "keys": ["Space"], "frameSet": "cat",    "holdMs": 400, "resetIndex": true, "frameIndex": 2 },
    { "keys": ["*"],     "frameSet": "typing", "holdMs": 600, "resetIndex": false }
  ]
}
```

## 빌드와 배포

```bash
dotnet build KeyboardPet.slnx
dotnet test KeyboardPet.slnx
```

단일 exe 게시(자체 포함, .NET 설치 불필요, 약 145MB, 실행 중 메모리 약 160MB):

```powershell
.\scripts\publish.ps1
```

exe 크기를 줄인 압축 빌드(약 65MB). 대신 실행 중 메모리를 약 70MB 더 씁니다:

```powershell
.\scripts\publish.ps1 -Compress
```

.NET 10 Desktop Runtime이 설치된 PC용 소형 빌드:

```powershell
.\scripts\publish.ps1 -FrameworkDependent
```

포터블 zip(폴더 형태, 자체 포함). 단일 exe는 첫 실행 때 네이티브 DLL을 `%TEMP%\.net\KeyboardPet\` 아래에 풀어서 쓰는데,
백신이나 AppLocker, TEMP 실행 제한 정책이 있는 PC에서는 이 단계가 막혀 앱이 조용히 실행되지 않을 수 있습니다.
그런 PC에서는 zip을 풀어 `KeyboardPet.exe`를 실행하세요:

```powershell
.\scripts\publish.ps1 -Portable
```

결과물은 `artifacts\win-x64\` 아래에 생성됩니다. Visual Studio에서는 게시 프로필
`Properties\PublishProfiles\win-x64-single.pubxml`을 사용할 수 있습니다.

## 프로젝트 구조

```
src/KeyboardPet.Core     WPF 의존 없는 순수 로직 (애니메이션 엔진, 스케줄러, 규칙, 설정) — 단위 테스트 대상
src/KeyboardPet.App      WPF 앱 (전역 훅, 펫 창, 트레이, 설정 창, 이미지 캐시)
tests/KeyboardPet.Core.Tests   xUnit 테스트
scripts/publish.ps1      단일 exe 게시 스크립트
PLAN.md                  개발 계획서와 진행 기록
```

## 알려진 제한

- 관리자 권한으로 실행 중인 다른 프로그램에 입력할 때는 Windows 보안 정책(UIPI) 때문에 키 입력을 받지 못합니다.
  필요하면 Keyboard Pet도 관리자 권한으로 실행하세요.
- 독점 전체화면 게임 위에는 창이 표시되지 않습니다. 게임을 "테두리 없는 창" 모드로 설정하세요.
- 프레임 간격의 실질 최소값은 약 16ms이며, 세트당 프레임 수 상한은 500장입니다.
- 클릭 통과를 켜면 펫 창을 직접 클릭할 수 없으므로 메뉴는 트레이 아이콘에서 여세요.

## 문제 해결

- 예기치 않은 오류가 나면 `%LocalAppData%\KeyboardPet\crash.log`에 기록됩니다. 시작 실패, 트레이 생성 실패, 훅 오류 등이 모두 여기에 남으므로 문제를 보고할 때 이 파일을 첨부해 주세요.
- 트레이 아이콘을 만들 수 없는 환경에서는 트레이 없이 실행되며 안내 창이 뜹니다. 이때 설정과 종료 메뉴는 펫 창을 마우스 오른쪽 버튼으로 눌러 열 수 있습니다.
- Windows 10에서 시작 직후 "알 수 없는 소프트웨어 예외 (0xe0434352)"가 뜨던 문제는 v1.3.1에서 수정됐습니다(트레이 라이브러리의 효율 모드 호출이 일부 Windows 10 환경에서 실패). v1.3.1 이상으로 업데이트하세요.
- **시작 진단**: 실행할 때마다 `%LocalAppData%\KeyboardPet\startup.log`가 새로 만들어지며, OS 버전과 시작 단계가 순서대로 기록됩니다. 어느 단계 뒤에서 멈췄는지 이 파일로 알 수 있습니다.
  - `startup.log`조차 생기지 않으면 .NET 런타임이 뜨기 전에 실패한 것입니다. Windows 10은 1607(2016년 8월) 이상이어야 하며, 단일 exe 대신 **포터블 zip**을 써 보세요. 이벤트 뷰어의 Windows 로그 → 응용 프로그램에서 ".NET Runtime" 또는 "Application Error" 항목도 확인하세요.
  - 원인 분리용 실행 옵션: `KeyboardPet.exe --no-tray`(트레이 생략), `KeyboardPet.exe --no-hook`(키보드 훅 생략).
- 절전 복귀나 잠금 해제 후 반응이 없으면 앱이 훅을 자동으로 다시 설치합니다. 그래도 반응이 없으면 앱을 재시작하세요.
- 설정을 전부 초기화하려면 설정 → 정보 → **모든 설정 기본값으로 복원**을 누르거나 `settings.json`을 삭제하세요.
