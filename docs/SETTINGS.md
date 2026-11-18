# Settings.json

현장에서 바꾸는 `StreamingAssets/Settings.json`의 키 설명입니다. 예약 종료 시각(`ShutdownSettings.json`)은 [USAGE.md](USAGE.md#예약-종료-shutdownscheduler), 개요는 [README](../README.md)에 있습니다.

```json
{
  "useInactivityTimer": true,
  "resetTime": 90,
  "targetFrameRate": 60,
  "focusRestoreDelay": 3,
  "focusRestoreRetryInterval": 3,
  "alwaysOnTop": false,
  "autoRestart": { "enabled": true, "hangRestartSeconds": 60, "restartDelaySeconds": 5 },
  "apiUrl": "https://example.com/api/log?device=1&message=",
  "fonts": [
    { "key": "title", "address": "Fonts/NotoSans-Bold" },
    { "key": "body",  "address": "Fonts/NotoSans-Regular" }
  ],
  "sounds": [
    { "key": "bgm",   "clipPath": "Sounds/bgm.mp3",   "volume": 0.6 },
    { "key": "click", "clipPath": "Sounds/click.wav", "volume": 1.0 }
  ]
}
```

- 키를 생략해도 오류는 나지 않고 `false`, `0`, 빈 값으로 읽히며, 각 키의 동작은 아래와 같습니다. 모르는 키는 무시합니다. 키 이름은 대소문자를 구분하므로 `ResetTime`처럼 잘못 적으면 오류 없이 생략한 것과 같습니다.
- `Settings.json`을 읽지 못하면(파일 없음, 빈 파일, 끝 쉼표 같은 형식 오류) 오류 로그를 남기고 **비활동 타이머만 켠 대체 설정**(`resetTime` 90초, `AppSettingsProvider.FallbackResetTimeSeconds`)으로 동작합니다. 효과음·폰트·`alwaysOnTop`·`autoRestart`·`apiUrl`은 적용되지 않고 `targetFrameRate`도 값이 없어 수직 동기화가 켜지므로, 콘솔이나 로그 파일에 "Failed to load Settings.json"이 보이면 파일을 고치고 앱을 다시 시작하세요.
- `useInactivityTimer`가 `true`이고 `resetTime`(초)이 0보다 크면 `InactivityTimer`가 활성화됩니다. 둘 중 하나라도 생략하면 꺼집니다(`useInactivityTimer`만 켜고 `resetTime`을 생략하면 경고 로그가 남습니다).
- `targetFrameRate`는 값을 지정하면 `vSyncCount`를 0으로 끄고 해당 FPS로 제한합니다. 0 이하(또는 생략)면 `vSyncCount`를 1로 켜서 프레임을 고정하지 않고 모니터 주사율을 따르며, 이때 품질 레벨의 `vSyncCount`는 쓰지 않습니다. 수직 동기화가 꺼져 있으면 영상처럼 화면 전체가 움직이는 장면에서 찢어짐이 보일 수 있으므로, 이런 앱은 0을 쓰세요. 고주사율 모니터에서는 0일 때 그만큼 더 많이 그리므로, 찢어짐보다 발열·전력 소모가 더 중요하면 30~60 사이 값을 지정하세요. 수직 동기화가 켜져 있으면 `Application.targetFrameRate`가 무시되어 다른 제한이 없으므로, 그래픽 드라이버가 수직 동기화를 강제로 끄면 프레임이 제한되지 않습니다.
- `focusRestoreDelay`(초)는 포커스를 잃은 뒤 첫 복구 시도까지 기다리는 시간, `focusRestoreRetryInterval`(초)은 포커스가 돌아오지 않았을 때 다시 시도하는 간격입니다. 0 이하(또는 생략)면 기본값 3초를 쓰고, 1초 미만 양수는 1초로 올려 적용합니다. 기능을 켜고 끄는 키는 Settings.json에 없고 `F` 키로 전환합니다([창 포커스 복구](USAGE.md#창-포커스-복구-windowfocusrestorer-windows-전용) 참고).
- `alwaysOnTop`이 `true`면 Windows 스탠드얼론 빌드에서 앱 창을 항상 위로 둡니다. 생략하면 `false`(끔)이며, 점검할 때는 `F` 키로 포커스 복구와 함께 끕니다([항상 위](USAGE.md#항상-위-alwaysontop-선택) 참고).
- `autoRestart.enabled`가 `true`면 Windows 스탠드얼론 빌드에서 크래시·강제 종료·응답 없음·Alt+F4 뒤 앱을 다시 켭니다(Alt+F4 재시작은 `F` 키로 끔). 생략하면 꺼짐입니다. `hangRestartSeconds`는 생략하면 60초, 0이면 응답 없음 감지를 끄고, 10초 미만은 10초로 올립니다. `restartDelaySeconds`는 0 이하(또는 생략)면 5초, 1초 미만은 1초로 올리고 600초보다 크면 600초로 줄입니다([자동 재시작](USAGE.md#자동-재시작-autorestartwatchdog-windows-전용) 참고).
- `apiUrl`은 `ApiManagerBase`가 로그를 보낼 서버 주소입니다. `message=`까지 포함한 전체 주소를 적으면 로그 내용을 뒤에 이어 붙여 GET으로 보냅니다. 생략하면 보내지 않습니다.
- `fonts`·`sounds`를 생략하면 등록되는 폰트·사운드가 없습니다. `sounds[].volume`을 생략하면 1.0입니다.
- `fonts[].address`는 **Addressables 주소**이고, `sounds[].clipPath`는 **StreamingAssets 기준 상대 경로**입니다.
- `fonts[].key`는 자유롭게 명명할 수 있으며 `TextSetting.fontName`에서 이 키로 참조합니다.
