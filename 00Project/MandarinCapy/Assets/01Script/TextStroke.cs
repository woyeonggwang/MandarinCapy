using TMPro;
using UnityEngine;

namespace MandarinCapy
{
    /// <summary>
    /// 글자 뒤에 같은 글자를 두껍게(SDF Face Dilate) 한 번 더 그려서 외곽선을 만듦.
    /// TMP 기본 외곽선은 글자마다 면과 외곽선을 같이 그려, 글자 간격이 좁으면 다음 글자의 외곽선이 앞 글자를 덮음.
    /// 외곽선 층을 통째로 먼저 그리면 이 문제가 없음.
    /// 대상 글자의 내용·크기·정렬·투명도·위치를 매 프레임 따라감.
    /// </summary>
    [RequireComponent(typeof(TextMeshProUGUI))]
    public class TextStroke : MonoBehaviour
    {
        public TextMeshProUGUI target;
        public Color strokeColor = Color.white;
        [Range(0f, 1f)] public float thickness = 0.4f;

        TextMeshProUGUI _me;
        RectTransform _rt, _targetRt;

        public static TextStroke Attach(TextMeshProUGUI target, Color color, float thickness)
        {
            var go = new GameObject(target.name + "_Stroke", typeof(RectTransform), typeof(TextMeshProUGUI));
            var rt = (RectTransform)go.transform;
            rt.SetParent(target.transform.parent, false);
            rt.SetSiblingIndex(target.transform.GetSiblingIndex()); // 대상 바로 뒤(먼저 그려짐)
            var s = go.AddComponent<TextStroke>();
            s.target = target;
            s.strokeColor = color;
            s.thickness = thickness;
            s.Init();
            return s;
        }

        void Init()
        {
            _me = GetComponent<TextMeshProUGUI>();
            _rt = (RectTransform)transform;
            _targetRt = target.rectTransform;
            _me.raycastTarget = false;
            _me.font = target.font;
            _me.textWrappingMode = target.textWrappingMode;
            _me.overflowMode = target.overflowMode;
            _me.richText = target.richText;
            // 글자 면을 두껍게 → 외곽선 역할
            _me.fontMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, thickness);
            _me.fontMaterial.SetFloat(ShaderUtilities.ID_OutlineWidth, 0f);
            _me.UpdateMeshPadding();
            Sync();
        }

        void LateUpdate()
        {
            if (target == null) { Destroy(gameObject); return; }
            Sync();
        }

        void Sync()
        {
            if (_me.font != target.font)
            {
                _me.font = target.font;
                _me.fontMaterial.SetFloat(ShaderUtilities.ID_FaceDilate, thickness);
                _me.UpdateMeshPadding();
            }
            if (_me.text != target.text) _me.text = target.text;
            if (!Mathf.Approximately(_me.fontSize, target.fontSize)) _me.fontSize = target.fontSize;
            if (_me.alignment != target.alignment) _me.alignment = target.alignment;
            if (!Mathf.Approximately(_me.characterSpacing, target.characterSpacing)) _me.characterSpacing = target.characterSpacing;
            var c = strokeColor; c.a *= target.color.a;
            if (_me.color != c) _me.color = c;

            _rt.anchorMin = _targetRt.anchorMin;
            _rt.anchorMax = _targetRt.anchorMax;
            _rt.pivot = _targetRt.pivot;
            _rt.sizeDelta = _targetRt.sizeDelta;
            _rt.anchoredPosition = _targetRt.anchoredPosition;
            _rt.localScale = _targetRt.localScale;

            bool on = target.isActiveAndEnabled;
            if (_me.enabled != on) _me.enabled = on;
        }
    }
}
