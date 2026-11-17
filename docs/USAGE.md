# 사용법

기능별 사용법과 에디터 메뉴입니다. 설치는 [INSTALL.md](INSTALL.md), `Settings.json` 키는 [SETTINGS.md](SETTINGS.md)에 있습니다.

## GameManager 확장

```csharp
public class GameManager : GameManagerBase
{
    protected override void Start()
    {
        base.Start();
        // 프로젝트 고유 시작 로직
    }

    protected override void OnSettingsLoaded(Settings loadedSettings)
    {
        // 설정 로드 완료 직후 초기화 로직
    }
}
```

## 프로젝트 이벤트 브로커 등록

프로젝트 고유 MessagePipe 이벤트는 `RootLifetimeScope` 파생 클래스에서 `ConfigureProjectMessageBrokers`를 override해 등록합니다. 템플릿 브로커를 모두 등록한 뒤 `RegisterMessagePipe()`가 돌려준 `options`를 그대로 넘겨 주므로, 템플릿 목록을 복사할 필요가 없습니다.

```csharp
public class GameLifetimeScope : RootLifetimeScope
{
    protected override void ConfigureProjectMessageBrokers(IContainerBuilder builder, MessagePipeOptions options)
    {
        builder.RegisterMessageBroker<QuizAnsweredEvent>(options);
    }
}
```

> `ConfigureMessagePipe`를 override해 목록을 복사하지 마세요. `RegisterMessagePipe()`는 한 번만 부를 수 있어 복사본이 템플릿 목록 전체를 대신하게 되고, 템플릿이 나중에 브로커를 추가하면 그 이벤트를 구독하는 템플릿 컴포넌트가 컨테이너 빌드에서 해석에 실패합니다.

## InactivityTimer (비활동 타이머)

`Settings.json`의 `useInactivityTimer`/`resetTime`에 따라 일정 시간 입력이 없으면 `HuliacDev.App.InactivityTimeoutEvent`(MessagePipe 메시지)를 발행합니다. "최초 화면"이 별도 씬인지, 같은 씬의 첫 패널인지는 프로젝트마다 다르므로, 실제로 되돌아가는 로직(씬 로드, 패널 전환 등)은 이 이벤트를 `ISubscriber<InactivityTimeoutEvent>`로 구독해 프로젝트가 직접 구현합니다.

다른 선택 매니저와 동일하게, 씬에 `InactivityTimer` 컴포넌트를 두면(배치 자체가 사용 선언) `RootLifetimeScope`가 자동으로 찾아 등록합니다. VContainer로 런타임 생성하는 경우도 동작 방식은 동일하며, 두 경우 모두 이벤트 연결은 코드로만 가능합니다(런타임 스폰 특성상 인스펙터 연결은 지원하지 않음).

```csharp
public class PanelManager : MonoBehaviour
{
    private IDisposable _subscription;

    [Inject]
    public void Construct(ISubscriber<InactivityTimeoutEvent> subscriber)
    {
        _subscription = subscriber.Subscribe(_ => ReturnToFirstPanel());
    }

    private void OnDestroy()
    {
        _subscription?.Dispose();
    }

    private void ReturnToFirstPanel()
    {
        // 프로젝트별 복귀 로직
    }
}
```

`ApiManagerBase`를 함께 쓰는 프로젝트라면, 타임아웃으로 복귀했을 때 `move_idle_timeout` 로그가 자동으로 전송됩니다(`ApiManagerBase`가 `InactivityTimeoutEvent`를 직접 구독함). 종료 버튼처럼 사용자가 직접 첫 화면으로 돌아가는 일반적인 경우에는 `IPublisher<MoveIdleEvent>.Publish(new MoveIdleEvent())`를 호출하면 동일하게 `move_idle` 로그가 자동 전송됩니다(`ApiManagerBase`를 직접 참조할 필요 없음). 두 경우 모두 실제 화면 전환 로직과는 무관하므로, 위 `ReturnToFirstPanel` 같은 복귀 함수 안에서 로그를 직접 호출할 필요는 없습니다(아웃트로 등 특정 씬에서 타임아웃 시 정상 관람 완료로 집계하는 등 분기가 필요하면 `ApiManagerBase.OnInactivityTimeout()` / `OnMoveIdle()` 가상 메서드를 override).

긴 영상 재생처럼 입력 없이도 사용자가 실제로는 콘텐츠를 보고 있는 구간에서는 재생 시작/종료 시점에 `Pause()`/`Resume()`을 호출해 그동안 카운트가 멈추도록 합니다.

```csharp
[Inject] private InactivityTimer _inactivityTimer; // 씬 배치형이라면 선택 매니저처럼 IObjectResolver.ResolveOrDefault로 조회하는 것을 권장

void OnVideoStarted() => _inactivityTimer?.Pause();
void OnVideoFinished() => _inactivityTimer?.Resume();
```

## 효과음 미리 불러오기 (SoundManager.PreloadAsync)

`SoundManager`는 처음 트는 클립을 `StreamingAssets`에서 읽고 디코드한 뒤에야 재생합니다. 나레이션처럼 긴 효과음을 화면 연출과 같은 순간에 시작해야 하면, 앱을 켤 때나 그 장면에 들어가기 전에 `PreloadAsync`로 미리 읽어 두세요. 캐시에 있는 클립은 `PlaySFX`·`PlayBGM`이 읽기를 기다리지 않고 바로 재생합니다.

```csharp
// 클립이 캐시에 들어 있으면 true, 없는 키이거나 읽기에 실패하면 false (원인은 로그로 남음)
bool isLoaded = await _soundManager.PreloadAsync("narration", this.GetCancellationTokenOnDestroy());

// 나중에 연출을 시작할 때 같은 프레임에 재생됨
_soundManager.PlaySFX("narration");
```

