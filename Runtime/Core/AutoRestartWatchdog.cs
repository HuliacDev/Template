using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UnityEngine;
using VContainer.Unity;
using HuliacDev.Data;
using HuliacDev.Utils;
using ZLogger;
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
#endif

namespace HuliacDev.Core
{
    /// <summary>
    /// 앱이 크래시·강제 종료·응답 없음·Alt+F4로 끝나면 다시 실행하는 감시 스크립트(PowerShell)를 앱 시작 시 띄우는 서비스(Windows 스탠드얼론 빌드 전용).
    /// <para>
    /// 감시 스크립트는 앱 프로세스가 끝나기를 기다렸다가 종료 코드를 봄. ShutdownScheduler처럼 QuitReason.Set으로 사유를 적고
    /// Application.Quit으로 끝내면 종료 코드가 0이므로, 일부러 끈 것으로 보고 다시 실행하지 않고 감시도 함께 끝냄.
    /// 크래시(접근 위반 0xC0000005 등)와 taskkill /F처럼 프로세스를 바로 끝낸 경우(1)는 앱 코드가 실행될 기회가 없어 QuitReason으로는
    /// 구분할 수 없지만 종료 코드가 0이 아니므로 다시 실행함. 창이 Windows에 '응답 없음'으로 판정된 상태가 정해진 시간 넘게
    /// 이어지면 강제 종료하고 다시 실행함.
    /// </para>
    /// <para>
    /// 사유 없이 끝나는 종료(Alt+F4·창 닫기)는 AltF4ExitCode(76)로 끝내 다시 실행함. 점검할 때는 WindowFocusRestorer의
    /// ToggleFocusRestore 키(기본 F)로 포커스 복구와 함께 끄면 Alt+F4가 종료 코드 0으로 끝나 다시 켜지지 않음.
    /// 토글 키가 없으면(WindowFocusRestorer·TemplateInputActions 미등록) 끌 방법이 없으므로 Alt+F4 재시작을 쓰지 않음.
    /// </para>
    /// <para>
    /// 감시 스크립트는 다시 실행한 앱도 계속 지켜보며, 다시 실행한 앱에 환경 변수로 자기 PID와 재시작 이유를 넘김.
    /// 다시 실행된 앱은 그 감시가 살아 있으면 감시를 또 띄우지 않고 재시작 이유만 로그에 남김.
    /// 감시 스크립트가 시작·재시작·종료한 시각과 이유는 로그 폴더의 AutoRestart.log에 남음.
    /// </para>
    /// <para>
    /// 현장 PC에 설치·작업 스케줄러 등록 없이 동작하도록 스크립트를 이 클래스에 문자열로 두고, 시작할 때마다 persistentDataPath에 씀
    /// (패키지 버전과 스크립트가 항상 맞음). Settings.json의 autoRestart.enabled가 true일 때만 띄우며, 에디터와 다른 플랫폼에서는 띄우지 않음.
    /// </para>
    /// <para>
    /// 프로젝트 코드는 정적 API로 상태를 읽음: 이번 실행이 감시가 다시 켠 실행인지(WasRestarted, RestartReason),
    /// 감시가 지금 앱을 지켜보는지(IsWatching, WaitUntilWatchingAsync). 앱이 스스로 다시 켜져야 할 때는 TryRequestRestart를 부름.
    /// 환경 변수 이름을 복사해 읽거나 Application.Quit(코드)를 직접 부르지 말 것(이름이 바뀌거나 ApiManagerBase·Alt+F4 처리기가 종료 코드를 바꿀 수 있음).
    /// </para>
    /// </summary>
    public sealed class AutoRestartWatchdog : IStartable, IDisposable
    {
        /// <summary>토글 키 표시에 쓰는 감시 상태.</summary>
        internal enum WatchStatus
        {
            /// <summary>감시가 없음(autoRestart 꺼짐, 에디터·다른 플랫폼, 실행 실패, 감시가 끝남).</summary>
            Off,

            /// <summary>감시를 띄웠지만 아직 지켜보기 시작했는지 정해지지 않음.</summary>
            Starting,

            /// <summary>감시가 이 앱을 지켜보고 있음.</summary>
            Watching
        }

        /// <summary>감시 스크립트가 다시 실행한 앱에 자기 PID를 알려 주는 환경 변수 이름. WatchdogScript와 맞춰야 함.</summary>
        internal const string WatchdogPidVariable = "HULIACDEV_AUTORESTART_WATCHDOG_PID";

        /// <summary>감시 스크립트가 다시 실행한 앱에 재시작 이유를 알려 주는 환경 변수 이름. WatchdogScript와 맞춰야 함.</summary>
        internal const string RestartReasonVariable = "HULIACDEV_AUTORESTART_REASON";

        /// <summary>
        /// 감시 스크립트가 다시 실행한 앱에 자기 응답 없음 판정 시간(초, 0이면 끔)을 알려 주는 환경 변수 이름. WatchdogScript와 맞춰야 함.
        /// 다시 켜진 앱은 이미 떠 있는 감시를 그대로 쓰므로, 그사이 바뀐 Settings.json이 아니라 이 값으로 토글 키 표시를 정함.
        /// </summary>
        internal const string HangRestartSecondsVariable = "HULIACDEV_AUTORESTART_HANG_SECONDS";

        /// <summary>
        /// 앱이 감시에 다시 켜 달라고 요청하며 끝낼 때 쓰는 종료 코드(TryRequestRestart). 감시 스크립트가 이 값을 보고 AutoRestart.log와
        /// 다시 켠 앱의 RestartReason에 "restart requested by the app"으로 남김. WatchdogScript와 맞춰야 함.
        /// </summary>
        public const int RestartRequestedExitCode = 75;

        /// <summary>
        /// 사유 없이 끝나는 종료(Alt+F4·창 닫기)를 감시가 다시 켜도록 끝낼 때 쓰는 종료 코드. 감시 스크립트가 이 값을 보고 AutoRestart.log와
        /// 다시 켠 앱의 RestartReason에 "closed with Alt+F4 (exit code 76)"으로 남김. WatchdogScript와 맞춰야 함.
        /// </summary>
        public const int AltF4ExitCode = 76;

