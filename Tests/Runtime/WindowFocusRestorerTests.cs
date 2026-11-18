using System;
using NUnit.Framework;
using UnityEngine.InputSystem;
using VContainer;
using VContainer.Unity;
using HuliacDev.Core;
using HuliacDev.Data;

namespace HuliacDev.Tests
{
    /// <summary>
    /// ToggleFocusRestore 키(F)가 포커스 복구와 함께 AutoRestartWatchdog의 Alt+F4 재시작을 끄고 켜는지 검증함.
    /// 이 연결이 빠지면 점검하려고 F로 끈 뒤 Alt+F4를 눌러도 앱이 다시 켜지거나, 끌 키가 없는데 Alt+F4 재시작이 켜짐.
    /// InputTestFixture로 실제 장치 입력을 막은 가상 키보드에서 실행함. 창 포커스 복구 자체는 Windows 빌드에서만 동작하므로 여기서는 보지 않음.
    /// </summary>
    public class WindowFocusRestorerTests : InputTestFixture
    {
        private Keyboard _keyboard;
        private AppSettingsProvider _settingsProvider;

        /// <summary>
        /// 가상 키보드와 설정 제공자를 만듦.
        /// </summary>
        [SetUp]
        public void SetUpRestorer()
        {
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _settingsProvider = new AppSettingsProvider();
        }

        /// <summary>
        /// 설정 제공자를 해제함. 실제 입력 환경 복원은 이후 기반 클래스의 TearDown이 담당함.
        /// </summary>
        [TearDown]
        public void TearDownRestorer()
        {
            _settingsProvider.Dispose();
        }

        /// <summary>
        /// 토글 키가 있으면 시작할 때 Alt+F4 재시작을 켜고, F 키를 누를 때마다 포커스 복구와 함께 끄고 켜며, 사라질 때 꺼야 함.
        /// </summary>
        [Test]
        public void F_키가_포커스_복구와_함께_Alt_F4_재시작을_끄고_켠다()
        {
            using IObjectResolver resolver = BuildResolver(withInputActions: true);
            WindowFocusRestorer restorer = new WindowFocusRestorer(_settingsProvider, null, resolver);

            try
            {
                ((IStartable)restorer).Start();
                Assert.IsTrue(AutoRestartWatchdog.IsAltF4RestartOn, "토글 키가 있는데 Alt+F4 재시작이 켜지지 않음");

                PressAndRelease(_keyboard.fKey);
                Assert.IsFalse(AutoRestartWatchdog.IsAltF4RestartOn, "F 키로 껐는데 Alt+F4 재시작이 켜져 있음");

                PressAndRelease(_keyboard.fKey);
                Assert.IsTrue(AutoRestartWatchdog.IsAltF4RestartOn, "F 키로 다시 켰는데 Alt+F4 재시작이 꺼져 있음");
            }
            finally
            {
                ((IDisposable)restorer).Dispose();
            }

            Assert.IsFalse(AutoRestartWatchdog.IsAltF4RestartOn, "끌 키가 사라졌는데 Alt+F4 재시작이 켜져 있음");
        }

        /// <summary>
        /// TemplateInputActions가 없으면(ConfigureCoreComponents를 override해 빠진 구성) 점검할 때 끌 방법이 없으므로 Alt+F4 재시작을 켜지 않아야 함.
        /// </summary>
        [Test]
        public void 토글_키가_없으면_Alt_F4_재시작을_켜지_않는다()
        {
            using IObjectResolver resolver = BuildResolver(withInputActions: false);
            WindowFocusRestorer restorer = new WindowFocusRestorer(_settingsProvider, null, resolver);

            try
            {
                ((IStartable)restorer).Start();
                Assert.IsFalse(AutoRestartWatchdog.IsAltF4RestartOn);
            }
            finally
            {
                ((IDisposable)restorer).Dispose();
            }
        }