- 키는 `Settings.json`의 `sounds[].key`입니다. BGM과 효과음이 같은 캐시를 쓰므로 BGM 키도 미리 읽을 수 있습니다.
- `SoundManager`가 사운드 목록을 읽기 전에 불러도(예: `GameManagerBase.OnSettingsLoaded`) 목록을 읽을 때까지 기다린 뒤 진행합니다. `SoundManager` 오브젝트가 꺼져 있어 목록을 읽지 못하면 넘긴 토큰이 취소될 때까지 기다리므로, 호출하는 쪽의 파괴 토큰(`this.GetCancellationTokenOnDestroy()`)을 넘기세요.
- 이미 캐시에 있으면 바로 `true`를 돌려주고, 같은 클립을 읽는 중이면 그 읽기를 함께 기다립니다.
- 넘긴 토큰이 취소되면 `OperationCanceledException`을 던집니다. 이미 시작한 읽기는 같은 클립을 기다리는 재생 요청과 공유되므로 멈추지 않고 끝까지 진행되어 캐시에 들어갑니다(사운드 목록을 기다리던 중에 취소되면 읽기를 시작하지 않습니다).
- 미리 읽은 클립은 다른 클립처럼 `ClearCache()`를 부를 때까지 메모리에 남습니다.
- `StopSFX()`는 재생 중인 효과음과 함께, 읽기를 기다리던 `PlaySFX` 요청도 취소합니다(읽기가 끝나도 재생하지 않음). 미리 불러오기는 취소하지 않습니다.

## 외부 API 호출 로깅 (wavespeed, GPT 등)

프로젝트 내에서 외부 API를 호출할 때 사내 로깅 서버(`Settings.json`의 `apiUrl`)로 호출 및 반환 상태를 전송해야 하는 경우 세 가지 방식 중 상황에 맞춰 사용할 수 있습니다.
- **호출 규격:** `Call API {http://apiaddress+parameter}`
- **반환 규격:** `Return OK` (성공) / `Return fail` 또는 `Return fail: {실패 사유}` (실패, 사유를 알 수 있을 때만 덧붙음)

```csharp
// 방법 1: 래퍼 메서드로 자동 전송 (호출 전 "Call API ~", 성공 시 "Return OK",
// 예외 발생 시 "Return fail: {예외 메시지}" — 예외 메시지는 자동으로 채워지며,
// 메시지에 URL 쿼리 스트링이 있으면 API 키/토큰 유출 방지를 위해 제거 후 전송됨).
// 로그는 기다리지 않고 뒤에서 보내므로 API 호출이 늦어지지 않고, Return은 Call 전송이 끝난 뒤 보냄.
WavespeedResult response = await _apiManager.ExecuteWithExternalApiLoggingAsync(
    "https://api.wavespeed.ai/v1/task?user=123",
    async () => await CallWavespeedAsync());

// 방법 2: ApiManagerBase 직접 호출 (실패 사유는 두 번째 인자로 직접 전달).
// 로그 전송은 재시도로 수십 초 걸릴 수 있어 기다리면 API 호출이 그만큼 늦어지므로 Forget으로 보냄.
// 이때 두 로그가 서버에 도착하는 순서는 보장되지 않으므로, 순서가 중요하면 방법 1을 씀.
_apiManager.SendExternalApiCallLogAsync("https://api.openai.com/v1/chat/completions").Forget();
(bool success, string errorReason) = await CallGptAsync();
_apiManager.SendExternalApiReturnLogAsync(success, errorReason).Forget();

// 방법 3: MessagePipe 이벤트 발행 (ApiManagerBase를 직접 참조하지 않는 경우)
_externalApiCallPublisher.Publish(new ExternalApiCallEvent("https://api.openai.com/v1/chat/completions"));
_externalApiReturnPublisher.Publish(new ExternalApiReturnEvent(true));
_externalApiReturnPublisher.Publish(new ExternalApiReturnEvent(false, "HTTP 500"));
```

## 전송 전 인터넷 연결 확인

`ApiRetryUtil`은 보내기 전에 인터넷에 나갈 수 있는지 확인하고, 나갈 수 없으면 재시도 없이 바로 실패를 돌려줍니다. `NetworkStatusService`도 같은 판정을 씁니다.

- 네트워크 어댑터가 끊겼으면 보내지 않습니다.
- Windows에서는 작업 표시줄의 지구본 아이콘을 정하는 Windows의 판정도 읽습니다. 공유기에는 연결됐지만 인터넷이 끊긴 경우("인터넷 없음")에도 보내지 않습니다. 네트워크 요청 없이 Windows가 가진 값을 읽기만 합니다.
- Windows가 아직 판정하지 못했거나 판정을 읽을 수 없는 환경(Windows 10 2004 이전, Windows가 아닌 환경)에서는 막지 않고 어댑터만 봅니다.
- `ApiManagerBase`의 시작 로그(`start`)는 Windows 판정으로 거르지 않고 보냅니다. Windows가 "인터넷 연결됨"이라고 하지 않는 동안(인터넷 없음 또는 판정 전) 시작 로그가 성공하면 그 현장에서는 Windows 판정을 믿을 수 없는 것으로 보고, Windows가 "인터넷 연결됨"이라고 할 때까지 어댑터만 봅니다. 외부 확인 주소를 막은 망이나 같은 LAN 안 서버로만 보내는 현장을 위한 동작이며, `apiUrl`로 보내는 로그(시작 로그와 아래 확인 전송)가 아닌 요청의 성공은 이 근거로 쓰지 않습니다.
- 시작 로그가 실패했거나(부팅 때 네트워크 어댑터가 아직 연결되지 않았으면 재시도 없이 바로 실패), 보정이 풀린 뒤 다시 막히면 idle·외부 API 로그 하나를 `InternetProbeIntervalSeconds`(기본 300초)마다 한 번 Windows 판정으로 거르지 않고 보내 봅니다. 이 확인 전송은 재시도하지 않으며, 닿으면 시작 로그와 같은 방식으로 보정합니다.
  - 종료 로그는 종료를 늦추지 않도록 확인 전송에 쓰지 않습니다. 보낼 idle·외부 API 로그가 없으면 확인하지 않습니다.
  - 간격을 바꾸려면 `ApiManagerBase`를 상속한 클래스에서 `InternetProbeIntervalSeconds`를 override하세요. 인터넷이 정말 없으면 확인할 때마다 실패 로그가 남습니다.
  - 에디터·Development 빌드는 로그를 실제로 보내지 않으므로 보정되지 않습니다.
