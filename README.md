# HuliacDev Template

전시·키오스크형 Unity 앱의 공통 기능을 모은 패키지(`com.huliacdev.template`)입니다. `Settings.json` 하나로 현장 설정을 제어하므로, 빌드한 뒤에도 코드 수정 없이 기획 변경에 대응할 수 있습니다.

## 주요 기능

- 현장 설정: `StreamingAssets/Settings.json` 하나로 효과음·폰트·무입력 복귀·프레임레이트·숨은 종료 버튼·자동 재시작을 빌드한 뒤에도 바꿉니다.
- DI 구성: `RootLifetimeScope` 가 로깅·MessagePipe·설정·입력과 씬에 둔 공통 매니저(UI·Fade·Sound·Video, API 로그, Arduino, 무입력 타이머, 예약 종료)를 등록하고, 프로젝트는 `GameManagerBase` 를 상속해 시작합니다.
- 현장 운영: 무입력 복귀, 요일별 예약 종료, 로그 파일 회전·30일 보관. Windows 빌드는 크래시·응답 없음 뒤 자동으로 다시 켜고, 포커스를 잃은 창을 되찾거나 항상 위에 둡니다.
- 장치·네트워크: 시리얼 아두이노(`ArduinoManager`), 랜 장치 UDP·TCP 클라이언트, 사내 로그 서버 전송과 재시도, 인터넷 연결 상태 감시
- 에디터 메뉴: 스택 패키지 한 번에 업데이트, Windows 빌드 뒤 zip 보관·복사, 예약 종료 편집 도구 만들기

## 기술 스택

- Unity 2022.3 이상(검증 2022.3.62f3), 플랫폼: Windows(주 대상) / Android
- 주요 라이브러리: VContainer, UniTask, MessagePipe, R3, ZLogger, ZString, DOTween, Input System, Addressables, TextMeshPro
- 함께 들어 있는 도구: RuntimeInspector(런타임 인스펙터), LogViewer(Reporter)
- 라이선스: MIT

## 폴더 구조

```
Runtime/
├─ App/         RootLifetimeScope(DI 구성 진입점)
├─ Core/        GameManagerBase, InactivityTimer, ShutdownScheduler, WindowFocusRestorer, AutoRestartWatchdog, StateMachine
├─ Data/        Settings 스키마, AppSettingsProvider
├─ Hardware/    ArduinoManager
├─ Input/       TemplateInputActions
├─ Network/     NetworkStatusService, ApiManagerBase, ApiRetryUtil, PacketUtility, UdpCommandClient, TcpLineClient
├─ Prefabs/     SystemCanvas(숨은 종료 버튼)
├─ UI/          UIManager, SoundManager, VideoManager, FadeManager
├─ Utils/       JsonLoader, GameCloser, ConsecutiveClickCounter, SingletonGuard, LogRetentionService
└─ ThirdParty/  RuntimeInspector, LogViewer(Reporter)
Editor/         스택 패키지 업데이트, Windows 빌드·보관 메뉴와 설정, 예약 종료 편집 도구 빌더
Tools~/         ShutdownScheduleEditor(ShutdownSettings.json 편집 프로그램)
Tests/Runtime/  PlayMode 테스트
docs/           설치, 구조, 사용법, Settings.json 안내
```

클래스별 역할과 설계 원칙은 [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)에 있습니다.

## 사용법 및 설정

- 설치: Unity 레지스트리 밖의 의존성을 쓰므로 OpenUPM 스코프 레지스트리 등록 → UPM 의존성 → NuGet 코어 패키지(ZLogger·R3 구현 DLL 등) → DOTween → 이 패키지(Package Manager의 **Add package from git URL**) 순서로 설치합니다. 단계별 방법과 패키지를 개발할 때 쓰는 검증 프로젝트 구성은 [docs/INSTALL.md](docs/INSTALL.md)에 있습니다.
- 시작: 프로젝트의 `GameManager` 는 `GameManagerBase` 를 상속하고, `RootLifetimeScope` 를 상속한 스코프에서 등록합니다. 무입력 복귀, 효과음 미리 불러오기, 외부 API 로그, 랜 장치 연결, 자동 재시작 등 기능별 사용법은 [docs/USAGE.md](docs/USAGE.md)에 있습니다.
- 설정 파일: `Assets/StreamingAssets/Settings.json`(키 설명은 [docs/SETTINGS.md](docs/SETTINGS.md)), 예약 종료 시각은 `Assets/StreamingAssets/ShutdownSettings.json`
- 단축키(기본): `D` 로그 뷰어, `I` 런타임 인스펙터, `M` 마우스 커서 표시, `F` 창 포커스 복구(와 항상 위) 끄기/켜기. 바코드·QR 스캐너를 쓰는 프로젝트는 `ConfigureInputBindings` 를 override해 Ctrl 조합으로 바꿉니다.
- 에디터 메뉴: `Tools > Update Stack Packages`(스택 UPM·NuGet 패키지 업데이트), `Build > Windows Build`(빌드, zip 보관, 복사 폴더에 복사. 경로는 `Edit > Preferences > HuliacDev Build`), `Tools > Build Shutdown Scheduler Exe`
- 변경 기록: [CHANGELOG.md](CHANGELOG.md)