        /// <summary>
        /// 감시 스크립트 본문. 인수 이름은 AutoRestartPolicy.BuildWatchdogArguments와 맞춰야 함.
        /// 앱의 종료 코드를 읽으려면 앱이 살아 있을 때 프로세스 핸들을 열어 둬야 하므로, 시작하자마자 핸들을 엶.
        /// 다시 실행할 때는 ShellExecute(보안 경고 창이 뜰 수 있음)가 아니라 CreateProcess로 같은 실행 파일과 인수를 씀.
        /// </summary>
        internal const string WatchdogScript = @"# This file is rewritten by HuliacDev Template AutoRestartWatchdog every time the app starts. Do not edit.
param(
    [Parameter(Mandatory = $true)][int]$AppPid,
    [Parameter(Mandatory = $true)][string]$LogPath,
    [string]$ReadyEventName = '',
    [int]$HangRestartSeconds = 60,
    [int]$RestartDelaySeconds = 5,
    [int]$MaxRestartDelaySeconds = 600,
    [int]$QuickExitSeconds = 60
)

$ErrorActionPreference = 'Stop'

# 대기 시간이 0이면 간격 계산이 NaN이 되어 감시가 예외로 끝나므로 최소 1초로 맞춤(C#에서도 1초 이상으로 넘김).
if ($RestartDelaySeconds -lt 1) { $RestartDelaySeconds = 1 }

function Write-WatchdogLog([string]$message) {
    try {
        $line = '{0} | {1}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss'), $message
        Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8
    } catch { }
}

# 켜자마자 끝난 횟수가 이어질수록 두 번째부터 간격을 두 배씩 늘림(처음 한 번은 원래 간격).
function Get-RestartDelay([int]$quickExitCount) {
    return [int][Math]::Min($MaxRestartDelaySeconds, $RestartDelaySeconds * [Math]::Pow(2, [Math]::Max(0, $quickExitCount - 1)))
}

try {
    Add-Type -Namespace HuliacDev -Name AutoRestartNative -MemberDefinition '[DllImport(""user32.dll"")] public static extern bool IsHungAppWindow(System.IntPtr hWnd); [DllImport(""user32.dll"")] public static extern bool IsWindow(System.IntPtr hWnd); [DllImport(""user32.dll"")] public static extern int GetSystemMetrics(int index);'
} catch {
    Write-WatchdogLog ""Could not load user32 functions; watchdog exits: $_""
    exit 1
}

# GetSystemMetrics(SM_SHUTTINGDOWN)이 0이 아니면 Windows가 종료·로그오프 중이므로 다시 실행하지 않음.
function Test-ShuttingDown {
    return [HuliacDev.AutoRestartNative]::GetSystemMetrics(0x2000) -ne 0
}

try {
    $app = [System.Diagnostics.Process]::GetProcessById($AppPid)
    # 앱이 끝난 뒤에도 ExitCode를 읽으려면 실행 중에 핸들을 열어 둬야 함.
    $null = $app.Handle
    $exePath = $app.MainModule.FileName
    $startedAt = $app.StartTime
} catch {
    Write-WatchdogLog ""Could not open the app process (pid $AppPid); watchdog exits: $_""
    exit 1
}

$commandLine = $null
try {
    $commandLine = (Get-CimInstance -ClassName Win32_Process -Filter ""ProcessId = $AppPid"").CommandLine
} catch {
    Write-WatchdogLog ""Could not read the app command line; restarts will use no arguments: $_""
}

# 다시 실행할 때 같은 인수(-screen-fullscreen 등)를 넘기도록 명령줄에서 실행 파일 부분만 뺌.
$arguments = ''
if ($commandLine) {
    $trimmed = $commandLine.TrimStart()
    if ($trimmed.StartsWith('""')) {
        $end = $trimmed.IndexOf('""', 1)
        if ($end -ge 0) { $arguments = $trimmed.Substring($end + 1).Trim() }
    } elseif ($trimmed.StartsWith($exePath, [StringComparison]::OrdinalIgnoreCase)) {
        # 따옴표 없이 공백 든 경로로 실행된 경우 경로 중간에서 자르지 않도록 실행 파일 경로 길이만큼 뺌.
        $arguments = $trimmed.Substring($exePath.Length).Trim()
    } elseif ($trimmed -match '^\S+\s+(.*)$') {
        $arguments = $Matches[1].Trim()
    }
}

# 이 감시가 다시 실행한 앱이 감시를 또 띄우지 않도록 PID를 넘김(자식 프로세스는 환경 변수를 물려받음).
$env:HULIACDEV_AUTORESTART_WATCHDOG_PID = [string]$PID
# 다시 실행된 앱이 이 감시의 응답 없음 판정 시간을 알 수 있게 넘김(그사이 Settings.json이 바뀌어도 이 감시는 이 값을 씀).
$env:HULIACDEV_AUTORESTART_HANG_SECONDS = [string]$HangRestartSeconds

# 다시 실행된 앱이 위 PID가 정말 이 감시인지(감시가 끝난 뒤 다른 프로세스가 PID를 재사용한 게 아닌지) 확인하도록,
# 감시가 사는 동안 이름 있는 이벤트를 열어 둠. 감시가 끝나면 Windows가 닫아 이름이 사라짐.
try {
    $aliveEvent = New-Object System.Threading.EventWaitHandle($false, [System.Threading.EventResetMode]::ManualReset, ""Local\HuliacDev.AutoRestart.Alive.$PID"")
} catch {
    Write-WatchdogLog ""Could not create the alive event; restarted apps will start another watchdog: $_""
}

# 앱이 만들어 둔 이벤트에 신호를 줘, 이제 앱이 0이 아닌 코드로 끝나도 다시 켤 수 있음을 알림(앱 핸들을 연 뒤라 종료 코드를 읽을 수 있음).
if ($ReadyEventName) {
    try {
        $readyEvent = [System.Threading.EventWaitHandle]::OpenExisting($ReadyEventName)
        $null = $readyEvent.Set()
        $readyEvent.Dispose()
    } catch {
        Write-WatchdogLog ""Could not tell the app that watching started: $_""
    }
}

if ($HangRestartSeconds -gt 0) { $hangText = ""${HangRestartSeconds}s"" } else { $hangText = 'off' }
Write-WatchdogLog (""Watching {0} (pid {1}). Hang restart: {2}."" -f $exePath, $AppPid, $hangText)

$quickExitCount = 0

while ($true) {
    $window = [IntPtr]::Zero
    $hungSince = $null
    $killedForHang = $false

    while (-not $app.WaitForExit(2000)) {
        if ($HangRestartSeconds -le 0) { continue }

        # 응답 없는 창은 Windows가 고스트 창으로 바꾸면서 숨겨 MainWindowHandle이 0이 될 수 있으므로, 처음 찾은 창을 계속 씀.
        if ($window -eq [IntPtr]::Zero -or -not [HuliacDev.AutoRestartNative]::IsWindow($window)) {
            # 그 사이 앱이 끝나면 MainWindowHandle이 예외를 던지므로, 창 없음으로 보고 다음 WaitForExit에서 종료를 처리함.
            try {
                $app.Refresh()
                $window = $app.MainWindowHandle
            } catch {
                $window = [IntPtr]::Zero
            }
            if ($window -eq [IntPtr]::Zero) {
                $hungSince = $null
                continue
            }
        }

        if (-not [HuliacDev.AutoRestartNative]::IsHungAppWindow($window)) {
            $hungSince = $null
            continue
        }

        if ($null -eq $hungSince) {
            $hungSince = Get-Date
            continue
        }

        if (((Get-Date) - $hungSince).TotalSeconds -lt $HangRestartSeconds) { continue }

        Write-WatchdogLog (""App (pid {0}) has not responded for {1}s; killing it."" -f $app.Id, $HangRestartSeconds)
        try { $app.Kill() } catch { Write-WatchdogLog ""Failed to kill the app: $_"" }

        # 강제 종료로 끝난 것을 확인했을 때만 표시함. 실패한 뒤 앱이 살아나 정상 종료(0)하면 다시 실행하지 않아야 하기 때문.
        if ($app.WaitForExit(15000)) {
            $killedForHang = $true
            break
        }

        # 강제 종료가 아직 끝나지 않았으면 판정 시간 뒤 다시 시도함.
        $hungSince = Get-Date
    }

    $exitCode = $app.ExitCode
    # [int]는 반올림하므로 59.6초가 60이 되어 '켜자마자 끝남'에서 빠지지 않게 버림.
    $ranSeconds = [int][Math]::Floor(((Get-Date) - $startedAt).TotalSeconds)
    $app.Dispose()

    if (-not $killedForHang -and $exitCode -eq 0) {
        Write-WatchdogLog (""App exited normally (exit code 0, ran {0}s); not restarting. Watchdog exits."" -f $ranSeconds)
        exit 0
    }

    if ($killedForHang) {
        $reason = ""not responding for ${HangRestartSeconds}s""
    } elseif ($exitCode -eq 75) {
        $reason = 'restart requested by the app (exit code 75)'
    } elseif ($exitCode -eq 76) {
        $reason = 'closed with Alt+F4 (exit code 76)'
    } else {
        $reason = 'exit code {0} (0x{0:X8})' -f $exitCode
    }

    if (Test-ShuttingDown) {
        Write-WatchdogLog ""App ended ($reason) while Windows is shutting down; not restarting. Watchdog exits.""
        exit 0
    }

    # 켜자마자 계속 끝나면(설정 오류, 장치 없음 등) 크래시 덤프가 쌓여 디스크가 차지 않도록 간격을 두 배씩 늘림.
    if ($ranSeconds -lt $QuickExitSeconds) { $quickExitCount++ } else { $quickExitCount = 0 }
    $delay = Get-RestartDelay $quickExitCount
    Write-WatchdogLog (""App ended ({0}, ran {1}s); restarting in {2}s."" -f $reason, $ranSeconds, $delay)

    while ($true) {
        Start-Sleep -Seconds $delay

        if (Test-ShuttingDown) {
            Write-WatchdogLog 'Windows is shutting down; not restarting. Watchdog exits.'
            exit 0
        }

        $env:HULIACDEV_AUTORESTART_REASON = $reason
        try {
            $startInfo = New-Object System.Diagnostics.ProcessStartInfo
            $startInfo.FileName = $exePath
            $startInfo.Arguments = $arguments
            $startInfo.WorkingDirectory = Split-Path -Parent $exePath
            $startInfo.UseShellExecute = $false
            $app = [System.Diagnostics.Process]::Start($startInfo)
            $null = $app.Handle
            $startedAt = Get-Date
            Write-WatchdogLog (""Restarted the app (new pid {0})."" -f $app.Id)
            break
        } catch {
            $quickExitCount++
            $delay = Get-RestartDelay $quickExitCount
            Write-WatchdogLog ""Failed to restart the app; retrying in ${delay}s: $_""
        }
    }
}
";