        /// <summary>
        /// 씬마다 스코프를 두는 구성에서 새 스코프가 옛 스코프보다 먼저 시작돼도, 옛 스코프가 사라지며 새 스코프가 켠 Alt+F4 재시작을 끄지 않아야 함.
        /// </summary>
        [Test]
        public void 먼저_시작한_스코프가_나중에_사라져도_새_스코프의_상태를_끄지_않는다()
        {
            using IObjectResolver oldResolver = BuildResolver(withInputActions: true);
            using IObjectResolver newResolver = BuildResolver(withInputActions: true);
            WindowFocusRestorer oldRestorer = new WindowFocusRestorer(_settingsProvider, null, oldResolver);
            WindowFocusRestorer newRestorer = new WindowFocusRestorer(_settingsProvider, null, newResolver);

            try
            {
                ((IStartable)oldRestorer).Start();
                ((IStartable)newRestorer).Start();

                ((IDisposable)oldRestorer).Dispose();
                Assert.IsTrue(AutoRestartWatchdog.IsAltF4RestartOn, "옛 스코프가 사라지며 새 스코프의 Alt+F4 재시작을 끔");

                ((IDisposable)oldRestorer).Dispose();
                Assert.IsTrue(AutoRestartWatchdog.IsAltF4RestartOn, "옛 스코프를 한 번 더 Dispose하자 새 스코프의 Alt+F4 재시작이 꺼짐");
            }
            finally
            {
                ((IDisposable)oldRestorer).Dispose();
                ((IDisposable)newRestorer).Dispose();
            }

            Assert.IsFalse(AutoRestartWatchdog.IsAltF4RestartOn, "마지막 토글 키가 사라졌는데 Alt+F4 재시작이 켜져 있음");
        }

        /// <summary>
        /// 토글 표시 이름은 항상 위를 켰을 때와 Alt+F4 재시작이 적용될 때만 그 기능을 넣고, 기능이 둘 이상이면 복수로 알려야 함("them").
        /// </summary>
        [Test]
        public void 토글_표시_이름은_적용되는_기능만_넣는다()
        {
            Assert.AreEqual("Focus restore", WindowFocusRestorer.BuildToggleFeatureName(false, false, out bool isPlural));
            Assert.IsFalse(isPlural);

            Assert.AreEqual("Focus restore and always on top", WindowFocusRestorer.BuildToggleFeatureName(true, false, out isPlural));
            Assert.IsTrue(isPlural);

            Assert.AreEqual("Focus restore and Alt+F4 restart", WindowFocusRestorer.BuildToggleFeatureName(false, true, out isPlural));
            Assert.IsTrue(isPlural);

            Assert.AreEqual("Focus restore, always on top and Alt+F4 restart", WindowFocusRestorer.BuildToggleFeatureName(true, true, out isPlural));
            Assert.IsTrue(isPlural);
        }

        /// <summary>
        /// 토글 표시 둘째 줄은 감시 상태를 알리고, 토글 키로 꺼도 크래시·응답 없음 재시작은 남는다는 것과 응답 없음 감지가 꺼진 경우를 구분해야 함.
        /// 감시가 뜨는 중에는 Alt+F4를 다시 켜지 않고 크래시도 다시 켜지 않을 수 있으므로 무엇을 다시 켠다고 적지 않고, 감시가 없으면(에디터 포함) 꺼짐으로 알려야 함.
        /// </summary>
        [Test]
        public void 감시_상태_줄은_다시_켜는_경우를_상태에_맞게_적는다()
        {
            Assert.AreEqual("Watchdog: watching (restarts after crash, hang or Alt+F4)",
                WindowFocusRestorer.BuildWatchdogStatusLine(AutoRestartWatchdog.WatchStatus.Watching, true, true));
            Assert.AreEqual("Watchdog: watching (still restarts after crash or hang)",
                WindowFocusRestorer.BuildWatchdogStatusLine(AutoRestartWatchdog.WatchStatus.Watching, false, true));
            Assert.AreEqual("Watchdog: watching (restarts after crash or Alt+F4)",
                WindowFocusRestorer.BuildWatchdogStatusLine(AutoRestartWatchdog.WatchStatus.Watching, true, false));
            Assert.AreEqual("Watchdog: watching (still restarts after crash)",
                WindowFocusRestorer.BuildWatchdogStatusLine(AutoRestartWatchdog.WatchStatus.Watching, false, false));
            Assert.AreEqual("Watchdog: starting (restarts apply once it is watching)",
                WindowFocusRestorer.BuildWatchdogStatusLine(AutoRestartWatchdog.WatchStatus.Starting, true, true),
                "감시가 앱을 잡기 전인데 Alt+F4 등으로 다시 켠다고 알림");
            Assert.AreEqual("Watchdog: off (disabled, Editor or not running - see AutoRestart.log)",
                WindowFocusRestorer.BuildWatchdogStatusLine(AutoRestartWatchdog.WatchStatus.Off, true, true));
            Assert.AreEqual(AutoRestartWatchdog.WatchStatus.Off, AutoRestartWatchdog.CurrentWatchStatus, "에디터에서는 감시가 없음");
        }

