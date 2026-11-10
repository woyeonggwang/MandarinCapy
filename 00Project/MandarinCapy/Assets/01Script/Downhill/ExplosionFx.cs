using UnityEngine;

// 폭발 임시 연출: 구가 빠르게 커졌다가 사라짐.
public class ExplosionFx : MonoBehaviour
{
    public float duration = 0.35f;
    public float maxSize = 2.2f;
    float t;

    void Update()
    {
        t += Time.deltaTime;
        float k = Mathf.Clamp01(t / duration);
        float s = Mathf.Lerp(0.2f, maxSize, 1f - (1f - k) * (1f - k));
        if (k > 0.6f) s *= Mathf.Lerp(1f, 0.2f, (k - 0.6f) / 0.4f);
        transform.localScale = Vector3.one * s;
        if (t >= duration) Destroy(gameObject);
    }
}
