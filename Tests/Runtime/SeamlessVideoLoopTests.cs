using System;
using System.Collections;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using HuliacDev.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.Video;

namespace HuliacDev.Tests
{
    /// <summary>
    /// VideoManager.PlaySeamlessLoopAsync가 실제 영상을 재생기 두 개로 번갈아 트는지 검증.
    /// 영상은 이 파일에 넣어 둔 1초(128x128, 30fps, 30프레임) mp4를 임시 폴더에 써서 사용함.
    /// </summary>
    public class SeamlessVideoLoopTests
    {
        private const string VideoBase64 =
            "AAAAIGZ0eXBpc29tAAACAGlzb21pc28yYXZjMW1wNDEAAAOkbW9vdgAAAGxtdmhkAAAAAAAAAAAAAAAAAAAD6AAAA+gAAQAAAQAAAAAAAAAAAAAAAAEAAAAA" +
            "AAAAAAAAAAAAAAABAAAAAAAAAAAAAAAAAABAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAgAAAs50cmFrAAAAXHRraGQAAAADAAAAAAAAAAAAAAAB" +
            "AAAAAAAAA+gAAAAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAABAAAAAAAAAAAAAAAAAABAAAAAAIAAAACAAAAAAAAkZWR0cwAAABxlbHN0AAAAAAAA" +
            "AAEAAAPoAAAAAAABAAAAAAJGbWRpYQAAACBtZGhkAAAAAAAAAAAAAAAAAAA8AAAAPABVxAAAAAAALWhkbHIAAAAAAAAAAHZpZGUAAAAAAAAAAAAAAABWaWRl" +
            "b0hhbmRsZXIAAAAB8W1pbmYAAAAUdm1oZAAAAAEAAAAAAAAAAAAAACRkaW5mAAAAHGRyZWYAAAAAAAAAAQAAAAx1cmwgAAAAAQAAAbFzdGJsAAAAwXN0c2QA" +
            "AAAAAAAAAQAAALFhdmMxAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAAAAIAAgABIAAAASAAAAAAAAAABFUxhdmM2Mi4yOC4xMDEgbGlieDI2NAAAAAAAAAAAAAAA" +
            "GP//AAAAN2F2Y0MBZAAM/+EAGWdkAAysTCIQI2AiAAADAAIAAAMAeB4oVCMBAAdo6EMDksiw/fj4AAAAABBwYXNwAAAAAQAAAAEAAAAUYnRydAAAAAAAAB2o" +
            "AAAAAAAAABhzdHRzAAAAAAAAAAEAAAAeAAACAAAAABRzdHNzAAAAAAAAAAEAAAABAAAAHHN0c2MAAAAAAAAAAQAAAAEAAAAeAAAAAQAAAIxzdHN6AAAAAAAA" +
            "AAAAAAAeAAAATwAAAB8AAAAwAAAAFwAAABgAAAAcAAAAGwAAABwAAAAbAAAAHAAAAB0AAAAdAAAAHQAAAB0AAAAeAAAAIAAAAB4AAAAeAAAAHgAAAB4AAAAe" +
            "AAAAIQAAAB4AAAAeAAAAHgAAAB4AAAAgAAAAHwAAAB8AAAAfAAAAFHN0Y28AAAAAAAAAAQAAA9QAAABidWR0YQAAAFptZXRhAAAAAAAAACFoZGxyAAAAAAAA" +
            "AABtZGlyYXBwbAAAAAAAAAAAAAAAAC1pbHN0AAAAJal0b28AAAAdZGF0YQAAAAEAAAAATGF2ZjYyLjEyLjEwMQAAAAhmcmVlAAADvW1kYXQAAABLZYiCBP/9" +
            "GfSXyll+AAHZgAZqS0UAeorawFlPhC6fhziH5wheIfu1KVXr9TIpL3zTfKcPbybuZzpu4JQ5zUUgE2jhaBDI07C+w2NHAAAAG0GaHYhP/wRBirlfFjnYBCHA" +
            "LXvWFGboD1RsIAAAACxBmivwQZMphCf/BD1pv2U22vUe52hi3hf+StC4DrDMOuCuR/+rkr+z56u5EQAAABNBmjk8EHkymBCfBAfCvXpGveDaAAAAFEGaSXwQ" +
            "fkymAQn/BAkvUNTv+8G1AAAAGEGaWbwQf5MpgBCfBEeG9FTbhpt8M5uMaQAAABdBmmn8EH/kymABCf8DvzF/dQzTaFLRgQAAABhBmniPBB/+TKYABCf/A73u" +
            "ED4BIl/biIEAAAAXQZqInwQf/5MpgABCfwO/MX907gHi+kYAAAAYQZqYrwQf/+TKYAAEJ/8DveiIZgOJ4H0jAAAAGUGaqL8EH//5MpgAAEL/Af/CXtkz2vgq" +
            "KUEAAAAZQZq4zwQf//5MpgAABC//AcvLOWJozlRYEAAAABlBmsjfBB///5MpgAAAQv8By+MDVofNsiIgAAAAGUGa2O8EH///5MpgAAAEL/8BstLbpE/eCB0A" +
            "AAAaQZro/wQf///5MpgAAAMAQv8Bs3Wygoxnp7kAAAAcQZr3gg////8mUwAAAwACF/8Bz8JZshYel4ggoQAAABpBmweCD////yZTAAADAAIX/wGRGrKCjGeo" +
            "KAAAABpBmxeCD////yZTAAADAAIX/wGQkOpvX1/CWAAAABpBmyeCD////yZTAAADAAIX/wGRGrKCjGeoKQAAABpBmzeCD////yZTAAADAAIX/wGQjrBY6UaZ" +
            "ugAAABpBm0eCD////yZTAAADAAIX/wGRGrKCjGeoKAAAAB1Bm1eCD////yZTAAADAAIX/wGo048HYirZVmkKfQAAABpBm2eCD////yZTAAADAAIX/wFyCrKC" +
            "rGa3OAAAABpBm3eCD////yZTAAADAAIX/wFxgOpwTxdhmQAAABpBm4eCD////yZTAAADAAIX/wFyCrKCrGa3OQAAABpBm5eCD////yZTAAADAAIX/wFxfrBY" +
            "0U6UTgAAABxBm6eCD////yZTAAADAAIX/wGI05OBn9r4Kq9BAAAAG0Gbt4IP////JlMAAAMAAhf/AVes9GxNPhi91AAAABtBm8eCD////yZTAAADAAIV/wIp" +
            "qL0b6S2rytEAAAAbQZvXgg////8mUwAAAwACE/8C2+FlvMqGeZHp";

