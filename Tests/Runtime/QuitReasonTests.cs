using NUnit.Framework;
using HuliacDev.Utils;

namespace HuliacDev.Tests
{
    /// <summary>
    /// 종료 사유와 종료 코드, 진행 중 표시(QuitReason) 검증.
    /// ApiManagerBase는 종료 로그를 보내려고 종료를 미뤘다가 QuitReason.ExitCode로 다시 끝내므로,
    /// 이 값이 틀리면 자동 재시작 감시가 다시 켤지 판단이 틀어짐.
    /// </summary>
    public class QuitReasonTests
    {
        [SetUp]
        public void SetUp()
        {
            QuitReason.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            QuitReason.Clear();
        }

        /// <summary>
        /// 종료 코드를 함께 적으면 그 값이 남고 종료 진행 중으로 표시돼야 함. 코드가 남지 않으면 ApiManagerBase가 0으로 끝내 감시가 다시 켜지 않음.
        /// </summary>
        [Test]
        public void 종료_코드를_함께_적으면_그_값이_남고_진행_중으로_표시된다()
        {
            QuitReason.Set("Monitor Reconnect", 75);

            Assert.AreEqual("Monitor Reconnect", QuitReason.Current);
            Assert.AreEqual(75, QuitReason.ExitCode);
            Assert.IsTrue(QuitReason.IsQuitRequested);
        }

        /// <summary>
        /// 사유만 적는 경로(ShutdownScheduler)는 일부러 끄는 것이므로 앞서 적힌 종료 코드를 0으로 되돌려야 함.
        /// 남아 있으면 예약 종료로 끈 앱을 감시가 다시 켬.
        /// </summary>
        [Test]
        public void 사유만_적으면_종료_코드를_0으로_되돌린다()
        {
            QuitReason.Set("Monitor Reconnect", 75);

            QuitReason.Set(QuitReason.ShutdownScheduler);

            Assert.AreEqual(QuitReason.ShutdownScheduler, QuitReason.Current);
            Assert.AreEqual(0, QuitReason.ExitCode);
        }

        /// <summary>
        /// 종료를 취소한 뒤 Clear하면 기본값으로 돌아가야 함. 75가 남으면 이후 Alt+F4로 끌 때 감시가 다시 켜고,
        /// 진행 중 표시가 남으면 재시작 요청이 계속 거절됨.
        /// </summary>
        [Test]
        public void 취소한_종료를_지우면_기본값으로_돌아간다()
        {
            QuitReason.Set("Monitor Reconnect", 75);

            QuitReason.Clear();

            Assert.AreEqual(QuitReason.AltF4, QuitReason.Current);
            Assert.AreEqual(0, QuitReason.ExitCode);
            Assert.IsFalse(QuitReason.IsQuitRequested);
        }

        /// <summary>
        /// 사유를 적지 않은 종료(Alt+F4)를 미룰 때 진행 중으로 표시하고, 넘긴 종료 코드(감시가 다시 켜는 76 또는 0)로 끝내야 함.
        /// 사유는 기본값 AltF4로 남아 종료 로그가 "end (Alt + F4)"가 됨.
        /// </summary>
        [Test]
        public void 사유_없는_종료는_진행_중으로_표시하고_넘긴_종료_코드를_쓴다()
        {
            QuitReason.MarkQuitRequested(76);

            Assert.IsTrue(QuitReason.IsQuitRequested);
            Assert.AreEqual(QuitReason.AltF4, QuitReason.Current);
            Assert.AreEqual(76, QuitReason.ExitCode);
        }

        /// <summary>
        /// 이미 진행 중인 종료는 사유 없는 종료 표시가 사유·종료 코드를 덮어쓰지 않아야 함. 덮어쓰면 예약 종료가 Alt+F4 코드로 끝나
        /// 감시가 다시 켜고, Alt+F4 처리기가 미룬 종료를 ApiManagerBase가 다시 표시할 때 코드가 바뀜.
        /// </summary>
        [Test]
        public void 진행_중인_종료는_사유_없는_종료_표시가_덮어쓰지_않는다()
        {
            QuitReason.Set(QuitReason.ShutdownScheduler);
            QuitReason.MarkQuitRequested(76);

            Assert.AreEqual(QuitReason.ShutdownScheduler, QuitReason.Current);
            Assert.AreEqual(0, QuitReason.ExitCode);

            QuitReason.Clear();
            QuitReason.MarkQuitRequested(76);
            QuitReason.MarkQuitRequested(0);

            Assert.AreEqual(76, QuitReason.ExitCode, "먼저 정한 Alt+F4 코드가 바뀜");
        }
    }
}
