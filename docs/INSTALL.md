# 설치

이 패키지를 프로젝트에 설치하는 순서와, 패키지를 개발·수정할 때 쓰는 검증 프로젝트 구성입니다. 개요는 [README](../README.md)에 있습니다.

이 패키지는 Unity 레지스트리 밖의 의존성을 사용하므로 **순서대로** 설치해야 합니다.

## 1. 스코프 레지스트리 등록 (OpenUPM)

프로젝트의 `Packages/manifest.json`에 다음을 추가합니다.

```json
{
  "scopedRegistries": [
    {
      "name": "package.openupm.com",
      "url": "https://package.openupm.com",
      "scopes": [
        "com.cysharp",
        "jp.hadashikick"
      ]
    }
  ]
}
```

## 2. UPM 의존성 설치

Package Manager의 **Add package by name**으로 아래를 설치합니다. 괄호는 검증된 버전입니다.

| 패키지 | ID | 용도 |
|---|---|---|
| VContainer | `jp.hadashikick.vcontainer` (1.19.0) | DI 컨테이너 |
| UniTask | `com.cysharp.unitask` (2.5.11) | async/await (zero-allocation) |
| ZLogger | `com.cysharp.zlogger` (2.6.0) | zero-GC 구조적 로깅 |
| ZString | `com.cysharp.zstring` (2.6.0) | zero-allocation 문자열 결합 |
| R3 | `com.cysharp.r3` (1.3.1) | 반응형 스트림 |
| MessagePipe | `com.cysharp.messagepipe` (1.8.2) | 전역 pub/sub |
| MessagePipe.VContainer | `com.cysharp.messagepipe.vcontainer` (1.8.2) | `RegisterMessagePipe()` 확장 |

> **주의**
> - 위 7개는 스코프 레지스트리 없이는 해석에 실패하므로 `dependencies`에 넣지 않고 수동 설치로 분리했습니다. **1번을 건너뛰면 2번이 실패합니다.**
> - `com.cysharp.messagepipe.vcontainer`를 빠뜨리면 `RootLifetimeScope`의 `builder.RegisterMessagePipe()`가 컴파일되지 않습니다.
> - `com.cysharp.zstring`을 빠뜨리면 `JsonLoader`·`SoundManager`의 `ZString.Concat`이 컴파일되지 않습니다.
> - Unity 레지스트리 의존성(Addressables, Input System, uGUI, TextMeshPro, Test Framework, 각종 모듈)은 이 패키지의 `dependencies`에 선언되어 있어 자동 설치됩니다.

## 3. NuGet 코어 패키지 설치 (NuGetForUnity)

ZLogger와 R3는 UPM 패키지가 Unity 연동 레이어만 제공하고, **실제 구현 DLL은 NuGet에서 받아야 합니다.**

