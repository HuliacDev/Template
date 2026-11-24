using HuliacDev.UI;
using NUnit.Framework;

namespace HuliacDev.Tests
{
    /// <summary>
    /// SeamlessLoopTimer가 다음 재생기로 바꿀 때를 바르게 정하는지 검증.
    /// </summary>
    public class SeamlessLoopTimerTests
    {
        private const long LastFrame = 29;

        /// <summary>
        /// 마지막 프레임을 처음 본 때가 아니라 한 프레임 길이(30fps에서 약 33ms, 허용 오차 8ms)가 지난 뒤에 바꿔야 함.
        /// </summary>
        [Test]
        public void 마지막_프레임이_한_프레임_길이만큼_보인_뒤에_바꾼다()
        {
            SeamlessLoopTimer timer = CreateTimer();

            Assert.IsFalse(timer.IsLastFrameShown(LastFrame - 1, 10d));
            Assert.IsFalse(timer.IsLastFrameShown(LastFrame, 10.033d), "마지막 프레임을 처음 보자마자 바꾸면 그 프레임이 짧아짐");
            Assert.IsFalse(timer.IsLastFrameShown(LastFrame, 10.050d));
            Assert.IsTrue(timer.IsLastFrameShown(LastFrame, 10.0665d));
        }

        /// <summary>
        /// 되감아 다시 재생하면 이전 바퀴에서 본 시각을 버려야 함. 남으면 재생 도중이나 다음 바퀴 끝에서 너무 일찍 바뀜.
        /// </summary>
        [Test]
        public void 되감으면_이전에_본_시각을_버린다()
        {
            SeamlessLoopTimer timer = CreateTimer();

            timer.IsLastFrameShown(LastFrame, 10d);
            Assert.IsFalse(timer.IsLastFrameShown(3, 10.2d));
            Assert.IsFalse(timer.IsLastFrameShown(LastFrame, 11d), "다음 바퀴의 마지막 프레임을 처음 보자마자 바꿈");
        }

        /// <summary>
        /// 재생기를 바꾼 뒤 Reset하면 다음 바퀴의 마지막 프레임을 새로 기다려야 함.
        /// </summary>
        [Test]
        public void Reset하면_다시_한_프레임_길이를_기다린다()
        {
            SeamlessLoopTimer timer = CreateTimer();

            timer.IsLastFrameShown(LastFrame, 10d);
            timer.Reset();

            Assert.IsFalse(timer.IsLastFrameShown(LastFrame, 10.1d));
        }

        /// <summary>
        /// 프레임 수나 프레임레이트를 알 수 없거나 한 프레임뿐이면 번갈아 틀 수 없음을 알려야 함.
        /// </summary>
        [TestCase(0ul, 30f)]
        [TestCase(1ul, 30f)]
        [TestCase(30ul, 0f)]
        public void 길이를_알_수_없으면_false를_돌려준다(ulong frameCount, float frameRate)
        {
            Assert.IsFalse(new SeamlessLoopTimer().TryConfigure(frameCount, frameRate));
        }

        /// <summary>
        /// 30fps, 30프레임 영상의 타이머를 만듦.
        /// </summary>
        private static SeamlessLoopTimer CreateTimer()
        {
            SeamlessLoopTimer timer = new SeamlessLoopTimer();
            Assert.IsTrue(timer.TryConfigure((ulong)LastFrame + 1, 30f));
            return timer;
        }
    }
}
