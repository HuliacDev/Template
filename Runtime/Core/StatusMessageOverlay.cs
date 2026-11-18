using UnityEngine;

namespace HuliacDev.Core
{
    /// <summary>
    /// 운영자 단축키로 바뀐 상태를 화면 위쪽에 잠깐 보여 주는 IMGUI 오버레이.
    /// 씬·프리팹 배치 없이 코드로 만들어 쓰며(WindowFocusRestorer), 표시 시간이 지나면 컴포넌트를 꺼 OnGUI가 더 불리지 않게 함.
    /// IMGUI는 Screen Space Overlay 캔버스(SystemCanvas 포함)보다 나중에 그려지므로 다른 UI에 가려지지 않음.
    /// </summary>
    internal sealed class StatusMessageOverlay : MonoBehaviour
    {
        /// <summary>메시지를 보여 주는 시간(초).</summary>
        private const float DisplaySeconds = 2f;

        /// <summary>화면 높이 대비 글자 크기 비율. 해상도가 달라도 비슷한 크기로 보이게 함.</summary>
        private const float FontSizeRatio = 0.04f;

        /// <summary>글자 크기의 최소값. 낮은 해상도 창에서도 읽을 수 있게 함.</summary>
        private const int MinFontSize = 16;

        /// <summary>상자의 최대 너비(화면 너비 대비). 메시지의 각 줄이 줄바꿈 없이 한 줄로 보이도록 넉넉히 둠.</summary>
        private const float MaxWidthRatio = 0.9f;

        /// <summary>각 줄을 한 줄에 맞추려고 글자를 줄일 수 있는 하한(기본 글자 크기 대비). 이보다 작아져야 하면 줄을 바꿈.</summary>
        private const float MinFitFontRatio = 0.6f;

        // 상자 크기를 글 크기에 맞추려고 CalcSize·CalcHeight에 넘기는 내용. OnGUI가 매 프레임 불리므로 새로 만들지 않고 재사용함.
        private readonly GUIContent _content = new GUIContent();

        private string _message;
        private GUIStyle _boxStyle;
        private float _hideTime;

        /// <summary>
        /// 새 GameObject에 꺼진 상태의 오버레이를 만들고 씬을 넘어 유지되게 함. 파괴는 만든 쪽이 맡음.
        /// </summary>
        public static StatusMessageOverlay Create(string objectName)
        {
            GameObject go = new GameObject(objectName);
            DontDestroyOnLoad(go);

            StatusMessageOverlay overlay = go.AddComponent<StatusMessageOverlay>();
            overlay.enabled = false;
            return overlay;
        }

        /// <summary>
        /// 메시지를 표시 시간 동안 보여 줌. 이미 보이는 중이면 내용을 바꾸고 시간을 처음부터 다시 셈.
        /// </summary>
        public void Show(string message)
        {
            _message = message;
            _hideTime = Time.unscaledTime + DisplaySeconds;
            enabled = true;
        }

        /// <summary>
        /// 표시 시간이 지나면 컴포넌트를 끔. 일시정지(timeScale 0) 중에도 사라지도록 unscaledTime으로 셈.
        /// </summary>
        private void Update()
        {
            if (Time.unscaledTime >= _hideTime)
            {
                enabled = false;
            }
        }

        /// <summary>
        /// 화면 위쪽 가운데에 반투명 배경 상자와 함께 메시지를 그림. 메시지의 각 줄(줄바꿈으로 나눈 줄)이 한 줄로 보이도록
        /// 상자 너비를 가장 긴 줄에 맞추고(화면 너비의 MaxWidthRatio까지), 그래도 넘치면 글자를 줄임. 하한까지 줄여도 넘치면 줄을 바꿈.
        /// </summary>
        private void OnGUI()
        {
            // GUI.skin은 OnGUI 안에서만 쓸 수 있으므로 처음 그릴 때 스타일을 만들어 재사용함.
            if (_boxStyle == null)
            {
                // 상태 단어에 색을 입히는 <color> 태그를 그리도록 richText를 켬. CalcSize·CalcHeight도 태그를 빼고 잼.
                _boxStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.MiddleCenter,
                    wordWrap = true,
                    richText = true
                };
            }

            int baseFontSize = Mathf.Max(MinFontSize, Mathf.RoundToInt(Screen.height * FontSizeRatio));
            float maxWidth = Screen.width * MaxWidthRatio;
            float horizontalPadding = _boxStyle.padding.horizontal;
            _content.text = _message;

            // CalcSize는 자동 줄 바꿈 없이 가장 긴 줄의 너비(안쪽 여백 포함)를 돌려줌.
            _boxStyle.fontSize = baseFontSize;
            float lineWidth = _boxStyle.CalcSize(_content).x;
            int fontSize = ResolveFitFontSize(baseFontSize, lineWidth - horizontalPadding, maxWidth - horizontalPadding);
            if (fontSize != baseFontSize)
            {
                _boxStyle.fontSize = fontSize;
                lineWidth = _boxStyle.CalcSize(_content).x;
            }

            // 좌우에 글자 크기만큼 여유를 두되 최대 너비를 넘지 않음. 글 높이에 위아래 여백을 더하고, 한 줄이면 예전 높이(글자 2.5배)를 둠.
            float width = Mathf.Min(maxWidth, lineWidth + fontSize);
            float height = Mathf.Max(fontSize * 2.5f, _boxStyle.CalcHeight(_content, width) + fontSize);
            Rect rect = new Rect((Screen.width - width) * 0.5f, Screen.height * 0.1f, width, height);
            GUI.Box(rect, _content, _boxStyle);
        }

        /// <summary>
        /// 가장 긴 줄의 글 너비가 들어갈 수 있는 너비를 넘으면, 넘지 않도록 글자 크기를 비례해 줄여 돌려줌. 들어가면 기본 크기를 그대로 돌려줌.
        /// 너무 작아지지 않도록 기본 크기의 MinFitFontRatio와 MinFontSize 중 큰 값 아래로는 줄이지 않음(그때는 줄을 바꿈).
        /// 글자 크기는 정수라 반올림하면 넘칠 수 있으므로 내림하고 1을 더 뺌.
        /// </summary>
        internal static int ResolveFitFontSize(int baseFontSize, float textWidth, float availableTextWidth)
        {
            if (textWidth <= availableTextWidth || textWidth <= 0f) return baseFontSize;

            int lowerBound = Mathf.Min(baseFontSize, Mathf.Max(MinFontSize, Mathf.RoundToInt(baseFontSize * MinFitFontRatio)));
            int fitted = Mathf.FloorToInt(baseFontSize * availableTextWidth / textWidth) - 1;
            return Mathf.Clamp(fitted, lowerBound, baseFontSize);
        }
    }
}
