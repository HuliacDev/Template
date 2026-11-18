using NUnit.Framework;
using HuliacDev.Network;
using HuliacDev.Utils;

namespace HuliacDev.Tests
{
    /// <summary>
    /// ApiManagerBase가 서버로 보내는 시작·종료 로그 메시지 검증.
    /// 서버 로그만 보고 앱을 누가 켜고 껐는지 구분하므로, 문구가 바뀌면 서버 쪽 집계가 틀어짐.
    /// </summary>
    public class ApiLogMessageTests
    {
        /// <summary>
        /// 그날 처음 켜면 "start", 같은 날 사람이 다시 켜면 "start (user restart)", 자동 재시작 감시가 켜면 "start (auto restart)"여야 함.
        /// 감시가 켠 실행은 그날 처음이어도 감시가 켠 것으로 남겨야 자정 무렵 크래시도 구분됨.
        /// </summary>
        [Test]
        public void 시작_로그는_처음_사람의_재시작_감시의_재시작을_구분한다()
        {
            Assert.AreEqual("start", ApiManagerBase.BuildStartupMessage(wasAutoRestarted: false, alreadyLoggedToday: false));
            Assert.AreEqual("start (user restart)", ApiManagerBase.BuildStartupMessage(wasAutoRestarted: false, alreadyLoggedToday: true));
            Assert.AreEqual("start (auto restart)", ApiManagerBase.BuildStartupMessage(wasAutoRestarted: true, alreadyLoggedToday: true));
            Assert.AreEqual("start (auto restart)", ApiManagerBase.BuildStartupMessage(wasAutoRestarted: true, alreadyLoggedToday: false));
        }

        /// <summary>
        /// 종료 로그는 "by" 없이 종료 사유를 괄호에 넣어야 함: "end (Alt + F4)", "end (Shutdown Scheduler)".
        /// </summary>
        [Test]
        public void 종료_로그는_사유를_괄호에_넣는다()
        {
            Assert.AreEqual("end (Alt + F4)", ApiManagerBase.BuildExitMessage(QuitReason.AltF4));
            Assert.AreEqual("end (Shutdown Scheduler)", ApiManagerBase.BuildExitMessage(QuitReason.ShutdownScheduler));
            Assert.AreEqual("end (Monitor Reconnect)", ApiManagerBase.BuildExitMessage("Monitor Reconnect"));
        }
    }
}