- 같은 LAN 안 서버처럼 인터넷 없이 닿는 서버로 보내는 요청은 `apiUrl` 여부와 관계없이 `SendGetRequestWithRetryAsync`·`GetTextWithRetryAsync`에 `requireInternetAccess: false`를 넘기세요. 넘기지 않으면 인터넷이 없는 망에서는 보정되기 전까지 요청을 보내지 않습니다.
- `NetworkStatusService`의 `IsConnected`도 같은 판정이라, 인터넷 없이 LAN 서버만 쓰는 현장에서는 시작 로그나 확인 전송으로 보정되기 전까지 끊긴 것으로 보입니다. 그런 현장에서 LAN 연결 여부가 필요하면 `Application.internetReachability`를 직접 보세요.

## 응답이 필요한 API 호출 (ApiRetryUtil.GetTextWithRetryAsync)

사용자 확인·진행도 조회처럼 응답 본문을 해석해야 하는 API는 `GetTextWithRetryAsync`로 호출합니다. 재시도 간격·횟수와 네트워크 미연결 판정(위 "전송 전 인터넷 연결 확인")은 로그 전송용 `SendGetRequestWithRetryAsync`와 같고, 성공하면 `(true, 본문)`, 네트워크 미연결이거나 재시도를 모두 소진하면 `(false, null)`을 돌려줍니다.

```csharp
(bool isSuccess, string responseText) result = await ApiRetryUtil.GetTextWithRetryAsync(
    url, "user check", _logger, this.GetCancellationTokenOnDestroy(), maxAttemptCount: 3);

if (result.isSuccess)
{
    UserCheckResponse response = JsonUtility.FromJson<UserCheckResponse>(result.responseText);
}
```

- `SendGetRequestWithRetryAsync`와 달리 기본값으로 **에디터·Development 빌드에서도 실제로 전송**해 개발 중에 서버 응답을 확인할 수 있습니다. 운영 서버에 기록이 남는 요청이라 개발 중에는 보내면 안 되면 `skipInEditorAndDevelopmentBuild: true`를 넘기세요.
- HTTP 4xx를 포함한 모든 실패를 재시도합니다. '없는 사용자'를 404로 알리는 API라면 `maxAttemptCount`를 줄여 응답을 오래 기다리지 않게 하세요.
- 시도 한 번이 `attemptTimeoutSeconds`(기본 10초, `ApiRetryUtil.DefaultAttemptTimeoutSeconds`)를 넘기면 그 시도를 실패로 보고 재시도합니다. 연결만 받고 응답하지 않는 서버에서도 무기한 기다리지 않으며, 0 이하를 넘기면 시간 제한을 두지 않습니다.
- 성공 로그는 기본적으로 Information으로 남습니다. 사용자 확인처럼 자주 부르는 API는 `successLogLevel: LogLevel.Debug`(또는 `LogLevel.None`)로 낮추세요. 실패·재시도 로그는 그대로 남습니다.

## 랜 장치 연결 (UdpCommandClient · TcpLineClient)

랜선으로 연결한 아두이노 같은 장치와 주고받는 클라이언트입니다. USB 시리얼 장치는 `ArduinoManager`를 씁니다. 장치가 명령을 받기만 하고 받았다는 표시로 같은 문자열을 돌려주면 `UdpCommandClient`, 장치가 TCP 서버로 접속을 받아 상태를 줄 단위로 보내면 `TcpLineClient`를 씁니다. 둘 다 MonoBehaviour가 아닌 일반 클래스라 LifetimeScope에 등록해 주입받고, 컨테이너가 정리될 때 `Dispose`됩니다.

```csharp
builder.Register<UdpCommandClient>(Lifetime.Singleton);
builder.Register<TcpLineClient>(Lifetime.Singleton);
```

주소·포트처럼 현장에서 바꾸는 값은 `Settings.json`이 아니라 프로젝트의 목적별 JSON(예: `StreamingAssets/DeviceSettings.json`)에 두고 읽어서 넘깁니다.

```csharp
// UDP: 장치가 받은 명령을 보낸 쪽 IP·포트로 그대로 돌려줘야 함(이 클라이언트의 포트는 실행마다 바뀜)
_udp.SetTarget(settings.host, settings.port);   // IP 주소만
bool isAcknowledged = await _udp.SendAsync("1_enter");   // 결과가 필요 없으면 .Forget()

// TCP: 앱 시작 때 한 번 Connect하면 끊겨도 알아서 다시 접속함
_tcp.OnDataReceived.Subscribe(line => HandleLine(line)).AddTo(ref _disposables);
_tcp.OnConnected.Subscribe(_ => ShowDeviceReady()).AddTo(ref _disposables);
_tcp.Connect(settings.host, settings.port);
_tcp.Send("S2\n");   // 줄 끝 문자는 붙이지 않으므로 장치가 줄 단위로 받으면 직접 붙임
```

