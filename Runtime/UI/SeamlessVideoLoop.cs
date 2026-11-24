using System.Threading;
using HuliacDev.Utils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace HuliacDev.UI
{
    /// <summary>
    /// 같은 영상을 재생기 두 개로 번갈아 틀어 반복 지점의 멈칫을 없앰. VideoManager.PlaySeamlessLoopAsync가 만들고 매 프레임 갱신함.
    /// <para>
    /// VideoPlayer의 반복(isLooping)이나 끝에서 되감아 다시 재생하기는 되감는 동안 마지막 프레임이 더 오래 보임.
    /// 쉬는 재생기를 첫 프레임에서 멈춰 두었다가 재생 중인 쪽의 마지막 프레임이 한 프레임 길이만큼 보이면 화면을 바꿔 이어 재생함.
    /// 쉬는 재생기가 아직 준비되지 않았으면 재생 중인 쪽을 처음으로 되감아 다시 재생함(멈칫은 있지만 끊기지 않음).
    /// </para>
    /// </summary>
    internal sealed class SeamlessVideoLoop
    {
        // 쉬는 재생기가 이 시간 안에 준비되지 않으면 되감을 때 다시 준비시킴. 준비 중인 것을 바퀴마다 처음부터 다시 시키지 않을 만큼 길게 둠.
        private const double ArmRetrySeconds = 5d;

        private readonly VideoPlayer _primary;
        private readonly VideoManager _videoManager;
        private readonly SeamlessLoopTimer _timer = new SeamlessLoopTimer();
        private readonly VideoPlayer.FrameReadyEventHandler _onFrameReady;
        private readonly VideoPlayer.EventHandler _onLoopPointReached;
        private readonly VideoPlayer.ErrorEventHandler _onStandbyError;

        private RawImage _image;
        private VideoPlayer _standby;
        private VideoPlayer _active;
        private VideoPlayer.ErrorEventHandler _errorHandler;
        private CancellationTokenSource _pendingCts;
        private double _armedAt;
        private bool _hasBegun;
        private bool _isRunning;
        private bool _isStandbyReady;

        /// <summary>
        /// 화면에 놓인 재생기(primary)를 받음. 두 번째 재생기는 Begin에서 만듦.
        /// </summary>
        public SeamlessVideoLoop(VideoPlayer primary, VideoManager videoManager)
        {
            _primary = primary;
            _videoManager = videoManager;
            _onFrameReady = OnFrameReady;
            _onLoopPointReached = OnLoopPointReached;
            _onStandbyError = OnStandbyError;
        }

        /// <summary>
        /// 화면에 놓인 재생기. 파괴됐으면 이 반복은 더 쓸 수 없음.
        /// </summary>
        public VideoPlayer Primary => _primary;

        /// <summary>
        /// 번갈아 재생하는 중인지 여부.
        /// </summary>
        public bool IsRunning => _isRunning;

        /// <summary>
        /// 쉬는 재생기가 첫 프레임에서 기다리는 중인지 여부.
        /// </summary>
        public bool IsStandbyReady => _isStandbyReady;

        /// <summary>
        /// 두 번째 재생기. 아직 만들지 않았으면 null.
        /// </summary>
        public VideoPlayer Standby => _standby;

        /// <summary>
        /// 지금 화면에 보이는 재생기. 번갈아 재생하지 않으면 null.
        /// </summary>
        public VideoPlayer Active => _active;

        /// <summary>
        /// 재생 준비를 시작함을 알리고 준비에 쓸 취소 소스를 맡김. 준비 중에 Stop을 부르거나 같은 재생기로 다시 시작하면 이 소스가 취소됨.
        /// </summary>
        public void SetPending(CancellationTokenSource cts)
        {
            _pendingCts = cts;
        }

        /// <summary>
        /// 준비가 끝난 취소 소스를 정리하고 true를 돌려줌. 그사이 Stop이나 다른 시작으로 이미 취소됐으면 건드리지 않고 false를 돌려줌.
        /// </summary>
        public bool ClearPending(CancellationTokenSource cts)
        {
            if (_pendingCts != cts) return false;

            _pendingCts = null;
            cts.Dispose();
            return true;
        }

        /// <summary>
        /// primary가 재생을 시작한 뒤 부름. 두 번째 재생기를 만들어(처음 한 번) 첫 프레임에서 멈춰 두고 번갈아 재생을 시작함.
        /// 길이를 알 수 없는 영상이면 재생기의 반복에 맡기고 false를 돌려줌.
        /// errorHandler는 두 번째 재생기의 오류를 받음(primary의 오류는 부르는 쪽이 구독). 보이는 두 번째 재생기에 오류가 나면
        /// primary의 반복으로 이어 재생한 뒤 부름.
        /// </summary>
        public bool Begin(RawImage image, VideoPlayer.ErrorEventHandler errorHandler)
        {
            _hasBegun = true;
            _image = image;
            if (_image) _image.texture = _primary.targetTexture;
            _primary.isLooping = false;
            if (!_timer.TryConfigure(_primary.frameCount, _primary.frameRate * _primary.playbackSpeed))
            {
                _primary.isLooping = true;
                return false;
            }

            if (!_standby) CreateStandby();
            CopySettingsToStandby();
            WireStandbyTexture();

            _errorHandler = errorHandler;
            _standby.errorReceived += _onStandbyError;
            _primary.loopPointReached += _onLoopPointReached;
            _standby.loopPointReached += _onLoopPointReached;
            _primary.frameReady += _onFrameReady;
            _standby.frameReady += _onFrameReady;

            _active = _primary;
            _isRunning = true;
            ArmStandby(_standby);
            return true;
        }

        /// <summary>
        /// 매 프레임 부름. 재생 중인 쪽의 마지막 프레임이 한 프레임 길이만큼 보였으면 쉬는 재생기로 바꿈.
        /// </summary>
        public void Tick(double now)
        {
            if (!_isRunning) return;

            if (!_standby)
            {
                FallBackToPrimary();
                return;
            }

            if (_timer.IsLastFrameShown(_active.frame, now) && _isStandbyReady) Swap();
        }

        /// <summary>
        /// 준비나 재생 중이면 두 재생기를 멈추고 화면을 primary의 텍스처로 되돌림. 다시 시작할 수 있음.
        /// 이미 멈췄으면 아무것도 하지 않음. 그사이 같은 재생기나 RawImage를 다른 곳에 쓰고 있을 수 있기 때문.
        /// </summary>
        public void Stop()
        {
            bool wasPending = CancelPending();
            if (!_hasBegun && !wasPending) return;

            Detach();
            if (_primary)
            {
                _primary.Stop();

                // 두 번째 재생기의 화면일 때만 되돌림. 그사이 다른 재생기가 이 RawImage를 이어받았으면 그 화면을 덮지 않게 하기 위함.
                if (_image && _standby && _image.texture == _standby.targetTexture) _image.texture = _primary.targetTexture;
            }

            _image = null;
            _hasBegun = false;
        }

        /// <summary>
        /// 멈추고 두 번째 재생기를 지움. primary나 VideoManager가 파괴될 때 부름(primary 컴포넌트만 파괴되면 자식 오브젝트가 남음).
        /// </summary>
        public void Release()
        {
            Stop();
            if (_standby) DestroyUtil.SafeDestroy(_standby.gameObject);
        }

        /// <summary>
        /// 준비 중인 시작 요청을 취소하고, 준비 중이었으면 true를 돌려줌.
        /// </summary>
        private bool CancelPending()
        {
            CancellationTokenSource cts = _pendingCts;
            if (cts == null) return false;

            _pendingCts = null;
            cts.Cancel();
            cts.Dispose();
            return true;
        }

        /// <summary>
        /// 두 번째 재생기가 밖에서 파괴됐거나 보이는 동안 오류가 났을 때 primary의 반복으로 이어 재생함. 화면이 멈춘 채 남지 않게 하기 위함.
        /// </summary>
        private void FallBackToPrimary()
        {
            Detach();

            // primary가 막 파괴됐으면 VideoManager.LateUpdate가 정리함.
            if (!_primary) return;

            if (_image && _primary.targetTexture) _image.texture = _primary.targetTexture;
            _primary.isLooping = true;
            _primary.Play();
        }

        /// <summary>
        /// 두 재생기의 이벤트 구독을 풀고 두 번째 재생기를 멈춰 번갈아 재생을 끝냄. primary의 재생은 건드리지 않음.
        /// </summary>
        private void Detach()
        {
            if (_primary)
            {
                _primary.loopPointReached -= _onLoopPointReached;
                _primary.frameReady -= _onFrameReady;
                _primary.sendFrameReadyEvents = false;
            }

            if (_standby)
            {
                _standby.loopPointReached -= _onLoopPointReached;
                _standby.frameReady -= _onFrameReady;
                _standby.errorReceived -= _onStandbyError;
                _standby.sendFrameReadyEvents = false;
                _standby.Stop();
            }

            _errorHandler = null;
            _active = null;
            _isRunning = false;
            _isStandbyReady = false;
            _timer.Reset();
        }

        /// <summary>
        /// primary 아래에 두 번째 재생기를 만듦.
        /// </summary>
        private void CreateStandby()
        {
            GameObject standbyObject = new GameObject("SeamlessLoopStandby");
            standbyObject.transform.SetParent(_primary.transform, false);
            _standby = standbyObject.AddComponent<VideoPlayer>();
            _standby.playOnAwake = false;
            _standby.isLooping = false;
            _standby.source = VideoSource.Url;
            _standby.renderMode = VideoRenderMode.RenderTexture;
            _standby.audioOutputMode = VideoAudioOutputMode.None;
        }

        /// <summary>
        /// primary의 영상 주소와 재생 설정을 두 번째 재생기에 맞춤. 멈춘 사이 바뀐 설정이 한 바퀴씩 걸러 다르게 보이지 않도록 시작할 때마다 부름.
        /// </summary>
        private void CopySettingsToStandby()
        {
            _standby.url = _primary.url;
            _standby.playbackSpeed = _primary.playbackSpeed;
            _standby.aspectRatio = _primary.aspectRatio;
            _standby.skipOnDrop = _primary.skipOnDrop;
            _standby.timeUpdateMode = _primary.timeUpdateMode;
            _standby.waitForFirstFrame = _primary.waitForFirstFrame;
        }

        /// <summary>
        /// 두 번째 재생기에 primary와 같은 크기의 텍스처를 연결함. 이미 같은 크기면 그대로 씀.
        /// </summary>
        private void WireStandbyTexture()
        {
            RenderTexture primaryTexture = _primary.targetTexture;
            RenderTexture standbyTexture = _standby.targetTexture;
            if (standbyTexture && standbyTexture.width == primaryTexture.width && standbyTexture.height == primaryTexture.height) return;

            _videoManager.WireRawImageAndRenderTexture(_standby, null, new Vector2Int(primaryTexture.width, primaryTexture.height));
        }

        /// <summary>
        /// 쉬는 재생기를 처음부터 틀어 첫 프레임이 텍스처에 그려지면 멈추게 함(OnFrameReady).
        /// </summary>
        private void ArmStandby(VideoPlayer player)
        {
            _isStandbyReady = false;
            _armedAt = Time.realtimeSinceStartupAsDouble;
            player.Stop();
            player.sendFrameReadyEvents = true;
            player.Play();
        }

        /// <summary>
        /// 쉬는 재생기의 첫 프레임이 그려지면 그 자리에서 멈춰 둠.
        /// </summary>
        private void OnFrameReady(VideoPlayer source, long frameIndex)
        {
            if (!_isRunning || source == _active || _isStandbyReady) return;

            source.Pause();
            source.sendFrameReadyEvents = false;
            _isStandbyReady = true;
        }

        /// <summary>
        /// 보이는 두 번째 재생기에 오류가 나면 primary의 반복으로 이어 재생함. 쉬는 동안의 오류는 되감을 때 다시 준비시켜 되찾음.
        /// </summary>
        private void OnStandbyError(VideoPlayer source, string message)
        {
            _videoManager.WarnSeamlessLoopError(message);
            VideoPlayer.ErrorEventHandler errorHandler = _errorHandler;
            if (_isRunning)
            {
                if (source == _active)
                {
                    FallBackToPrimary();
                }
                else
                {
                    // 오류 뒤에 늦게 온 첫 프레임 이벤트가 준비됨으로 되돌리지 않게 함. 다시 준비시킬 때(ArmStandby) 켬.
                    source.sendFrameReadyEvents = false;
                    _isStandbyReady = false;
                }
            }

            if (errorHandler != null) errorHandler(source, message);
        }

        /// <summary>
        /// 재생 중인 쪽이 끝에 닿았는데 아직 바꾸지 못했으면 바꾸고, 쉬는 재생기가 준비되지 않았으면 되감아 다시 재생함.
        /// 쉬는 재생기가 오래 준비되지 않으면 다시 준비시킴. 그대로 두면 준비가 한 번 실패한 뒤로 반복마다 멈칫함.
        /// </summary>
        private void OnLoopPointReached(VideoPlayer source)
        {
            if (!_isRunning || source != _active) return;

            if (_isStandbyReady)
            {
                Swap();
                return;
            }

            // 아래 호출 안에서 오류 처리기가 반복을 끝냈으면 멈춘 재생기를 다시 틀지 않음.
            _timer.Reset();
            source.frame = 0;
            if (!_isRunning) return;

            source.Play();
            if (!_isRunning) return;

            VideoPlayer resting = source == _primary ? _standby : _primary;
            if (resting && Time.realtimeSinceStartupAsDouble - _armedAt >= ArmRetrySeconds) ArmStandby(resting);
        }

        /// <summary>
        /// 화면을 쉬는 재생기(첫 프레임에서 멈춰 있음)의 텍스처로 바꿔 이어 재생하고, 재생하던 쪽을 다음 차례를 위해 쉬게 함.
        /// </summary>
        private void Swap()
        {
            VideoPlayer previous = _active;
            VideoPlayer next = previous == _primary ? _standby : _primary;

            // 바꿀 재생기가 밖에서 파괴됐으면 VideoManager.LateUpdate가 정리함(두 번째면 Tick, primary면 Release).
            if (!next) return;

            if (_image) _image.texture = next.targetTexture;
            _active = next;
            _timer.Reset();
            next.Play();

            // Play 안에서 오류 처리기가 반복을 끝냈으면 쉬게 하던 재생기를 다시 틀지 않음.
            if (_isRunning) ArmStandby(previous);
        }
    }
}
