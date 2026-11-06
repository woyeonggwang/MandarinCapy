using UnityEngine;

namespace MandarinCapy
{
    /// <summary>
    /// 프로토타입용 임시 도형 스프라이트를 코드로 생성합니다. (에셋 의존 없음)
    /// Square: 1x1 유닛, Circle: 지름 1 유닛
    /// </summary>
    public static class SpriteFactory
    {
        static Sprite _square;
        static Sprite _circle;

        public static Sprite Square
        {
            get
            {
                if (_square == null)
                {
                    var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                    var px = new Color32[16];
                    for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
                    tex.SetPixels32(px);
                    tex.filterMode = FilterMode.Point;
                    tex.Apply();
                    _square = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
                    _square.name = "TempSquare";
                }
                return _square;
            }
        }

        public static Sprite Circle
        {
            get
            {
                if (_circle == null)
                {
                    const int size = 128;
                    var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                    var px = new Color32[size * size];
                    float r = size * 0.5f;
                    for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dx = x + 0.5f - r, dy = y + 0.5f - r;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        byte a = (byte)(Mathf.Clamp01(r - d) * 255f); // 가장자리 1px 안티앨리어싱
                        px[y * size + x] = new Color32(255, 255, 255, a);
                    }
                    tex.SetPixels32(px);
                    tex.filterMode = FilterMode.Bilinear;
                    tex.Apply();
                    _circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
                    _circle.name = "TempCircle";
                }
                return _circle;
            }
        }

        public static SpriteRenderer Create(string name, Sprite sprite, Color color, Vector2 size, int order, Transform parent = null)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localScale = new Vector3(size.x, size.y, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }
    }
}