1. [NuGetForUnity](https://github.com/GlitchEnzo/NuGetForUnity)를 설치합니다.
   `https://github.com/GlitchEnzo/NuGetForUnity.git?path=src/NuGetForUnity`
2. **NuGet → Manage NuGet Packages**에서 아래를 설치합니다 (의존 패키지는 자동으로 딸려옵니다).

| 패키지 | 버전 |
|---|---|
| `ZLogger` | 2.6.0 |
| `R3` | 1.3.1 |
| `Microsoft.Extensions.Logging` | 10.0.14 |
| `Microsoft.Bcl.AsyncInterfaces` | 10.0.14 |
| `Microsoft.Bcl.TimeProvider` | 10.0.14 |
| `System.Text.Json` | 10.0.14 |
| `System.Threading.Channels` | 10.0.14 |
| `System.Runtime.CompilerServices.Unsafe` | 6.1.2 |
| `System.ComponentModel.Annotations` | 5.0.0 |

> - 위는 **직접 설치**해야 하는 목록입니다. `Microsoft.Extensions.Options`, `System.IO.Pipelines` 등 나머지는 의존 관계로 자동 설치됩니다.
> - **Player Settings → Api Compatibility Level**을 `.NET Framework`로 둡니다. `ArduinoManager`가 쓰는 `System.IO.Ports`(SerialPort)가 이 프로필에만 있어, `.NET Standard 2.1`로 두면 Windows 빌드에서 컴파일되지 않습니다. 위 DLL들은 이 설정에서도 로드되며, `Microsoft.Extensions.*` 10.x도 netstandard 2.0/2.1 타깃을 제공하므로 Unity 2022.3에서 동작합니다.
> - `R3`·`ZLogger`는 UPM 패키지(`com.cysharp.r3`·`com.cysharp.zlogger`)와 **버전을 맞춰야 합니다.** 한쪽만 올리면 런타임에 타입 불일치가 날 수 있습니다. 설치한 뒤에는 `Tools > Update Stack Packages`가 UPM과 NuGet을 함께 올립니다([에디터 메뉴](USAGE.md#에디터-메뉴) 참고).

## 4. DOTween 설치

`FadeManager`·`SoundManager`의 페이드가 DOTween을 씁니다. DOTween은 UPM 배포가 없어 `dependencies`로 선언할 수 없으므로 프로젝트마다 직접 설치합니다.

1. 에셋스토어에서 DOTween(무료판)을 임포트하고 `Tools > Demigiant > DOTween Utility Panel`에서 셋업합니다.
2. 같은 패널의 **Create ASMDEF**로 `DOTween.Modules` 어셈블리를 만듭니다.
3. **Player Settings → Scripting Define Symbols**에 `UNITASK_DOTWEEN_SUPPORT`를 추가합니다. 플랫폼별 값이므로 Android도 빌드한다면 Android 탭에도 넣습니다.

> 2번과 3번을 빠뜨리면 이 패키지의 트윈 코드(`ToUniTask(TweenCancelBehaviour...)`)가 컴파일되지 않습니다.

## 5. 본 패키지 설치

Package Manager → **Add package from git URL** 로 이 저장소 주소를 입력합니다.
로컬 개발 중이라면 `manifest.json`에 파일 참조로 걸어도 됩니다.

```json
"com.huliacdev.template": "file:C:/Projects/Template"
```

## 6. VContainer 소스 제너레이터 (선택, 권장)

**하지 않아도 정상 동작합니다.** 다만 DI 주입이 런타임 리플렉션으로 처리됩니다.

VContainer는 주입 코드를 찾을 때 소스 제너레이터가 만든 클래스를 먼저 보고, 없으면 리플렉션으로 폴백합니다.

```csharp
// VContainer/Runtime/Internal/InjectorCache.cs
var generatedType = key.Assembly.GetType($"{key.FullName}GeneratedInjector", false);
if (generatedType != null) return (IInjector)Activator.CreateInstance(generatedType);
...
return ReflectionInjector.Build(key);   // 제너레이터가 없으면 여기로
```

UPM 패키지에는 제너레이터가 포함되어 있지 않으므로, 기본 상태에서는 이 템플릿의 `[Inject]` 지점 전부가 리플렉션 경로를 탑니다.

**설치**

1. [VContainer 릴리즈 페이지](https://github.com/hadashiA/VContainer/releases)에서 **설치된 UPM 패키지와 같은 버전 태그**의 `VContainer.SourceGenerator.dll`을 받습니다.
   - Unity 2022.3은 Roslyn 4.x이므로 `VContainer.SourceGenerator.dll`을 받습니다 (`...Roslyn3.dll`은 구버전 Unity용).
2. `Assets/` 아래 아무 곳에나 둡니다. 예: `Assets/Plugins/VContainerSourceGenerator/`
3. Project 창에서 DLL을 선택 → 인스펙터 하단 **라벨(태그) 아이콘** 클릭 → `RoslynAnalyzer` 입력
4. 인스펙터의 **Select platforms for plugin**에서 `Any Platform` 해제 → **Include Platforms**에서 Editor/Standalone 해제

> **버전을 반드시 맞추세요.** 생성되는 코드가 런타임의 `IInjector` 인터페이스와 맞물리므로, UPM 패키지와 제너레이터 버전이 어긋나면 컴파일 에러나 주입 실패가 발생할 수 있습니다.

**적용 범위**

`Assets/` 아래에 두면 `Assembly-CSharp`뿐 아니라 **이 패키지 어셈블리(`HuliacDev.Template`)에도 적용**됩니다. 패키지 안에 DLL을 넣을 필요는 없습니다.

**확인 방법**

리플렉션 폴백이 남아 있는지 코드로 확인할 수 있습니다.

```csharp
VContainer.IInjector injector = VContainer.Internal.InjectorCache.GetOrBuild(typeof(HuliacDev.UI.UIManager));
Debug.Log(injector.GetType().Name);
// 생성됨    -> UIManagerGeneratedInjector
// 폴백 중   -> ReflectionInjector
```

검증 환경(Unity 2022.3.62f3 / VContainer 1.19.0)에서 당시 템플릿의 `[Inject]` 지점이 모두 생성분으로 나오는 것을 확인했습니다. 그 뒤에 추가된 클래스는 다시 확인하지 않았으므로, 적용한 프로젝트에서 위 코드로 확인하세요.

## 검증 프로젝트 구성

이 패키지를 **개발·수정할 때** 쓰는 별도 Unity 프로젝트를 만드는 방법입니다. 패키지 자체는 UPM 패키지라 Unity로 직접 열 수 없으므로, 이를 `file:` 참조로 물린 프로젝트가 필요합니다.

위 설치 1~6단계를 마친 뒤 아래를 추가합니다.

### 테스트 노출

Test Runner는 기본적으로 `Assets/` 안의 테스트만 스캔합니다. 패키지 테스트를 보려면 프로젝트의 `Packages/manifest.json`에 다음을 추가해야 합니다.

```json
"testables": [ "com.huliacdev.template" ]
```

> 이걸 빠뜨리면 `AppSettingsProviderTests`가 Test Runner 창에 **아예 나타나지 않습니다.** 테스트가 없는 것처럼 보여 원인을 찾기 어려우니 주의하세요.

### 씬 구성

```
SampleScene
├─ GameManager        : GameManagerBase를 상속한 프로젝트 고유 클래스
├─ TestLifetimeScope  : RootLifetimeScope를 상속한 씬 스코프
├─ SystemCanvas       : Runtime/Prefabs/SystemCanvas.prefab (필수)
│   └─ (하위) GameCloser  : Button + Image + GameCloser (필수)
├─ Reporter           : Reporter + ReporterMessageReceiver
├─ Managers           : UIManager + SoundManager + VideoManager
└─ EventSystem        : InputSystemUIInputModule
```

```csharp
public class TestLifetimeScope : RootLifetimeScope
{
    protected override void Configure(IContainerBuilder builder)
    {
        base.Configure(builder);   // 로깅 / MessagePipe / 설정 / 입력 / 공통 컴포넌트 / 씬에 있는 선택 매니저

        builder.RegisterComponentInHierarchy<GameManager>();
    }
}
```

> `RootLifetimeScope`가 `SystemCanvas`·`GameCloser`를 등록하고, 씬에 있는 `FadeManager`·`UIManager`·`SoundManager`·`VideoManager`·`ArduinoManager`·`ApiManagerBase`·`InactivityTimer`·`ShutdownScheduler`도 자동으로 찾아 등록하므로, 프로젝트 스코프에서는 `GameManager`만 등록합니다. `UIManager`는 `VideoManager`를 주입받으므로 씬에 함께 두어야 합니다.

> **소비자를 전부 올려두는 것이 중요합니다.** `Settings.json`을 요청하는 컴포넌트가 여럿이어야 같은 프레임에 동시 요청이 발생하고, 그 경합에서만 드러나는 결함을 잡을 수 있습니다. 실제로 `AppSettingsProvider`의 동시 요청 예외가 이 구성 덕분에 발견되었습니다. 소비자를 하나만 올리면 해당 유형의 문제는 재현되지 않습니다.

### 실제 로드 경로까지 검증하려면

폰트·오디오·비디오는 대응 에셋이 있어야 로드 경로가 실행됩니다. 없으면 설정 로드까지만 확인됩니다.

| 대상 | 준비물 | 설정 항목 |
|---|---|---|
| 폰트 | `.ttf`를 Addressable로 등록하고 주소 지정 | `Settings.json`의 `fonts[].address` |
| 오디오 | `StreamingAssets/`에 `.wav`/`.mp3`/`.ogg` | `Settings.json`의 `sounds[].clipPath` |
| 비디오 | `StreamingAssets/`에 `.mp4` | `UIManager.SetVideo`에 넘기는 `VideoSetting.fileName` |

> Addressables는 Play Mode Script를 **Use Asset Database (fastest)** 로 두면 콘텐츠 빌드 없이 동작합니다.