- 명령은 ASCII로 보내고 받습니다(한글 등은 '?'로 바뀜).
- `UdpCommandClient`는 메인 스레드에서만 부릅니다. 받기가 처음 보낸 호출의 스레드 문맥으로 이어지므로, 그래야 `OnDataReceived`도 메인 스레드에서 옵니다. 응답은 앞뒤 공백을 빼고 비교하므로 줄 끝 문자가 붙어 돌아와도 되지만, 같은 명령을 연달아 보내면 앞 명령의 늦은 응답을 뒤 명령의 응답으로 볼 수 있습니다.
- `UdpCommandClient`는 응답이 없으면 `ReplyTimeoutMs`(기본 3초)마다 `MaxSendAttempts`(기본 3번)까지 보내므로, 장치가 같은 명령을 두 번 받아도 문제없게 만들거나(같은 상태로 두는 명령 등) 대기 시간을 장치의 처리 시간보다 넉넉히 잡습니다. 앱이 끝날 때 장치를 원래 상태로 돌릴 명령은 `Dispose` 전에 `SendAsync`로 넣어 두면 응답을 기다리지 않고 한 번 보냅니다.
- `TcpLineClient`는 첫 접속이 실패해도 같은 간격 규칙으로 계속 다시 접속하므로 `Connect`를 한 번만 부릅니다(다시 부르면 간격이 처음으로 돌아감). 랜선이 빠진 것은 약 12초 안에 알아챕니다.
- `TcpLineClient`는 끊긴 동안의 `Send`를 쌓아 두지 않고 false를 돌려줍니다. 다시 접속한 뒤 예전 명령으로 장치가 움직이지 않게 하려는 것이므로, 접속 뒤에 필요한 명령은 `OnConnected`에서 다시 보냅니다.
- `OnConnected`·`OnDisconnected`는 다음 프레임에 오므로, 버튼 입력처럼 그보다 먼저 올 수 있는 호출에서 장치를 움직이는 명령을 보내기 전에는 `IsConnected`와 `ConnectionId`(접속될 때마다 1씩 늘어나는 번호)를 읽어 처리한 접속과 같은지 확인합니다. 메인 스레드가 오래 멈춘 사이 끊겼다가 다시 접속까지 끝나면 `IsConnected`만으로는 알 수 없습니다.

## 창 포커스 복구 (WindowFocusRestorer, Windows 전용)

전시 앱을 전체 화면으로 켜 두고 USB 바코드·QR 스캐너를 키보드 장치로 읽는 경우를 위한 기능입니다. Windows는 키보드 입력을 포커스가 있는 창에만 보내므로, 알림·업데이트 창·다른 프로그램이 포커스를 가져가면 누가 화면을 터치할 때까지 스캐너 입력이 앱에 들어오지 않습니다. Input System의 Background Behavior로는 해결되지 않습니다(키보드 장치는 백그라운드 입력을 받지 못함).

`RootLifetimeScope`가 `WindowFocusRestorer`를 자동으로 만들므로 씬 배치나 등록 코드는 필요 없습니다. 앱이 포커스를 잃으면 `focusRestoreDelay`초 뒤 앱 창을 다시 앞으로 가져오고, 포커스가 돌아올 때까지 `focusRestoreRetryInterval`초마다 다시 시도합니다.

- **Windows 스탠드얼론 빌드에서만 동작합니다.** 에디터와 다른 플랫폼에서는 아무것도 하지 않습니다.
- **앱을 켜면 켜진 상태로 시작하고, `F` 키(ToggleFocusRestore)로 끄고 켭니다.** 유지보수 중 다른 프로그램을 써야 하면 앱에서 `F`를 눌러 끄고, 끝나면 다시 눌러 켭니다. 앱을 다시 시작하면 켜진 상태로 돌아갑니다. 끄고 켤 때 로그가 남고, 화면 위쪽에 켜짐·꺼짐이 2초 동안 표시됩니다(끌 때는 다시 켜는 키도 함께 표시). 꺼 둔 동안 포커스를 잃으면 "꺼져 있어 창을 되돌리지 않는다"는 로그가 남습니다.
- `Settings.json`에서는 타이밍만 조정합니다([SETTINGS.md](SETTINGS.md) 참고). 파일을 읽지 못하면 기본 타이밍(3초/3초)으로 동작합니다.
- Player Settings의 **Run In Background**가 켜져 있어야 합니다. 꺼져 있으면 포커스를 잃은 동안 플레이어가 멈춰 복구할 수 없으며, 시작 시 경고 로그가 남습니다.
- Windows의 포그라운드 잠금 때문에 `SetForegroundWindow`만으로는 거부되는 경우가 많아, 현재 포그라운드 창의 스레드에 입력을 잠시 붙여(`AttachThreadInput`) 창을 올립니다. 단 포그라운드 창이 응답 없음 상태면 붙이지 않습니다(멈춘 창과 입력을 공유하면 앱도 함께 멈출 수 있음). 그래도 잠금 화면, UAC 확인 창, 관리자 권한으로 실행된 창이 앞에 있으면 Windows가 막을 수 있습니다. 실패하면 포커스를 잃을 때마다 처음 한 번만 경고 로그를 남기고 계속 재시도합니다.
- 앱 종료 중에는 시도하지 않습니다.

### 항상 위 (alwaysOnTop, 선택)