        private const string ScriptFileName = "AutoRestartWatchdog.ps1";
        private const string LogFileName = "AutoRestart.log";

        /// <summary>감시가 다시 실행한 앱이면 그 이유, 아니면 null. 프로세스 환경 변수라 실행 중에 바뀌지 않으므로 한 번만 읽음.</summary>
        private static readonly string StartupRestartReason = NormalizeRestartReason(Environment.GetEnvironmentVariable(RestartReasonVariable));

        /// <summary>감시를 띄우지 않기로 했거나, 띄운 감시가 지켜보기 시작했거나 실패해 결과가 정해지면 완료됨(WaitUntilWatchingAsync가 기다림).</summary>
        private static readonly UniTaskCompletionSource WatchDecided = new UniTaskCompletionSource();

        /// <summary>
        /// Alt+F4 재시작을 마지막으로 켜거나 끈 쪽(토글 키를 가진 WindowFocusRestorer). 씬마다 스코프를 두는 구성에서 새 스코프가
        /// 옛 스코프보다 먼저 시작돼도, 옛 스코프가 사라지며 새 스코프가 정한 상태를 끄지 않게 하기 위함.
        /// </summary>
        private static object _altF4RestartOwner;

        /// <summary>
        /// Alt+F4·창 닫기로 끝날 때 다시 켤지. WindowFocusRestorer가 토글 키를 연결하면 켜고, 그 키로 포커스 복구와 함께 끄고 켬.
        /// 토글 키가 없으면 점검할 때 앱을 끌 방법이 없으므로 꺼진 채로 둠.
        /// </summary>
        private static bool _isAltF4RestartOn;

        /// <summary>
        /// 이 앱을 지켜보는 감시의 응답 없음 감지가 켜져 있는지. 감시를 띄울 때 넘긴 값이거나, 이미 떠 있는 감시를 쓰면 그 감시가 환경 변수로 넘긴 값.
        /// 토글 키 표시에서 감시가 무엇을 다시 켜는지 알리는 데 씀.
        /// </summary>
        private static bool _isHangRestartOn;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        /// <summary>GetSystemMetrics의 SM_SHUTTINGDOWN. 0이 아니면 Windows가 종료·로그오프 중임(감시 스크립트와 같은 값).</summary>
        private const int SmShuttingDown = 0x2000;

