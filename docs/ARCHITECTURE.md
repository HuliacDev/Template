# 구조

패키지의 폴더·클래스 구성과 설계 원칙입니다. 개요는 [README](../README.md)에 있습니다.

```
Runtime/
├─ App/        RootLifetimeScope    — DI 컨테이너 구성 진입점
├─ Core/       GameManagerBase      — 순수 DI 게임 매니저 기반 클래스
│              InactivityTimer      — 무입력 감지 및 복귀 이벤트
│              ShutdownScheduler    — 자동 종료 스케줄러
│              WindowFocusRestorer  — 포커스를 잃은 앱 창 복구·항상 위 (Windows)
│              AutoRestartWatchdog  — 비정상 종료·응답 없음 시 자동 재시작 (Windows)
│              StateMachine · IState — 상태 머신
├─ Data/       Settings 스키마 + AppSettingsProvider
├─ Hardware/   ArduinoManager       — 시리얼 통신 및 자동 재연결
├─ Input/      TemplateInputActions
├─ Network/    NetworkStatusService — R3 기반 실시간 네트워크 모니터링
│              ApiManagerBase       — 서버 상태 로깅 베이스
│              ApiRetryUtil         — 네트워크 예외 격리 재시도
│              PacketUtility        — 바이너리 패킷과 구조체 변환
│              UdpCommandClient     — 랜 장치에 UDP 명령 전송(응답 확인·재전송)
│              TcpLineClient        — 랜 장치와 TCP 줄 단위 통신(재접속·keepalive)
├─ Prefabs/    SystemCanvas         — 숨은 종료 버튼이 들어 있는 시스템 캔버스
├─ UI/         UIManager · SoundManager · VideoManager · FadeManager
├─ Utils/      SingletonGuard<T> · JsonLoader · GameCloser · ConsecutiveClickCounter · SystemCanvas
│              LogRetentionService  — 로그 파일 회전 및 만료 정리
└─ ThirdParty/ RuntimeInspector · LogViewer(Reporter)
Editor/
├─ PackageUpdater          — Tools > Update Stack Packages (스택 UPM 패키지와 NuGet 업데이트)
├─ BuildMenu               — Build > Windows Build (빌드, 버전 관리용 zip 보관, 복사 폴더에 복사)
├─ BuildMenuPreferences    — Edit > Preferences > HuliacDev Build (보관·복사 경로, PC별)
├─ BuildCopyPromptWindow   — 빌드 뒤 복사 확인 창
└─ ShutdownScheduleEditorBuilder — Tools > Build Shutdown Scheduler Exe
Tools~/
└─ ShutdownScheduleEditor  — ShutdownSettings.json 편집 도구 (독립 Windows 프로그램)
Tests/
└─ Runtime/                — PlayMode 테스트
```

## 주요 클래스 및 기능