시작 프로그램 폴더 등으로 여러 프로그램을 함께 켜면 실행 순서와 창이 뜨는 순서가 보장되지 않아, 나중에 뜬 다른 프로그램 창이 앱 화면을 가릴 수 있습니다. 포커스 복구는 `focusRestoreDelay`초 뒤에야 창을 되돌리고, 포그라운드 잠금으로 거부되면 계속 가려질 수 있습니다. `Settings.json`에 `"alwaysOnTop": true`를 적으면 앱 창을 항상 위(Topmost)로 두어 다른 창이 앱 위로 올라오지 못하게 합니다.

- 기본값은 꺼짐(`false`)이며, 생략하면 기존 동작과 같습니다. `Settings.json`을 읽지 못해 대체 설정으로 동작할 때도 꺼집니다.
- 앱을 시작할 때 한 번 읽으므로, 값을 바꾸면 앱을 다시 시작해야 합니다(`AppSettingsProvider.ReloadAsync`로 다시 읽어도 반영되지 않으며, 포커스 복구 타이밍도 같습니다).
- 포커스 복구와 함께 동작합니다. 항상 위는 화면 가림만 막고 키보드 포커스는 옮기지 않으므로, 스캐너 입력은 계속 포커스 복구가 되찾습니다.
- **`F` 키(ToggleFocusRestore)로 포커스 복구와 함께 끄고 켭니다.** 켜 두면 작업 관리자·오류 창·다른 프로그램도 앱 뒤에 가려지므로, 점검할 때는 `F`로 끄고 작업한 뒤 다시 켜세요. 화면 위쪽에는 "Focus restore and always on top ON/OFF"가 표시되고, 앱을 다시 시작하면 켜진 상태로 돌아갑니다. 앱을 끌 때는 Alt+F4나 숨은 종료 버튼(`GameCloser`)을 씁니다.
- `Display.Activate`로 연 보조 디스플레이 창에도 함께 적용하고, `F`로 끌 때도 함께 풉니다. 보조 창을 열면 메인 창의 항상 위가 풀리기도 하므로, 1초마다 확인해 항상 위가 아닌 창만 다시 적용합니다.
- 이미 항상 위인 다른 창(화상 키보드, '항상 위' 옵션을 켠 작업 관리자 등)과는 순서를 다투지 않으므로, 그런 창이 나중에 뜨면 앱 위에 보일 수 있습니다. 잠금 화면과 UAC 확인 창도 앱 위에 뜹니다.
- Windows 스탠드얼론 빌드에서만 동작하며, 에디터에서는 적용하지 않습니다.

> **바코드·QR 스캐너 주의:** 스캐너는 읽은 값을 키 입력으로 보내므로, 값에 대문자 `F`가 있으면 포커스 복구(항상 위를 켰으면 항상 위도 함께)가 꺼집니다(`D`·`I`·`M` 단축키도 같은 문제가 있습니다). 스캐너를 쓰는 프로젝트는 아래처럼 Ctrl 조합으로 바꾸세요. 스캐너는 Ctrl을 보내지 않습니다.

## 자동 재시작 (AutoRestartWatchdog, Windows 전용)

현장용 빌드가 크래시하거나 강제로 꺼지거나 '응답 없음'으로 멈췄을 때 앱을 다시 실행합니다. `Settings.json`에 아래처럼 적으면 켜집니다. 키가 없으면 꺼져 있어 기존 동작과 같습니다.

```json
"autoRestart": { "enabled": true, "hangRestartSeconds": 60, "restartDelaySeconds": 5 }
```

`RootLifetimeScope`가 `AutoRestartWatchdog`를 자동으로 만들므로 씬 배치나 등록 코드, 현장 PC의 설치·작업 스케줄러 등록은 필요 없습니다. 앱이 시작할 때 감시 스크립트(`AutoRestartWatchdog.ps1`, `persistentDataPath`에 매번 새로 씀)를 창 없는 PowerShell로 띄우고, 감시 스크립트가 앱 프로세스를 지켜봅니다.