        /// <summary>감시가 지켜보기 시작했는지 확인하는 간격(밀리초). 감시가 뜨는 데 몇 초 걸리므로 매 프레임 확인하지 않음.</summary>
        private const int ReadyPollIntervalMilliseconds = 200;

        // 이 앱을 지켜보기 시작한 것이 확인된 감시 프로세스. 앱이 끝날 때까지 두고 IsWatching에서 살아 있는지 확인함
        // (작업 관리자로 감시만 끈 경우를 걸러내기 위함).
        private static System.Diagnostics.Process _watchdogProcess;

        // 한 프로세스에서 감시를 두 번 띄우면 두 감시가 각자 앱을 다시 실행해 앱이 두 개 뜨므로 막음
        // (RootLifetimeScope를 상속한 스코프가 둘 이상 만들어지는 구성 대비).
        private static bool _hasLaunched;

        // 감시를 띄웠지만 아직 지켜보기 시작했는지 정해지지 않은 동안 true. 이때도 Alt+F4 재시작은 곧 적용되므로 토글 표시에 넣음.
        private static bool _isWatchStarting;

        // 사유 없는 종료(Alt+F4)를 AltF4ExitCode로 다시 끝내려고 미뤄 둔 동안 true.
        private static bool _isDeferringAltF4Quit;

        // 마지막으로 시작한 살아 있는 인스턴스. 정적 종료 처리기가 앱 로그를 남길 때 이 인스턴스의 로거를 씀. 감시를 띄운 스코프가
        // 씬 전환으로 먼저 사라져도 이후 스코프의 로거로 남기기 위해, 감시를 띄웠는지와 상관없이 시작할 때마다 바꿈.
        private static AutoRestartWatchdog _logOwner;

        private readonly string _logDirectory;
#endif

        private readonly AppSettingsProvider _settingsProvider;
        private readonly ILogger<AutoRestartWatchdog> _logger;

        private CancellationTokenSource _cts;

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        // 스코프가 파기된 뒤에는 로거도 함께 정리되므로, 앱 수명 동안 이어지는 감시 시작 대기에서 로그를 남기지 않기 위함.
        private bool _isDisposed;
#endif

        /// <summary>
        /// 이번 실행이 감시 스크립트가 다시 켠 실행이면 true. 다시 켜진 뒤 같은 원인으로 재시작이 반복되지 않게 하는 판단 등에 씀.
        /// 사람이 직접 켰거나 감시가 처음 띄워진 실행이면 false.
        /// </summary>
        public static bool WasRestarted => StartupRestartReason != null;

        /// <summary>
        /// 감시 스크립트가 이번 실행을 다시 켠 이유. 다시 켠 실행이 아니면 null.
        /// "exit code -1073741819 (0xC0000005)", "not responding for 60s", "restart requested by the app (exit code 75)",
        /// "closed with Alt+F4 (exit code 76)" 같은 로그·표시용 문장임.
        /// </summary>
        public static string RestartReason => StartupRestartReason;

        /// <summary>
        /// 감시 스크립트가 지금 이 앱을 지켜보고 있으면 true. 이때 앱이 0이 아닌 코드로 끝나면 다시 켜짐.
        /// 감시를 띄운 직후에는 감시가 앱 핸들을 열 때까지(보통 몇 초) false이며, autoRestart가 꺼져 있거나 에디터·다른 플랫폼이거나
        /// 감시 실행에 실패했거나 감시가 끝났으면 false. 앱 시작 직후에 판단해야 하면 WaitUntilWatchingAsync를 기다릴 것.
        /// 부를 때마다 감시 프로세스 상태를 OS에 물으므로 매 프레임이 아니라 판단이 필요할 때만 부를 것.
        /// </summary>
        public static bool IsWatching
        {
            get
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                if (_watchdogProcess == null) return false;

                try
                {
                    return !_watchdogProcess.HasExited;
                }
                catch (Exception)
                {
                    // 프로세스 정보를 읽을 수 없으면 지켜본다고 장담할 수 없음
                    return false;
                }
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// 감시를 띄울지와 띄운 감시가 지켜보기 시작했는지가 정해질 때까지 기다린 뒤 IsWatching을 돌려줌.
        /// 이미 정해졌으면 바로 끝남. AutoRestartWatchdog를 등록하지 않았으면(ConfigureAutoRestart를 빈 본문으로 override)
        /// 결과가 정해지지 않아 끝나지 않으므로, 시간 제한이나 파괴와 연결된 토큰을 넘길 것(취소되면 OperationCanceledException).
        /// 취소되면 그 취소가 일어난 스레드에서 이어지므로, 시간 제한은 CancelAfter(스레드 풀 타이머) 대신 CancelAfterSlim(PlayerLoop)으로 검.
        /// </summary>
        public static async UniTask<bool> WaitUntilWatchingAsync(CancellationToken cancellationToken)
        {
            await WatchDecided.Task.AttachExternalCancellation(cancellationToken);
            return IsWatching;
        }

        /// <summary>
        /// 감시가 이 앱을 지켜보고 있으면 RestartRequestedExitCode로 앱을 끝내고 true를 돌려줌. 감시가 restartDelaySeconds초 뒤
        /// 앱을 다시 켜며, 다시 켠 앱의 RestartReason은 "restart requested by the app (exit code 75)"임.
        /// 지켜보고 있지 않으면 끝내지 않고 false를 돌려줌(끝내면 다시 켜 줄 감시가 없어 앱이 꺼진 채 남기 때문).
        /// 다른 종료(예약 종료, Alt+F4)나 앞서 받아들인 재시작 요청이 이미 진행 중이어도(QuitReason.IsQuitRequested)
        /// false를 돌려줌. 이때 false는 "감시가 없다"는 뜻이 아니므로 호출부가 따로 Application.Quit을 부르지 말 것. 반대로 요청 뒤 예약 종료가 들어오면 그쪽이 종료 코드를 0으로 되돌려 그 종료가 이김.
        /// <para>
        /// reason은 ApiManagerBase 종료 로그에 "end ({reason})"로 남음. 끝내는 경로는 Application.Quit과 같아
        /// ApiManagerBase가 종료 로그를 보낸 뒤 이 종료 코드로 끝냄. 앱이 60초 안에 다시 요청하면 감시가 간격을 두 배씩 늘림.
        /// </para>
        /// </summary>
        public static bool TryRequestRestart(string reason)
        {
            if (!CanAcceptRestartRequest(IsWatching, QuitReason.IsQuitRequested)) return false;

            QuitReason.Set(reason, RestartRequestedExitCode);
            Application.Quit(RestartRequestedExitCode);
            return true;
        }

        /// <summary>
        /// 감시 로그를 쓸 폴더(앱 파일 로그와 같은 폴더)와 설정 제공자·로거를 받아 구성함.
        /// </summary>
        public AutoRestartWatchdog(string logDirectory, AppSettingsProvider settingsProvider, ILogger<AutoRestartWatchdog> logger)
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            _logDirectory = logDirectory;
#endif
            _settingsProvider = settingsProvider;
            _logger = logger;
        }