        private GameObject _managerGo;
        private GameObject _videoGo;
        private VideoManager _videoManager;
        private VideoPlayer _player;
        private RawImage _image;
        private string _url;

        /// <summary>
        /// 시험 영상을 임시 폴더에 한 번만 씀. 재생기가 파괴된 직후에는 파일이 아직 열려 있어 테스트마다 다시 쓸 수 없음.
        /// </summary>
        [OneTimeSetUp]
        public void WriteVideoFile()
        {
            string path = Path.Combine(Application.temporaryCachePath, "SeamlessVideoLoopTests.mp4");
            File.WriteAllBytes(path, Convert.FromBase64String(VideoBase64));
            _url = new Uri(path).AbsoluteUri;
        }

        [SetUp]
        public void SetUp()
        {
            _managerGo = new GameObject("SeamlessVideoLoopTests");
            _videoManager = _managerGo.AddComponent<VideoManager>();

            _videoGo = new GameObject("Video");
            _image = _videoGo.AddComponent<RawImage>();
            _player = _videoGo.AddComponent<VideoPlayer>();
            _videoManager.WireRawImageAndRenderTexture(_player, _image, new Vector2Int(128, 128));
        }

        [TearDown]
        public void TearDown()
        {
            if (_videoGo) UnityEngine.Object.DestroyImmediate(_videoGo);
            if (_managerGo) UnityEngine.Object.DestroyImmediate(_managerGo);
        }

        /// <summary>
        /// 1초 영상을 몇 바퀴 트는 동안 화면이 두 재생기의 텍스처를 번갈아 보이고, 보이는 쪽이 늘 재생 중이어야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator 두_재생기를_번갈아_보이며_계속_재생한다() => UniTask.ToCoroutine(async () =>
        {
            SeamlessVideoLoop loop = await PlayAsync();

            int swaps = await CountSwapsAsync(loop, 4.5f);

            Assert.GreaterOrEqual(swaps, 3, "반복 지점에서 재생기가 바뀌지 않음");
            Assert.IsTrue(loop.Active.isPlaying);
            Assert.AreSame(loop.Active.targetTexture, _image.texture, "보이는 텍스처가 재생 중인 쪽의 것이 아님");
        });

        /// <summary>
        /// 쉬는 재생기가 준비되지 않으면 재생 중인 쪽을 되감아 이어 재생해야 함. 처리가 없으면 마지막 프레임에서 멈춰 버림.
        /// </summary>
        [UnityTest]
        public IEnumerator 쉬는_재생기가_준비되지_않으면_되감아_이어_재생한다() => UniTask.ToCoroutine(async () =>
        {
            SeamlessVideoLoop loop = await PlayAsync();
            loop.Standby.enabled = false;

            int rewinds = 0;
            long previousFrame = -1;
            float end = Time.realtimeSinceStartup + 4.5f;
            while (Time.realtimeSinceStartup < end)
            {
                if (_player.frame >= 0 && _player.frame < previousFrame) rewinds++;
                previousFrame = _player.frame;
                await UniTask.Yield();
            }

            Assert.AreSame(_player, loop.Active);
            Assert.GreaterOrEqual(rewinds, 2, "되감아 이어 재생하지 않음");
        });