- **종료 코드로 구분합니다.** 숨은 종료 버튼(`GameCloser`), 예약 종료(`ShutdownScheduler`), Alt+F4·창 닫기는 모두 `Application.Quit` 경로로 끝나 종료 코드가 0이므로 다시 켜지지 않고 감시도 함께 끝납니다. 크래시(예: `0xC0000005`)와 `taskkill /F`처럼 프로세스를 바로 끝낸 경우(1)는 0이 아니므로 `restartDelaySeconds`초 뒤 같은 실행 파일과 인수로 다시 켭니다. 앱이 스스로 다시 켜져야 할 때는 아래 "프로젝트 코드에서 쓰기"의 `TryRequestRestart`를 씁니다. 씬에 `ApiManagerBase`를 둔 프로젝트에서 `Application.Quit(코드)`만 부르면 `ApiManagerBase`가 종료 로그를 보낸 뒤 종료 코드 0으로 끝내 다시 켜지지 않습니다. 직접 0이 아닌 코드로 끝내려면 `QuitReason.Set(사유, 코드)`를 먼저 부릅니다.
- **응답 없음:** 창이 Windows에 '응답 없음'으로 판정된 상태(메시지를 5초 넘게 처리하지 못함)가 `hangRestartSeconds`초 이어지면 강제로 끄고 다시 켭니다. 큰 씬·영상 로딩으로 잠깐 막혀도 걸리지 않도록 기본 60초이며, 0이면 이 감지를 끕니다.
- **켜자마자 계속 끝나면** 앱이 60초 안에 끝나는 일이 이어질 때 두 번째부터 재시작 간격을 두 배씩 늘립니다(5초 → 10초 → 20초 …, 최대 10분). 크래시마다 Unity가 덤프를 남기므로 설정 오류 등으로 시작하자마자 죽는 상태에서 디스크가 차지 않게 하기 위함입니다. 60초 넘게 실행되면 간격이 원래대로 돌아옵니다.
- Windows가 종료·로그오프 중이면 다시 켜지 않습니다.
- 다시 켠 앱도 같은 감시 스크립트가 계속 지켜봅니다. 다시 켜진 앱은 시작 로그에 "Restarted by the watchdog ... (exit code -1073741819 (0xC0000005))"처럼 이유를 경고로 남기고(코드에서는 `RestartReason`), 감시를 또 띄우지 않습니다.
- 감시 스크립트가 시작·재시작·종료한 시각과 이유는 앱 로그 폴더(`persistentDataPath/Logs`)의 `AutoRestart.log`에 남습니다.
- 값은 앱을 시작할 때 감시 스크립트에 넘기므로, 바꾸면 앱을 정상 종료(Alt+F4 등)한 뒤 다시 켜야 반영됩니다. 크래시로 다시 켜진 동안에는 처음 띄운 감시의 값이 그대로 쓰입니다.
- 점검 등으로 앱을 끄려면 Alt+F4나 숨은 종료 버튼을 씁니다.
- 작업 관리자의 '작업 끝내기'는 앱이 응답하면 창 닫기와 같은 종료 요청을 보내는 것으로 보여(같은 Windows API `EndTask`로 시험), 앱이 정상 종료하면 다시 켜지지 않습니다. 앱이 응답하지 않을 때는 강제로 끝나므로 다시 켜지고, 응답 없음 감지도 따로 동작합니다.
- 크래시가 이어져 재시작을 기다리는 동안에는 끌 앱 창이 없습니다. 이때 재시작을 멈추려면 감시 스크립트를 끝냅니다. 작업 관리자 **세부 정보** 탭의 열 머리글을 오른쪽 클릭해 **명령줄** 열을 켜고, 명령줄에 `AutoRestartWatchdog.ps1`이 있는 `powershell.exe`를 끝냅니다. `autoRestart.enabled`를 false로 바꾸는 것만으로는 이미 떠 있는 감시가 멈추지 않습니다.
- 재시작을 기다리는 동안 앱을 직접 켜도 됩니다. 직접 켠 앱이 감시를 새로 띄우고, 대기가 끝나 예전 감시가 실행한 앱은 Player Settings의 **Force Single Instance** 때문에 곧바로 정상 종료(종료 코드 0)되므로 예전 감시도 함께 끝납니다. 이때 이미 떠 있는 앱 창이 한 번 앞으로 올 수 있습니다. 감시는 기다리는 동안 앱이 켜졌는지 확인하지 않으므로, Force Single Instance를 끈 프로젝트에서는 앱이 두 개 뜹니다.
- 그룹 정책으로 PowerShell 스크립트 실행을 막아 둔 PC에서는 감시가 뜨지 않을 수 있습니다. 앱을 켠 뒤 `AutoRestart.log`에 "Watching" 줄이 생기는지 확인합니다.
- `Settings.json`을 읽지 못해 대체 설정으로 동작할 때는 감시를 띄우지 않습니다.
- PC 재부팅 뒤 앱을 켜는 일은 하지 않습니다. 기존처럼 시작 프로그램 등으로 켭니다.
- 감시 스크립트만 작업 관리자에서 끄면 그 앱 실행 동안에는 감시가 없습니다. 다음에 앱이 시작될 때 다시 띄웁니다.
- Windows 스탠드얼론 빌드에서만 동작하며, 에디터에서는 띄우지 않습니다. 기능을 아예 빼려면 `ConfigureAutoRestart`를 빈 본문으로 override합니다.

**프로젝트 코드에서 쓰기:** `AutoRestartWatchdog`의 정적 API로 상태를 읽고 재시작을 요청합니다. 환경 변수 이름을 복사해 읽지 않습니다(템플릿이 이름을 바꾸면 컴파일 오류 없이 어긋납니다).

| API | 설명 |
|-----|------|
| `WasRestarted` / `RestartReason` | 이번 실행이 감시가 다시 켠 실행인지와 그 이유(`"exit code -1073741819 (0xC0000005)"`, `"not responding for 60s"`, `"restart requested by the app (exit code 75)"`). 다시 켠 실행이 아니면 false / null |
| `IsWatching` | 감시가 지금 이 앱을 지켜보는지. 감시를 띄운 직후 감시가 앱을 잡을 때까지(보통 몇 초), 꺼져 있거나 에디터이거나 감시 실행에 실패했거나 감시가 끝났으면 false |
| `WaitUntilWatchingAsync(token)` | 감시를 띄울지와 띄운 감시가 지켜보기 시작했는지가 정해질 때까지 기다린 뒤 `IsWatching`을 돌려줌. `ConfigureAutoRestart`를 빈 본문으로 override하면 끝나지 않으므로 시간 제한 토큰을 넘김. 토큰이 취소되면 `OperationCanceledException`이고, 그 뒤 코드는 취소가 일어난 스레드에서 이어지므로 시간 제한은 `CancelAfterSlim`(PlayerLoop에서 취소)으로 검 |
| `TryRequestRestart(reason)` | 감시가 지켜보고 있으면 종료 코드 75(`RestartRequestedExitCode`)로 끝내 `restartDelaySeconds`초 뒤 다시 켜지게 하고 true. 지켜보고 있지 않거나 다른 종료(숨은 종료 버튼·예약 종료·Alt+F4)가 이미 진행 중이면 끄지 않고 false. 종료 로그에는 `end (by {reason})`로 남음 |