        /// <summary>
        /// 재시작 요청을 받아들일지 정함. 감시가 지켜보고 있고 다른 종료가 진행 중이 아닐 때만 받아들임.
        /// </summary>
        internal static bool CanAcceptRestartRequest(bool isWatching, bool isQuitRequested)
        {
            return isWatching && !isQuitRequested;
        }

        /// <summary>Alt+F4 재시작이 켜져 있으면 true. 토글 키를 가진 WindowFocusRestorer가 정함.</summary>
        internal static bool IsAltF4RestartOn => _isAltF4RestartOn;

        /// <summary>이 앱을 지켜보는 감시의 응답 없음 감지가 켜져 있으면 true(판정 시간이 0이 아님). 토글 키 표시에 씀.</summary>
        internal static bool IsHangRestartOn => _isHangRestartOn;

        /// <summary>
        /// 감시 상태. 토글 키 표시에 씀. IsWatching처럼 OS에 물으므로 매 프레임이 아니라 토글할 때만 부를 것.
        /// </summary>
        internal static WatchStatus CurrentWatchStatus
        {
            get
            {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
                if (IsWatching) return WatchStatus.Watching;
                return _isWatchStarting ? WatchStatus.Starting : WatchStatus.Off;
#else
                return WatchStatus.Off;
#endif
            }
        }

        /// <summary>
        /// 토글 키를 가진 쪽(owner)이 Alt+F4 재시작을 켜거나 끔. WindowFocusRestorer가 토글 키를 연결할 때와 그 키를 누를 때 부르며,
        /// 마지막으로 부른 쪽이 소유자가 됨.
        /// </summary>
        internal static void SetAltF4Restart(object owner, bool isOn)
        {
            _altF4RestartOwner = owner;
            _isAltF4RestartOn = isOn;
        }

        /// <summary>
        /// 토글 키를 가진 쪽이 사라질 때 부름. 소유자이면 끌 키가 없어지므로 Alt+F4 재시작을 끔. 그사이 다른 쪽(새 스코프)이 소유자가 됐으면
        /// 그 상태를 그대로 둠.
        /// </summary>
        internal static void ReleaseAltF4Restart(object owner)
        {
            if (!ReferenceEquals(_altF4RestartOwner, owner)) return;

            _altF4RestartOwner = null;
            _isAltF4RestartOn = false;
        }

        /// <summary>
        /// 사유를 기록하지 않은 종료(Alt+F4·창 닫기)를 진행 중으로 표시하고 그 종료 코드를 정함. 종료를 미루는 쪽(ApiManagerBase, 이 클래스의
        /// 종료 처리기)이 부르며, 이미 진행 중인 종료는 바꾸지 않음. 감시가 다시 켜는 코드로 끝내면 앱 로그에 남겨, 감시가 다시 켠 뒤에도
        /// 앞 실행의 로그에서 Alt+F4로 끈 것을 알 수 있게 함. PlayerLoop가 멈춰 다시 켜지 않는 경우도 이유를 남김.
        /// </summary>
        internal static void MarkUnattributedQuit()
        {
            if (QuitReason.IsQuitRequested) return;

            bool isWatching = IsWatching;
            bool isPlayerLoopPaused = IsPlayerLoopPaused();
            int exitCode = ResolveAltF4ExitCode(isWatching, _isAltF4RestartOn, IsWindowsShuttingDown(), isPlayerLoopPaused);
            QuitReason.MarkQuitRequested(exitCode);

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            if (exitCode != 0)
            {
                LogFromLiveInstance("[AutoRestartWatchdog] Closed with Alt+F4 or the window close button while Alt+F4 restart is on. Quitting with exit code "
                    + exitCode + " so the watchdog restarts the app. To close the app for maintenance, turn Alt+F4 restart off with the ToggleFocusRestore key first.");
            }
            else if (isWatching && _isAltF4RestartOn && isPlayerLoopPaused)
            {
                LogFromLiveInstance("[AutoRestartWatchdog] Closed while unfocused with Run In Background off; quitting without Alt+F4 restart because the player loop is paused.");
            }
#endif
        }

        /// <summary>
        /// 사유 없이 끝나는 종료(Alt+F4·창 닫기)를 끝낼 종료 코드. IsWatching으로 OS에 물으므로 종료할 때만 부를 것.
        /// </summary>
        internal static int ResolveAltF4ExitCode()
        {
            return ResolveAltF4ExitCode(IsWatching, _isAltF4RestartOn, IsWindowsShuttingDown(), IsPlayerLoopPaused());
        }

        /// <summary>
        /// 감시가 지켜보고 있고 Alt+F4 재시작이 켜져 있으면 AltF4ExitCode, 아니면 0(다시 켜지 않음)을 돌려줌.
        /// 지켜보지 않을 때 0이 아닌 코드로 끝내면 다시 켜 주는 쪽 없이 비정상 종료처럼 보이기만 하므로 0을 씀. Windows 종료 중에는 감시도
        /// 다시 켜지 않고, PlayerLoop가 멈춰 있으면 종료를 미뤘다가 다시 끝낼 수 없으므로 이때도 0을 씀(종료를 미루지 않게 하기 위함).
        /// </summary>
        internal static int ResolveAltF4ExitCode(bool isWatching, bool isAltF4RestartOn, bool isShuttingDown, bool isPlayerLoopPaused)
        {
            return isWatching && isAltF4RestartOn && !isShuttingDown && !isPlayerLoopPaused ? AltF4ExitCode : 0;
        }

        /// <summary>
        /// Run In Background가 꺼진 채 포커스가 없으면 플레이어가 멈춰 PlayerLoop(UniTask.Yield)가 돌지 않음(작업 표시줄에서 창을 닫는 경우 등).
        /// </summary>
        private static bool IsPlayerLoopPaused()
        {
            return !Application.runInBackground && !Application.isFocused;
        }