        /// <summary>
        /// 멈추면 두 재생기가 모두 멈추고 화면이 원래 재생기의 텍스처로 돌아가며, 다시 시작하면 번갈아 재생해야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator 멈춘_뒤_다시_시작할_수_있다() => UniTask.ToCoroutine(async () =>
        {
            SeamlessVideoLoop loop = await PlayAsync();
            await UniTask.WaitUntil(() => loop.Active == loop.Standby).AwaitWithRealtimeTimeout(5f, "두 번째 재생기로 바뀌지 않음");

            _videoManager.StopSeamlessLoop(_player);

            Assert.IsFalse(loop.IsRunning);
            Assert.IsFalse(_player.isPlaying);
            Assert.IsFalse(loop.Standby.isPlaying);
            Assert.AreSame(_player.targetTexture, _image.texture);

            VideoPlayer standby = loop.Standby;
            Assert.AreSame(loop, await PlayAsync(), "같은 VideoPlayer의 반복은 다시 써야 함");
            Assert.AreSame(standby, loop.Standby, "두 번째 재생기를 다시 만들어 쌓이게 함");
            Assert.GreaterOrEqual(await CountSwapsAsync(loop, 2.5f), 1);
        });

        /// <summary>
        /// 준비 중에 같은 VideoPlayer로 다시 부르면 앞의 호출은 false로 끝나고 나중 호출로 번갈아 재생해야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator 겹쳐_부르면_나중_호출로_번갈아_재생한다() => UniTask.ToCoroutine(async () =>
        {
            UniTask<bool> first = _videoManager.PlaySeamlessLoopAsync(_player, _image, _url, CancellationToken.None);
            UniTask<bool> second = _videoManager.PlaySeamlessLoopAsync(_player, _image, _url, CancellationToken.None);
            (bool firstPlayed, bool secondPlayed) = await UniTask.WhenAll(first, second).AwaitWithRealtimeTimeout(15f);

            Assert.IsFalse(firstPlayed);
            Assert.IsTrue(secondPlayed);
            Assert.GreaterOrEqual(await CountSwapsAsync(_videoManager.FindSeamlessLoop(_player), 3.5f), 2);
        });

        /// <summary>
        /// 준비 중에 멈추면 준비 제한 시간(30초)을 기다리지 않고 바로 false로 끝나고 재생하지 않아야 함.
        /// 한 번 쓴 VideoPlayer(반복이 목록에 남음)로 다시 시작할 때도 같아야 하므로 두 번째 시작에서 확인함.
        /// </summary>
        [UnityTest]
        public IEnumerator 준비_중에_멈추면_바로_false로_끝난다() => UniTask.ToCoroutine(async () =>
        {
            SeamlessVideoLoop loop = await PlayAsync();
            _videoManager.StopSeamlessLoop(_player);

            UniTask<bool> task = _videoManager.PlaySeamlessLoopAsync(_player, _image, _url, CancellationToken.None);
            _videoManager.StopSeamlessLoop(_player);

            Assert.IsFalse(await task.AwaitWithRealtimeTimeout(5f));
            await UniTask.Delay(500, DelayType.Realtime);
            Assert.IsFalse(loop.IsRunning);
            Assert.IsFalse(_player.isPlaying);
        });

        /// <summary>
        /// 이미 멈춘 반복을 다시 멈춰도 그사이 다른 용도로 쓰는 RawImage를 건드리지 않아야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator 이미_멈췄으면_다시_멈춰도_화면을_건드리지_않는다() => UniTask.ToCoroutine(async () =>
        {
            await PlayAsync();
            _videoManager.StopSeamlessLoop(_player);
            _image.texture = Texture2D.whiteTexture;

            _videoManager.StopSeamlessLoop(_player);

            Assert.AreSame(Texture2D.whiteTexture, _image.texture);
        });

        /// <summary>
        /// 다른 재생기가 같은 RawImage를 이어받은 뒤 앞의 반복을 멈춰도 새 화면을 덮지 않아야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator 화면을_넘겨준_뒤_멈춰도_새_화면을_덮지_않는다() => UniTask.ToCoroutine(async () =>
        {
            await PlayAsync();
            _image.texture = Texture2D.whiteTexture;

            _videoManager.StopSeamlessLoop(_player);

            Assert.AreSame(Texture2D.whiteTexture, _image.texture);
        });

        /// <summary>
        /// 반복 중인 VideoPlayer로 일반 재생을 시작하면 반복이 끝나야 함. 남으면 끝에서 이전 영상으로 되돌아감.
        /// </summary>
        [UnityTest]
        public IEnumerator 일반_재생을_시작하면_반복을_끝낸다() => UniTask.ToCoroutine(async () =>
        {
            SeamlessVideoLoop loop = await PlayAsync();
            await UniTask.WaitUntil(() => loop.Active == loop.Standby).AwaitWithRealtimeTimeout(5f, "두 번째 재생기로 바뀌지 않음");

            bool played = await _videoManager.PrepareAndPlayAsync(_player, _url, null, 0f, CancellationToken.None).AwaitWithRealtimeTimeout(15f);

            Assert.IsTrue(played);
            Assert.IsFalse(loop.IsRunning);
            Assert.IsTrue(_player.isPlaying);
            Assert.AreSame(_player.targetTexture, _image.texture);
        });

        /// <summary>
        /// 두 번째 재생기가 밖에서 파괴되면(자식 오브젝트 정리 등) 예외나 멈춤 없이 원래 재생기의 반복으로 이어져야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator 두_번째_재생기가_파괴되면_원래_재생기의_반복으로_이어진다() => UniTask.ToCoroutine(async () =>
        {
            SeamlessVideoLoop loop = await PlayAsync();
            await UniTask.WaitUntil(() => loop.Active == loop.Standby).AwaitWithRealtimeTimeout(5f, "두 번째 재생기로 바뀌지 않음");

            UnityEngine.Object.DestroyImmediate(loop.Standby.gameObject);
            await UniTask.Delay(1500, DelayType.Realtime);

            Assert.IsFalse(loop.IsRunning);
            Assert.IsTrue(_player.isLooping);
            Assert.IsTrue(_player.isPlaying);
            Assert.AreSame(_player.targetTexture, _image.texture);
        });

        /// <summary>
        /// VideoPlayer 컴포넌트만 파괴돼도 두 번째 재생기가 남지 않아야 함. 남으면 보이지 않는 디코딩이 계속됨.
        /// </summary>
        [UnityTest]
        public IEnumerator 재생기가_파괴되면_두_번째_재생기도_정리한다() => UniTask.ToCoroutine(async () =>
        {
            SeamlessVideoLoop loop = await PlayAsync();
            VideoPlayer standby = loop.Standby;

            UnityEngine.Object.DestroyImmediate(_player);
            await UniTask.DelayFrame(3);

            Assert.IsFalse(loop.IsRunning);
            Assert.IsFalse(standby, "두 번째 재생기가 남음");
        });

        /// <summary>
        /// 텍스처를 연결하지 않았거나 RawImage가 없으면 재생하지 않고 false를 돌려줘야 함.
        /// </summary>
        [UnityTest]
        public IEnumerator 연결이_없으면_재생하지_않고_false를_돌려준다() => UniTask.ToCoroutine(async () =>
        {
            bool withoutImage = await _videoManager.PlaySeamlessLoopAsync(_player, null, _url, CancellationToken.None).AwaitWithRealtimeTimeout();
            _player.targetTexture = null;
            bool withoutTexture = await _videoManager.PlaySeamlessLoopAsync(_player, _image, _url, CancellationToken.None).AwaitWithRealtimeTimeout();

            Assert.IsFalse(withoutImage);
            Assert.IsFalse(withoutTexture);
            Assert.IsNull(_videoManager.FindSeamlessLoop(_player));
        });

        /// <summary>
        /// 반복 재생을 시작하고 그 반복을 돌려줌.
        /// </summary>
        private async UniTask<SeamlessVideoLoop> PlayAsync()
        {
            bool played = await _videoManager.PlaySeamlessLoopAsync(_player, _image, _url, CancellationToken.None).AwaitWithRealtimeTimeout(15f);
            Assert.IsTrue(played, "재생을 시작하지 못함");

            SeamlessVideoLoop loop = _videoManager.FindSeamlessLoop(_player);
            Assert.IsTrue(loop.IsRunning, "번갈아 재생하지 않고 VideoPlayer의 반복으로 넘어감");
            return loop;
        }

        /// <summary>
        /// 실시간으로 지정한 초 동안 화면에 보이는 재생기가 바뀐 횟수를 셈.
        /// </summary>
        private static async UniTask<int> CountSwapsAsync(SeamlessVideoLoop loop, float seconds)
        {
            int swaps = 0;
            VideoPlayer active = loop.Active;
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                await UniTask.Yield();
                if (loop.Active == active) continue;

                active = loop.Active;
                swaps++;
            }

            return swaps;
        }
    }
}