        /// <summary>
        /// 화면 표시용 감시 상태 줄은 켜짐·꺼짐을 한눈에 구분하도록 상태 단어에만 색을 입히고(지켜보는 중 초록, 뜨는 중 노랑, 꺼짐 빨강),
        /// 나머지 글은 로그용 줄과 같아야 함.
        /// </summary>
        [Test]
        public void 화면용_감시_상태_줄은_상태_단어에만_색을_입힌다()
        {
            Assert.AreEqual("Watchdog: <color=#5BD75B>watching</color> (still restarts after crash or hang)",
                WindowFocusRestorer.BuildWatchdogStatusLine(AutoRestartWatchdog.WatchStatus.Watching, false, true, true));
            Assert.AreEqual("Watchdog: <color=#FFD24A>starting</color> (restarts apply once it is watching)",
                WindowFocusRestorer.BuildWatchdogStatusLine(AutoRestartWatchdog.WatchStatus.Starting, true, true, true));
            Assert.AreEqual("Watchdog: <color=#FF5A5A>off</color> (disabled, Editor or not running - see AutoRestart.log)",
                WindowFocusRestorer.BuildWatchdogStatusLine(AutoRestartWatchdog.WatchStatus.Off, true, true, true));
        }

        /// <summary>
        /// 토글 표시의 각 줄이 상자 너비를 넘으면 글자를 줄여 한 줄에 맞춰야 하고, 들어가면 그대로 둬야 함.
        /// 너무 작아지면 읽을 수 없으므로 기본 크기의 60%와 최소 글자 크기(16) 아래로는 줄이지 않아야 함(그때는 줄을 바꿈).
        /// </summary>
        [Test]
        public void 각_줄이_넘치면_글자를_줄여_한_줄에_맞추고_하한_아래로는_줄이지_않는다()
        {
            Assert.AreEqual(43, StatusMessageOverlay.ResolveFitFontSize(43, 1000f, 1700f), "들어가는데 글자를 줄임");
            Assert.AreEqual(34, StatusMessageOverlay.ResolveFitFontSize(43, 1200f, 1000f), "넘친 비율만큼 줄이지 않음(내림 후 1 뺌)");
            Assert.AreEqual(26, StatusMessageOverlay.ResolveFitFontSize(43, 3000f, 1000f), "기본 크기의 60% 아래로 줄임");
            Assert.AreEqual(16, StatusMessageOverlay.ResolveFitFontSize(16, 3000f, 1000f), "최소 글자 크기 아래로 줄임");
        }

        /// <summary>
        /// 토글 키(TemplateInputActions)를 넣거나 뺀 컨테이너를 만듦. 컨테이너를 해제하면 입력 액션도 함께 해제됨.
        /// </summary>
        private static IObjectResolver BuildResolver(bool withInputActions)
        {
            ContainerBuilder builder = new ContainerBuilder();
            if (withInputActions)
            {
                builder.Register<TemplateInputActions>(Lifetime.Singleton);
            }
            return builder.Build();
        }
    }
}
