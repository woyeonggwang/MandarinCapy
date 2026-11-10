using UnityEngine;

// 로봇팔이 집는 위치를 팔 끝(TCP) 바로 아래에 반투명 하늘색 구(절반 파묻힘)로 표시. (아래 줄의 '원'은 예전 설명)
//  - 지름 = 2 × grabRadius (팔이 집는 판정 구의 크기)
//  - 아래쪽 첫 표면(바닥·물체 윗면·수레)에 붙여 그림. 카피바라 몸과 들고 있는 물체는 무시
//  - 집을 수 있는 물체가 원 안에 있으면 진하게, 들고 있을 때는 내려놓을 자리를 보여줌
public class GrabIndicator : MonoBehaviour
{
    public ArmController arm;
    public Transform allowUnder;          // ignoreRoot 아래라도 이 물체(수레)는 표면으로 인정
    public Material material;
    public Color color = new Color(0.45f, 0.82f, 1f, 1f);
    public float idleAlpha = 0.4f;
    public float targetAlpha = 0.75f;     // 집을 수 있는 물체가 있을 때
    public float sizeScale = 0.5f;        // 지름 = 2 × grabRadius × sizeScale (예전 원판의 절반, 사용자 요청 2026-11-09)

    Transform disc;   // 반투명 구 (중심이 표면 접점 → 절반 파묻힌 반구처럼 보임, 표면 기울기에 따라 비틀리지 않음)
    Renderer rend;
    MaterialPropertyBlock mpb;
    float alpha;
    readonly Collider[] buf = new Collider[16];

    void Start()
    {
        if (disc != null) return;
        var q = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(q.GetComponent<Collider>());
        q.name = "GrabBall";
        disc = q.transform;
        disc.SetParent(transform, false);
        rend = q.GetComponent<Renderer>();
        rend.sharedMaterial = material;
        rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        rend.receiveShadows = false;
        mpb = new MaterialPropertyBlock();
        alpha = idleAlpha;
    }

    void LateUpdate()
    {
        if (arm == null || disc == null) return;
        bool show = arm.inputEnabled && arm.tcp != null;
        if (rend.enabled != show) rend.enabled = show;
        if (!show) return;

        Vector3 from = arm.Held != null ? arm.Held.transform.position : arm.tcp.position;
        RaycastHit best;
        if (!CastDown(from, out best)) { rend.enabled = false; return; }

        float d = arm.grabRadius * 2f * sizeScale;
        disc.position = best.point;                // 중심이 표면 → 절반 파묻힘
        disc.rotation = Quaternion.identity;
        disc.localScale = Vector3.one * d / Mathf.Max(0.0001f, transform.lossyScale.x);

        bool target = arm.Held == null && HasGrabbable();
        alpha = Mathf.MoveTowards(alpha, target ? targetAlpha : idleAlpha, Time.deltaTime * 4f);
        var c = color; c.a = alpha;
        rend.GetPropertyBlock(mpb);
        mpb.SetColor("_BaseColor", c);
        rend.SetPropertyBlock(mpb);
    }

    bool CastDown(Vector3 from, out RaycastHit best)
    {
        best = default(RaycastHit);
        float bestD = float.MaxValue;
        bool found = false;
        var hits = Physics.RaycastAll(from + Vector3.up * 0.05f, Vector3.down, 6f, ~0, QueryTriggerInteraction.Ignore);
        foreach (var h in hits)
        {
            var t = h.collider.transform;
            if (arm.ignoreRoot != null && t.IsChildOf(arm.ignoreRoot) && !(allowUnder != null && t.IsChildOf(allowUnder))) continue;
            if (arm.Held != null && t.IsChildOf(arm.Held.transform)) continue;
            if (h.distance < bestD) { bestD = h.distance; best = h; found = true; }
        }
        return found;
    }

    bool HasGrabbable()
    {
        int n = Physics.OverlapSphereNonAlloc(arm.tcp.position, arm.grabRadius, buf, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var o = buf[i].GetComponentInParent<Obstacle>();
            if (o != null && !o.IsHeld) return true;
        }
        return false;
    }
}