```csharp
// 예: 모니터가 다시 연결되면 앱을 다시 켜서 해상도를 새로 잡음. 앱 시작 직후에도 부를 수 있으면 먼저 감시 결과를 기다림.
// 시간 제한은 CancelAfterSlim으로 걸어 메인 스레드(PlayerLoop)에서 취소되게 함. CancelAfter는 스레드 풀 타이머에서 취소하므로
// 시간이 다 되면 아래 코드가 메인 스레드 밖에서 이어지고, 그 뒤에 화면을 바꾸는 등 Unity API를 부르면 오류가 남.
using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
using IDisposable timeoutTimer = timeout.CancelAfterSlim(TimeSpan.FromSeconds(30), DelayType.Realtime);
(bool isCanceled, bool isWatching) = await AutoRestartWatchdog.WaitUntilWatchingAsync(timeout.Token).SuppressCancellationThrow();

// 다시 켜진 뒤 같은 이유로 계속 재시작하지 않도록, 감시가 다시 켠 실행이면 한 번 더 요청하지 않음.
if (!isCanceled && isWatching && !AutoRestartWatchdog.WasRestarted)
{
    AutoRestartWatchdog.TryRequestRestart("Monitor Reconnect");
}
```

앱이 60초 안에 다시 요청하는 일이 이어지면 크래시와 같이 재시작 간격을 두 배씩 늘립니다. 프로젝트가 직접 `Application.wantsToQuit`에서 false를 돌려 종료를 취소(확인 창 등)한다면 취소할 때 `QuitReason.Clear()`를 부릅니다. 부르지 않으면 취소된 재시작 요청의 종료 코드 75가 남아, 이후 Alt+F4로 끌 때 감시가 다시 켭니다.

## 예약 종료 (ShutdownScheduler)

`StreamingAssets/ShutdownSettings.json`(`Settings.json`과 별개 파일)에 적은 시각이 되면 Windows `shutdown` 명령으로 PC 종료를 예약하고 앱을 정상 종료합니다. 씬에 `ShutdownScheduler`를 두면 `RootLifetimeScope`가 자동으로 찾아 등록하고, 두지 않으면 동작하지 않습니다.

- 예정 시각은 기본 15초마다 확인합니다(인스펙터의 `checkIntervalSeconds`). 시각이 되면 `BeforeShutdownEvent`를 발행하므로, 페이드아웃 같은 마무리는 이 이벤트를 구독해 연결합니다.
- 요일별 기본 스케줄이 있고, `dateOverrides`에 등록한 날짜는 그날의 요일 스케줄 대신 적용됩니다.
- `shutdownArguments`가 비어 있으면 `-s -f -t 45`(45초 뒤 강제 종료)를 씁니다. 지연 시간은 종료 로그를 보낼 여유입니다.
- 예정 시각이 지난 뒤에 앱을 켜면 그날은 건너뜁니다. 켜자마자 다시 꺼지는 것을 막기 위함입니다.
- 파일은 `Tools > Build Shutdown Scheduler Exe`로 만든 편집 도구로 고칩니다. 메뉴를 실행하면 도구를 게시해 현재 프로젝트의 `StreamingAssets`로 복사하며, 약 150MB라 git에 커밋하지 않습니다. 파일 형식과 앱이 멈췄을 때를 대비한 작업 스케줄러 백업은 `Tools~/ShutdownScheduleEditor/README.md`를 참고하세요.

## 단축키 (기본 제공)

| 키 | 동작 (System 맵 액션) |
|---|---|
| `D` | Reporter 로그 뷰어 토글 (ToggleDebug) |
| `I` | 런타임 인스펙터 토글 (ToggleInspector) |
| `M` | 커서 표시 토글 (ToggleMouse) |
| `F` | 창 포커스 복구 끄기/켜기, `alwaysOnTop`을 켰으면 항상 위도 함께 (ToggleFocusRestore) |

> 기본 바인딩은 `Runtime/Input/TemplateInputActions.inputactions`에 있습니다. 패키지 파일을 고치지 말고, 프로젝트의 `RootLifetimeScope` 파생 클래스에서 `ConfigureInputBindings`를 override해 바꾸세요. 컨테이너가 `TemplateInputActions`를 만든 직후, 어떤 컴포넌트가 받아 켜기 전에 호출됩니다.

```csharp
public class GameLifetimeScope : RootLifetimeScope
{
    protected override void ConfigureInputBindings(TemplateInputActions inputActions)
    {
        // 다른 키 하나로 바꾸기
        inputActions.System.ToggleDebug.ApplyBindingOverride("<Keyboard>/f12");

        // Ctrl+F 조합으로 바꾸기 (스캐너 입력과 겹치지 않게)
        InputAction toggleFocus = inputActions.System.ToggleFocusRestore;
        toggleFocus.ChangeBinding(0).Erase();
        toggleFocus.AddCompositeBinding("OneModifier")
            .With("Modifier", "<Keyboard>/ctrl")
            .With("Binding", "<Keyboard>/f");
    }
}
```

> `RootLifetimeScope`를 쓰지 않거나 `ConfigureCoreComponents`에서 `TemplateInputActions` 등록을 뺀 프로젝트는 `GameManagerBase`가 입력 액션을 직접 만들므로 이 override가 적용되지 않고, 포커스 복구 토글 키도 동작하지 않습니다(포커스 복구는 켜진 채로 동작). 이때 `alwaysOnTop`을 켜면 항상 위도 끌 수 없으므로 시작할 때 경고 로그가 남습니다.

## 에디터 메뉴

### 스택 패키지 업데이트 (Tools > Update Stack Packages)

