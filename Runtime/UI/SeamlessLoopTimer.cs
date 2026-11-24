namespace HuliacDev.UI
{
    /// <summary>
    /// SeamlessVideoLoop가 다음 재생기로 바꿀 때를 정하는 계산.
    /// 재생 중인 쪽의 마지막 프레임이 한 프레임 길이만큼 보였는지만 따지며, Unity 객체 없이 검증할 수 있게 나눔.
    /// </summary>
    internal sealed class SeamlessLoopTimer
    {
        // 한 프레임 길이에서 이만큼 모자라도 바꿈(Update 간격의 흔들림으로 한 번 더 기다려 마지막 프레임이 길어지지 않게).
        private const double SwapToleranceSeconds = 0.008;
        private const double NotSeen = -1d;

        private double _lastFrameSeenAt = NotSeen;
        private double _frameSeconds;
        private long _lastFrameIndex;

        /// <summary>
        /// 영상의 프레임 수와 프레임레이트로 기준을 정하고 true를 돌려줌. 길이를 알 수 없는 영상이면 false를 돌려줌.
        /// </summary>
        public bool TryConfigure(ulong frameCount, float frameRate)
        {
            Reset();
            _lastFrameIndex = (long)frameCount - 1;
            _frameSeconds = frameRate > 0f ? 1d / frameRate : 0d;
            return _lastFrameIndex >= 1 && _frameSeconds > 0d;
        }

        /// <summary>
        /// 마지막 프레임을 본 시각을 버림. 재생기를 바꾸거나 되감은 뒤 부름.
        /// </summary>
        public void Reset()
        {
            _lastFrameSeenAt = NotSeen;
        }

        /// <summary>
        /// 재생 중인 쪽의 지금 프레임과 시각을 받아, 마지막 프레임이 한 프레임 길이만큼 보였으면 true를 돌려줌.
        /// 마지막 프레임을 처음 본 호출은 시각만 기억함.
        /// </summary>
        public bool IsLastFrameShown(long frame, double now)
        {
            if (frame < _lastFrameIndex)
            {
                // 되감아 다시 재생한 뒤에는 남은 시각을 버림. 남겨 두면 재생 도중에 바뀜.
                _lastFrameSeenAt = NotSeen;
                return false;
            }

            if (_lastFrameSeenAt < 0d)
            {
                _lastFrameSeenAt = now;
                return false;
            }

            return now - _lastFrameSeenAt >= _frameSeconds - SwapToleranceSeconds;
        }
    }
}