| 모듈 | 클래스 | 주요 기능 및 역할 |
|---|---|---|
| **Core** | `GameManagerBase` | 앱 수명주기 총괄, VContainer DI 기반 비동기 초기화, `OnSettingsLoaded` 가상 훅 제공 |
| | `InactivityTimer` | 키오스크 무입력 시간 감지 및 자동 복귀 이벤트(`InactivityTimeoutEvent`) 발행 |
| | `ShutdownScheduler` | 일별/요일별 자동 종료 스케줄링 및 사전 알림(`BeforeShutdownEvent`) 발행 |
| | `WindowFocusRestorer` | 앱 창이 포커스를 잃으면 다시 앞으로 가져와 키보드형 스캐너 입력 유지, 선택적으로 창을 항상 위로 둠 (Windows 스탠드얼론, `F` 키로 끄고 켬) |
| | `AutoRestartWatchdog` | 크래시·강제 종료·응답 없음으로 앱이 끝나면 숨은 감시 스크립트가 다시 실행, 정상 종료(숨은 종료 버튼·예약 종료·창 닫기)는 다시 켜지 않음 (Windows 스탠드얼론, `autoRestart`로 켬) |
| | `StateMachine<TContext>` | `IState<TContext>` 기반 상태 머신. 전환할 때 상태를 새로 만들지 않고 미리 만든 인스턴스의 참조만 바꿈 |
| **UI & Media** | `UIManager` | uGUI/TextMeshPro 비동기 폰트 프리로드/일괄 적용, 텍스처 VRAM 압축(`compress`) 제어. `Set*`가 새로 붙이는 이미지·글자·영상은 클릭을 받지 않도록 `raycastTarget`을 끄고(대상에 Button 등 Selectable이 있거나 버튼 배경이면 켬), 대상에 다른 Graphic이 이미 있으면 붙이지 않고 경고를 남김 |
| | `FadeManager` | 화면 페이드 인/아웃(DOTween/UniTask), 커스텀 색상(`Color? color`) 및 `SortingOrder` 제어 |
| | `SoundManager` | BGM/SFX 비동기 재생, 미리 불러오기(`PreloadAsync`), 마스터/BGM/SFX 개별 볼륨 및 음소거(`Mute`), DOTween 페이드 충돌 방어 |
| | `VideoManager` | 비디오 비동기 프리로드/재생(`PrepareAndPlayAsync`는 재생을 시작했는지 bool로 돌려줌), 고아 RenderTexture 메모리 최적 해제 ($O(N+M)$) |
| **Network** | `NetworkStatusService` | R3 기반 실시간 네트워크 도달성/끊김/복구 모니터링, Windows의 인터넷 판정 반영 (오프라인 키오스크 초기 알림 억제) |
| | `ApiManagerBase` | 시작/종료/비활동/외부 API 호출 이력 사내 서버 전송 추상 베이스 클래스 |
| | `ApiRetryUtil` | 고정 간격 재시도 및 소켓/DNS 네트워크 예외 격리를 지원하는 WebRequest 안정적 재시도, 응답 본문 반환(`GetTextWithRetryAsync`), 인터넷이 없으면 전송 생략 |
| | `PacketUtility` | 바이너리 패킷과 구조체 변환(`FromBytes<T>`, `ToBytes<T>`, `GetPacketSize<T>`) |
| | `UdpCommandClient` | 랜으로 연결한 장치에 UDP로 명령을 보내고, 장치가 같은 문자열을 돌려주는지로 확인. 응답이 없으면 정한 횟수까지 다시 보내고 명령은 순서대로 하나씩 보냄 |
| | `TcpLineClient` | 랜으로 연결한 장치(TCP 서버)에 접속을 유지하며 줄 단위로 받고 문자열을 보냄. 끊기면 간격을 늘려 가며 다시 접속, keepalive로 랜선 빠짐 감지, 이벤트는 메인 스레드에서 발행 |
| **Logging** | `LogRetentionService` | 일별 로그 파일 회전(`AddZLoggerRollingFile`) 및 30일 보관 주기 만료 파일 자동 정리 |
| **Hardware** | `ArduinoManager` | 시리얼 통신, 읽기 스레드 예외 격리 및 메인 스레드 마샬링 기반 자동 재연결(`AutoReconnect`, 연결된 뒤 끊겼을 때만 동작하며 `AutoReconnectIntervalMs`(기본 3초)에서 두 배씩 늘려 `AutoReconnectMaxIntervalMs`(기본 30초)에서 멈춤). 연결 변화는 이벤트(`OnConnected`·`OnDisconnected`)라 현재 상태는 `IsConnected`로 읽음. 포트를 열면 Uno·Nano 계열은 리셋돼 첫 줄까지 약 1.7초가 걸리므로 핸드셰이크를 `HandshakeTimeoutMs`(기본 3초) 동안 기다림. 모든 포트를 동시에 확인해 포트 수와 상관없이 약 1.7초에 연결됨 |
| **Data & Utils**| `AppSettingsProvider` | `Settings.json` 단일 로드/캐싱 및 런타임 핫 리로드(`ReloadAsync`) 지원, 읽기 실패 시 비활동 타이머를 켠 대체 설정 사용 |
| | `JsonLoader` | `StreamingAssets` 및 `PersistentData` 지원, `.json` 확장자 자동 보정 직렬화, 읽기 성공 여부 반환(`TryLoadAsync`), 임시 파일을 거친 저장과 저장 성공 여부 반환 |
| | `ConsecutiveClickCounter` | "N초 안에 M번 누르기" 연속 클릭 판정 (`GameCloser`가 사용, 운영자용 숨은 버튼에 재사용) |
| | `SingletonGuard<T>` | `DontDestroyOnLoad` 중복 방어 일원화, 즉시 비활성화 및 에디터 도메인 리로드 자가 복구 |

## 설계 원칙

- **싱글톤 대신 DI** — 전역 `Instance` 접근을 쓰지 않고 `RootLifetimeScope`를 통해 주입합니다.
- **코루틴 대신 UniTask** — 파일 I/O와 연출 로직 전부 async. 프레임 드랍과 GC 할당을 줄입니다.
- **`Settings.json` 단일 로드** — `AppSettingsProvider`가 로드를 일원화하여 공유합니다. 매니저가 개별 로드하지 않습니다.
- **로깅은 ZLogger** — `Debug.Log` 대신 주입받은 `ILogger<T>`를 사용합니다.
- **리플렉션 최소화** — 런타임 코드에 `System.Reflection` 직접 사용이 없습니다. DI 주입도 소스 제너레이터로 대체할 수 있습니다([설치 6단계](INSTALL.md#6-vcontainer-소스-제너레이터-선택-권장)). 인스펙터 `UnityEvent` 바인딩 대신 코드로 직접 연결하는 것(`GameCloser`)도 같은 맥락입니다.

> **예외:** `ThirdParty/Plugins/RuntimeInspector`는 런타임 필드 조사가 존재 이유이므로 리플렉션을 사용합니다.
> 디버그 전용이라 릴리즈 빌드에서 제외하면 해당 비용도 사라집니다.

> **참고:** `JsonUtility`는 Unity의 네이티브 직렬화 백엔드를 쓰므로 관리 코드 리플렉션이 아닙니다.
> 다만 런타임 타입 주도 방식이라 IL2CPP 코드 스트리핑에 취약합니다. 빌드에서 설정이 비어 나온다면
> `link.xml`이나 `[Preserve]`로 `HuliacDev.Data` 타입을 보존하세요.

> **ZLogger 사용 시 주의:** 보간 문자열 핸들러가 `ref struct`라 널 조건부 연산자(`?.`)와 충돌합니다.
> `_logger?.ZLogInformation(...)`이 아니라 `if (_logger != null) _logger.ZLogInformation(...)` 형태로 작성하세요.