        /// <summary>
        /// Windows가 종료·로그오프 중이면 true. 이때는 감시가 다시 켜지 않으므로 Alt+F4 종료를 미루지 않음(미루면 Windows 종료가 잠깐 막힘).
        /// </summary>
        private static bool IsWindowsShuttingDown()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            return NativeMethods.GetSystemMetrics(SmShuttingDown) != 0;
#else
            return false;
#endif
        }

        /// <summary>
        /// 환경 변수 값이 비어 있으면 다시 켠 실행이 아닌 것으로 보고 null로 바꿈.
        /// </summary>
        internal static string NormalizeRestartReason(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>
        /// VContainer 컨테이너 빌드 완료 시 자동 호출됨. 감시가 다시 실행한 앱이면 재시작 이유를 남기고,
        /// 앱 종료와 연결된 토큰으로 설정을 읽어 감시를 띄움.
        /// </summary>
        void IStartable.Start()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            _logOwner = this;
#endif
            LogRestartReason();

            _cts = CancellationTokenSource.CreateLinkedTokenSource(Application.exitCancellationToken);
            // persistentDataPath는 메인 스레드에서만 읽을 수 있으므로 여기서 읽어 넘김.
            StartWatchdogAsync(Application.persistentDataPath, _cts.Token).Forget();
        }

        /// <summary>
        /// 감시 스크립트가 앱을 다시 실행했으면(환경 변수가 있으면) 그 이유를 경고로 남김.
        /// 이전 실행의 로그에는 크래시·강제 종료 흔적이 남지 않으므로, 다시 켜진 앱의 로그에서 원인을 알 수 있게 함.
        /// </summary>
        private void LogRestartReason()
        {
            if (!WasRestarted || _logger == null) return;

            _logger.ZLogWarning($"[AutoRestartWatchdog] Restarted by the watchdog after the previous run ended ({RestartReason}).");
        }

        /// <summary>
        /// Settings.json의 autoRestart를 읽어, 켜져 있으면 Windows 스탠드얼론 빌드에서만 감시 스크립트를 띄움.
        /// Settings.json을 읽지 못하면 AppSettingsProvider의 대체 설정에 autoRestart가 없으므로 띄우지 않음.
        /// 감시를 띄우지 않기로 했거나 결과가 정해지면 WatchDecided를 완료해 WaitUntilWatchingAsync를 깨움.
        /// </summary>
        private async UniTaskVoid StartWatchdogAsync(string persistentDataPath, CancellationToken cancellationToken)
        {
            Settings settings;
            try
            {
                settings = await _settingsProvider.GetAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 컨테이너 파기나 앱 종료로 정상 취소됨. 기다리는 쪽이 남지 않도록 결과를 정함
                // (다른 스코프가 이어서 감시를 띄워도 IsWatching은 감시 프로세스로 판단하므로 맞게 바뀜).
                WatchDecided.TrySetResult();
                return;
            }

            AutoRestartSetting setting = settings?.autoRestart;
            if (setting == null || !setting.enabled)
            {
                WatchDecided.TrySetResult();
                return;
            }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            // 먼저 만들어진 스코프가 결과를 정하므로 여기서는 완료하지 않음.
            if (_hasLaunched) return;

            if (TryGetRunningWatchdog(out System.Diagnostics.Process runningWatchdog))
            {
                // 감시는 앱을 다시 실행하자마자 앱 핸들을 열어 두므로 이미 지켜보는 중임.
                _hasLaunched = true;
                // 이 감시는 처음 띄울 때 받은 응답 없음 판정 시간을 계속 쓰므로 감시가 넘긴 값을 따름.
                _isHangRestartOn = AutoRestartPolicy.IsHangRestartOn(Environment.GetEnvironmentVariable(HangRestartSecondsVariable));
                BeginWatching(runningWatchdog);
                WatchDecided.TrySetResult();
                if (_logger != null) _logger.ZLogInformation($"[AutoRestartWatchdog] Already watched by the watchdog that restarted this app (pid {runningWatchdog.Id}).");
                return;
            }

            int hangRestartSeconds = AutoRestartPolicy.ResolveHangRestartSeconds(setting.hangRestartSeconds);
            _isHangRestartOn = hangRestartSeconds > 0;
            int restartDelaySeconds = AutoRestartPolicy.ResolveRestartDelaySeconds(setting.restartDelaySeconds);
            // persistentDataPath는 '/' 구분자를 쓰므로 로그·명령줄에 섞이지 않게 Windows 형식으로 맞춤.
            string scriptPath = Path.GetFullPath(Path.Combine(persistentDataPath, ScriptFileName));
            string logPath = Path.GetFullPath(Path.Combine(_logDirectory, LogFileName));

            int appProcessId;
            using (System.Diagnostics.Process current = System.Diagnostics.Process.GetCurrentProcess())
            {
                appProcessId = current.Id;
            }

            string readyEventName = AutoRestartPolicy.BuildReadyEventName(appProcessId);
            string arguments = AutoRestartPolicy.BuildWatchdogArguments(scriptPath, appProcessId, logPath, readyEventName, hangRestartSeconds, restartDelaySeconds);

            // 실행 결과를 기다리는 사이 다른 스코프가 또 띄우지 않도록 먼저 표시함.
            _hasLaunched = true;
            _isWatchStarting = true;

            System.Diagnostics.Process watchdog;
            IntPtr readyEvent;
            try
            {
                // 실행은 짧고 감시는 앱과 별개 프로세스라 취소 토큰을 넘기지 않음(그사이 스코프가 파기되면 아래 로그는 남기지 않음). 넘기면 실행 직전에 취소될 때
                // 감시는 뜨지 않았는데 _hasLaunched만 남아, 이후 만들어지는 스코프도 감시를 띄우지 않게 됨.
                (watchdog, readyEvent) = await UniTask.RunOnThreadPool(() => LaunchWatchdog(scriptPath, logPath, readyEventName, arguments));
            }
            catch (Exception e)
            {
                _hasLaunched = false;
                _isWatchStarting = false;
                WatchDecided.TrySetResult();
                if (_logger != null && !_isDisposed) _logger.ZLogError($"[AutoRestartWatchdog] Failed to start the watchdog; the app will not be restarted automatically: {e.Message}");
                return;
            }

            if (_logger != null && !_isDisposed)
            {
                _logger.ZLogInformation($"[AutoRestartWatchdog] Watchdog started (pid {watchdog.Id}). Restarts the app when it ends with a non-zero exit code or stays not responding for {hangRestartSeconds}s (0 = off), after {restartDelaySeconds}s. Log: {logPath}");
            }

            if (readyEvent == IntPtr.Zero)
            {
                // 감시는 떠서 재시작은 되지만, 지켜보기 시작했는지 알 수 없어 IsWatching을 켜지 않음.
                watchdog.Dispose();
                _isWatchStarting = false;
                WatchDecided.TrySetResult();
                if (_logger != null && !_isDisposed) _logger.ZLogWarning($"[AutoRestartWatchdog] Could not create the event the watchdog signals when it starts watching; IsWatching stays false.");
                return;
            }

            // 스코프가 먼저 파기돼도 IsWatching이 맞게 켜지도록 대기는 스코프가 아니라 앱 종료에만 묶음.
            await WaitForWatchStartAsync(watchdog, readyEvent, Application.exitCancellationToken);
#else
            WatchDecided.TrySetResult();
            if (_logger != null) _logger.ZLogInformation($"[AutoRestartWatchdog] autoRestart is enabled, but the watchdog only runs in Windows standalone builds. Skipped on this platform.");
#endif
        }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        /// <summary>
        /// 감시가 앱 핸들을 열고 이벤트에 신호를 줄 때까지 기다려 IsWatching을 켬. 감시가 그 전에 끝나면(PowerShell 실행 실패,
        /// 앱 핸들을 열지 못함) 켜지 않음. 감시가 뜨는 데(Add-Type 컴파일 포함) 몇 초 걸리므로 매 프레임이 아니라 간격을 두고 확인함.
        /// 앱 종료 토큰으로만 취소하며, 스코프가 파기된 뒤에는 로그를 남기지 않음.
        /// </summary>
        private async UniTask WaitForWatchStartAsync(System.Diagnostics.Process watchdog, IntPtr readyEvent, CancellationToken cancellationToken)
        {
            bool isWatching = false;

            try
            {
                while (true)
                {
                    if (watchdog.HasExited)
                    {
                        if (_logger != null && !_isDisposed) _logger.ZLogError($"[AutoRestartWatchdog] The watchdog exited (exit code {watchdog.ExitCode}) before it started watching; the app will not be restarted automatically. See AutoRestart.log.");
                        return;
                    }

                    if (NativeMethods.WaitForSingleObject(readyEvent, 0) == NativeMethods.WaitObject0)
                    {
                        isWatching = true;
                        BeginWatching(watchdog);
                        if (_logger != null && !_isDisposed) _logger.ZLogInformation($"[AutoRestartWatchdog] The watchdog is now watching this app.");
                        return;
                    }

                    await UniTask.Delay(ReadyPollIntervalMilliseconds, DelayType.Realtime, PlayerLoopTiming.Update, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                // 앱 종료로 정상 취소됨. 감시는 별개 프로세스라 그대로 앱을 지켜봄.
            }
            finally
            {
                NativeMethods.CloseHandle(readyEvent);
                if (!isWatching) watchdog.Dispose();
                _isWatchStarting = false;
                WatchDecided.TrySetResult();
            }
        }

        /// <summary>
        /// 감시가 이 앱을 지켜보기 시작했을 때 부름. 감시 프로세스를 기억하고, 사유 없는 종료(Alt+F4)를 다시 켤 종료 코드로 끝내는 처리기를 붙임.
        /// 감시는 _hasLaunched로 앱 수명 동안 한 번만 띄우므로 처리기도 한 번만 붙음.
        /// </summary>
        private static void BeginWatching(System.Diagnostics.Process watchdog)
        {
            _watchdogProcess = watchdog;
            Application.wantsToQuit += OnWantsToQuit;
        }

        /// <summary>
        /// 마지막으로 시작한 살아 있는 인스턴스의 로거로 정보 로그를 남김. 종료 처리기는 정적이라 로거가 없으므로 그 인스턴스의 것을 빌림.
        /// 살아 있는 인스턴스가 없으면(스코프가 모두 파기돼 로거가 정리됨) 남기지 않음. 종료할 때만 불리므로 문자열을 만들어 넘겨도 됨.
        /// </summary>
        private static void LogFromLiveInstance(string message)
        {
            AutoRestartWatchdog owner = _logOwner;
            if (owner == null || owner._isDisposed || owner._logger == null) return;

            owner._logger.ZLogInformation($"{message}");
        }

        /// <summary>
        /// 사유 없는 종료(Alt+F4·창 닫기)를 Alt+F4 재시작이 켜져 있으면 미루고 AltF4ExitCode로 다시 끝냄. Unity는 진행 중인 종료의
        /// 종료 코드를 바꿀 방법이 없어 한 번 미룸. 이미 진행 중인 종료(사유를 적은 종료, ApiManagerBase가 미룬 종료, 이 처리기가 다시 부른 종료)와
        /// 다시 켜지 않을 종료(코드 0, Windows 종료 중 포함)는 그대로 진행함. ApiManagerBase가 있으면 그쪽이 같은 코드로 종료 로그를 보낸 뒤 끝냄.
        /// </summary>
        private static bool OnWantsToQuit()
        {
            // 미룬 종료를 다시 끝내기 전에 들어온 종료 요청(창 닫기 연타 등)도 미룸. 그대로 보내면 종료 코드 0으로 끝나 다시 켜지지 않음.
            // 그사이 들어온 예약 종료도 QuitNextFrameAsync가 그 종료 코드(0)로 대신 끝냄.
            if (_isDeferringAltF4Quit) return false;
            if (QuitReason.IsQuitRequested) return true;

            // 다시 켜지 않을 종료(F 키로 끔, Windows 종료 중, PlayerLoop가 멈춤)는 코드 0으로 표시되므로 미루지 않음.
            MarkUnattributedQuit();
            if (QuitReason.ExitCode == 0) return true;

            _isDeferringAltF4Quit = true;
            QuitNextFrameAsync().Forget();
            return false;
        }

        /// <summary>
        /// 종료 처리기 안에서 바로 다시 끝내지 않도록 한 프레임 뒤 QuitReason.ExitCode로 끝냄(ApiManagerBase와 같은 이유).
        /// 그사이 예약 종료가 사유를 적으면 그 종료 코드(0)로 끝남.
        /// </summary>
        private static async UniTaskVoid QuitNextFrameAsync()
        {
            await UniTask.Yield();
            _isDeferringAltF4Quit = false;
            Application.Quit(QuitReason.ExitCode);
        }

        /// <summary>
        /// 이 앱을 다시 실행한 감시 스크립트가 아직 살아 있으면 그 프로세스를 돌려줌. 감시가 사는 동안 열어 두는 이름 있는 이벤트로
        /// 확인하므로, 감시가 끝난 뒤 PID가 다른 프로세스에 재사용돼도 감시로 잘못 보지 않음(시작 시각 비교와 달리 시계 변경에도 영향 없음).
        /// 확인할 수 없으면 살아 있지 않은 것으로 봄(그러면 새 감시를 띄움). 돌려준 프로세스는 핸들을 열어 두어,
        /// 이후 감시가 끝나고 PID가 재사용돼도 IsWatching이 다른 프로세스를 보지 않음.
        /// </summary>
        private static bool TryGetRunningWatchdog(out System.Diagnostics.Process watchdog)
        {
            watchdog = null;

            if (!int.TryParse(Environment.GetEnvironmentVariable(WatchdogPidVariable), out int id)) return false;

            System.Diagnostics.Process process = null;
            try
            {
                process = System.Diagnostics.Process.GetProcessById(id);
                // 핸들을 먼저 열어 두면 이 객체가 살아 있는 동안 PID가 다른 프로세스에 재사용되지 않으므로,
                // 이어서 확인하는 생존 이벤트가 이 핸들의 프로세스 것임이 보장됨.
                _ = process.Handle;
                if (process.HasExited || !IsAliveEventPresent(AutoRestartPolicy.BuildAliveEventName(id)))
                {
                    process.Dispose();
                    return false;
                }
            }
            catch (Exception)
            {
                // 이미 끝난 프로세스(ArgumentException)이거나 정보를 읽을 수 없음
                process?.Dispose();
                return false;
            }

            watchdog = process;
            return true;
        }

        /// <summary>
        /// 감시가 사는 동안 열어 두는 이벤트가 있는지 확인함. 열 수 있으면 그 이름의 감시가 살아 있다는 뜻이며, 확인만 하고 바로 닫음.
        /// </summary>
        private static bool IsAliveEventPresent(string aliveEventName)
        {
            IntPtr handle = NativeMethods.OpenEvent(NativeMethods.Synchronize, false, aliveEventName);
            if (handle == IntPtr.Zero) return false;

            NativeMethods.CloseHandle(handle);
            return true;
        }

        /// <summary>
        /// 감시 스크립트를 쓰고, 감시가 지켜보기 시작할 때 신호를 줄 이벤트를 만든 뒤 숨은 PowerShell로 실행함.
        /// 파일 쓰기와 프로세스 생성이 메인 스레드를 막지 않도록 스레드 풀에서 호출함. Windows PowerShell 5.1은 BOM 없는 UTF-8
        /// 스크립트를 시스템 코드 페이지로 읽으므로 BOM을 붙임. 이벤트를 만들지 못하면 IntPtr.Zero를 돌려주며 감시는 그대로 띄움.
        /// </summary>
        private static (System.Diagnostics.Process watchdog, IntPtr readyEvent) LaunchWatchdog(string scriptPath, string logPath, string readyEventName, string arguments)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(scriptPath));
            Directory.CreateDirectory(Path.GetDirectoryName(logPath));
            File.WriteAllText(scriptPath, WatchdogScript, new UTF8Encoding(true));

            // 감시는 이미 있는 이벤트만 열 수 있으므로 감시보다 먼저 만듦(수동 리셋, 처음엔 신호 없음).
            IntPtr readyEvent = NativeMethods.CreateEvent(IntPtr.Zero, true, false, readyEventName);

            System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            try
            {
                return (System.Diagnostics.Process.Start(startInfo), readyEvent);
            }
            catch (Exception)
            {
                if (readyEvent != IntPtr.Zero) NativeMethods.CloseHandle(readyEvent);
                throw;
            }
        }

        /// <summary>
        /// 감시가 지켜보기 시작했다는 신호와 감시가 살아 있다는 표시를 확인하는 이벤트용 kernel32 함수. .NET의 이름 있는 EventWaitHandle은 IL2CPP에서
        /// 프로세스 사이에 공유되는지 보장되지 않으므로 Win32 API를 직접 부름.
        /// </summary>
        private static class NativeMethods
        {
            public const uint WaitObject0 = 0;
            public const uint Synchronize = 0x00100000;

            /// <summary>이름 있는 이벤트를 만들거나 이미 있으면 엶.</summary>
            [DllImport("kernel32.dll", EntryPoint = "CreateEventW", CharSet = CharSet.Unicode)]
            public static extern IntPtr CreateEvent(IntPtr eventAttributes, bool manualReset, bool initialState, string name);

            /// <summary>이미 있는 이름 있는 이벤트를 엶. 없으면 IntPtr.Zero를 돌려줌.</summary>
            [DllImport("kernel32.dll", EntryPoint = "OpenEventW", CharSet = CharSet.Unicode)]
            public static extern IntPtr OpenEvent(uint desiredAccess, bool inheritHandle, string name);

            /// <summary>이벤트가 신호 상태가 될 때까지 지정 시간만큼 기다림(0이면 바로 확인).</summary>
            [DllImport("kernel32.dll")]
            public static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

            /// <summary>커널 객체 핸들을 닫음.</summary>
            [DllImport("kernel32.dll")]
            public static extern bool CloseHandle(IntPtr handle);

            /// <summary>시스템 값(SM_SHUTTINGDOWN 등)을 읽음.</summary>
            [DllImport("user32.dll")]
            public static extern int GetSystemMetrics(int index);
        }
#endif

        /// <summary>
        /// 설정 로드를 취소함. 감시 시작 대기는 앱이 끝날 때까지 이어가되 로그는 남기지 않음.
        /// 이미 띄운 감시 스크립트는 앱과 별개 프로세스라 그대로 앱을 지켜봄.
        /// </summary>
        public void Dispose()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            _isDisposed = true;
            // 그사이 다른 스코프가 시작했으면 그 인스턴스가 로그를 맡으므로 그대로 둠.
            if (ReferenceEquals(_logOwner, this)) _logOwner = null;
#endif
            if (_cts == null) return;

            _cts.Cancel();
            _cts.Dispose();
            _cts = null;
        }
    }
}