이 템플릿 스택의 UPM 패키지(VContainer, UniTask, MessagePipe, R3, ZLogger, ZString, NuGetForUnity, MCP for Unity, 이 패키지)만 골라 최신으로 올리고, 이어서 NuGetForUnity 창의 **Updates** 탭에 뜨는 NuGet 패키지(정식 버전)를 모두 올립니다. URP·Input System·Addressables처럼 프로젝트가 버전을 고정한 UPM 패키지는 건드리지 않습니다.

- UPM 패키지는 한 번의 요청으로 함께 올립니다. 하나씩 올리면 패키지가 바뀔 때마다 다시 컴파일되어 다음 요청이 "exclusive access" 오류로 실패하거나 남은 순서가 사라졌습니다.
- UPM 업데이트로 다시 컴파일되더라도 NuGet 업데이트는 컴파일이 끝난 뒤 이어서 실행됩니다.
- NuGetForUnity는 패키지를 다시 설치할 때 의존성을 최소 버전으로 다시 깔아, 먼저 올린 의존성이 도로 내려갈 수 있습니다(예: ZLogger 2.6.0을 설치하며 `System.IO.Hashing`이 8.0.0으로). 그래서 업데이트가 없을 때까지 최대 3번 올리고, 마지막에 한 번 더 확인합니다. 그래도 남으면 경고를 남깁니다.
- NuGetForUnity가 없는 프로젝트에서는 NuGet 단계를 건너뜁니다.
- 결과는 콘솔에 `[PackageUpdater]`로 남습니다. 업데이트 뒤에는 컴파일 오류와 테스트를 확인하세요.

### 빌드와 보관 (Build > Windows Build)

Build Settings의 씬 목록과 **Development Build** 설정으로 Windows 64비트 빌드를 프로젝트의 `Build/` 폴더(`<Product Name>.exe`)에 만들고, 성공하면 이어서 아래를 합니다. 이전 빌드의 파일이 섞이지 않도록 빌드 전에 `Build/` 폴더를 비우므로, 이 폴더에는 다른 파일을 두지 않습니다(빌드한 앱이 실행 중이라 비우지 못하면 알리고 멈춥니다).

1. **빌드 보관 폴더**가 정해져 있으면 `<보관 폴더>/<프로젝트 폴더 이름>/<YYMMDD>-<N>/`에 버전 관리용 zip(`<프로젝트 폴더 이름>_v<bundleVersion>.zip`)과 `BuildInfo.txt`(버전, 빌드 시각과 그날 몇 번째 빌드인지, git 브랜치·커밋, 커밋하지 않은 변경이 있었는지. git 값은 빌드 전에 읽음)를 둡니다. N은 그날 몇 번째로 보관한 빌드인지이며(예: `261103-1`, `261103-2`), 빌드마다 새 폴더를 만들어 이전 보관본을 덮어쓰지 않습니다.
2. **복사 폴더**가 정해져 있고 그 폴더가 있으면(드라이브가 연결돼 있으면) '빌드 뒤 복사' 설정에 따라 묻거나 바로 `<복사 폴더>/<프로젝트 폴더 이름>/`에 빌드 폴더를 압축 없이 복사합니다(현장 PC로 옮길 빌드, 항상 최신 빌드만 둠). 복사 전에 `<복사 폴더>/<프로젝트 폴더 이름>/`을 비우므로 그 폴더에 따로 넣어 둔 파일도 지워집니다. 다른 프로그램이 그 폴더의 파일을 쓰고 있어 비우지 못하면 복사하지 않고 오류 로그를 남깁니다. 묻는 창(**Copy Build**)에서 **Don't ask again**을 체크하면 누른 버튼(**Copy** / **Skip**)에 따라 '항상 복사'나 '복사 안 함'으로 바뀝니다. 보관이 실패해도 복사는 합니다.

Unity가 함께 만드는 현장에 보내면 안 되는 폴더(`*_BurstDebugInformation_DoNotShip`, IL2CPP의 `*_BackUpThisFolder_ButDontShipItWithYourGame`)는 zip과 복사에서 모두 뺍니다.

두 폴더와 '빌드 뒤 복사'(매번 묻기 / 항상 복사 / 복사 안 함)는 **Edit > Preferences > HuliacDev Build**(또는 **Build > Setting**)에서 정합니다. 팀원·PC마다 다를 수 있어 프로젝트가 아니라 그 PC의 Unity 환경설정(EditorPrefs)에 저장되며, 그 PC의 모든 프로젝트가 같은 값을 씁니다. 보관 폴더가 정해지지 않았거나 찾을 수 없으면(드라이브가 연결되지 않음) 빌드 전에 **Open Settings** / **Build Without Archiving** / **Cancel** 중에서 고릅니다. 두 경로는 저장소에 들어가지 않으므로, 저장소를 받은 팀원은 처음 빌드할 때 자기 경로를 정합니다.

- 설치 태그는 붙이지 않습니다. 현장에 설치한 버전은 `BuildInfo.txt`의 커밋을 보고 따로 태그합니다.
- 빌드할 때 URP가 URP 에셋의 셰이더 필터 값(`m_Prefilter…`)을 프로젝트에서 쓰는 기능에 맞춰 다시 쓰므로, 처음 빌드하면 URP 에셋이 바뀐 것으로 보일 수 있습니다. 설정이 같으면 Development 여부와 관계없이 다음 빌드도 같은 값을 쓰므로 한 번 커밋해 두면 다시 생기지 않습니다. 되돌리면 빌드할 때마다 다시 생기고, 다음 빌드의 `BuildInfo.txt`에 커밋하지 않은 변경으로 남습니다.
- 프로젝트의 `.gitignore`에 `/[Bb]uild/`가 있어야 빌드 결과가 git에 들어가지 않습니다.
