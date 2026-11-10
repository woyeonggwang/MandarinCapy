using System.Collections.Generic;
using UnityEngine;

// 카피바라 뒤에 달린 과일 수레.
//  - 용량(capacity)까지 과일을 담을 수 있고, 가득 차면 더 들어가지 않음
//  - 업그레이드 아이템을 먹을 때마다 용량 +capacityPerUpgrade, 크기 +growPerUpgrade (계속 커질 수 있음)
//  - 폭탄에 맞으면 마지막에 담은 과일부터 잃음
// body: 수레 그림·벽 콜라이더의 부모. 앞쪽 끝이 원점이라 커질 때 뒤로 늘어남.
public class Cart : MonoBehaviour
{
    public Transform body;
    public int capacity = 50;
    public int capacityPerUpgrade = 50;
    public float growPerUpgrade = 0.15f;

    [Header("내부 공간 (body 로컬, 크기 1 기준)")]
    public Vector3 interiorCenter = new Vector3(0f, 0.3f, -0.35f);
    public Vector3 interiorHalf = new Vector3(0.28f, 0.2f, 0.33f);
    public float visualFruitSize = 0.13f;

    public int Level { get; private set; }

    struct Load { public int points; public Material mat; public string kind; public GameObject visual; }
    readonly List<Load> loads = new List<Load>();
    Transform loadRoot;

    public int Count { get { return loads.Count; } }
    public float Fill { get { return capacity > 0 ? Mathf.Clamp01((float)loads.Count / capacity) : 1f; } }
    public bool IsFull { get { return loads.Count >= capacity; } }
    public int Score
    {
        get { int s = 0; foreach (var l in loads) s += l.points; return s; }
    }

    int initialCapacity;

    void Awake()
    {
        initialCapacity = capacity;
        var t = body.Find("Load");
        if (t == null) { t = new GameObject("Load").transform; t.SetParent(body, false); }
        loadRoot = t;
    }

    // 내부 공간 (겹침 판정용, 월드)
    public void GetInterior(out Vector3 center, out Vector3 halfExtents, out Quaternion rotation, float extraUp)
    {
        Vector3 s = body.lossyScale;
        center = body.TransformPoint(interiorCenter + new Vector3(0f, extraUp * 0.5f, 0f));
        halfExtents = Vector3.Scale(interiorHalf + new Vector3(0f, extraUp * 0.5f, 0f), s);
        rotation = body.rotation;
    }

    public bool TryCollect(Obstacle o)
    {
        if (IsFull || o.IsHeld) return false;
        o.IsHeld = true; // 같은 프레임에 FixedUpdate가 두 번 돌 때 중복 수집 방지 (Destroy는 프레임 끝에 처리됨)
        Material m = null;
        var r = o.GetComponent<Renderer>();
        if (r != null) m = r.sharedMaterial;
        var l = new Load { points = o.points, mat = m, kind = o.kind };
        l.visual = MakeVisual(m, o.visualPrefab);
        loads.Add(l);
        Destroy(o.gameObject);
        Relayout();
        return true;
    }

    // 마지막에 담은 과일부터 n개 꺼냄 (폭발). 꺼낸 과일의 (점수, 재질, 종류) 반환
    public List<KeyValuePair<int, Material>> RemoveFruits(int n, List<string> kinds)
    {
        var removed = new List<KeyValuePair<int, Material>>();
        for (int i = 0; i < n && loads.Count > 0; i++)
        {
            var l = loads[loads.Count - 1];
            loads.RemoveAt(loads.Count - 1);
            if (l.visual != null) Destroy(l.visual);
            removed.Add(new KeyValuePair<int, Material>(l.points, l.mat));
            if (kinds != null) kinds.Add(l.kind);
        }
        Relayout();
        return removed;
    }

    public void Upgrade()
    {
        Level++;
        capacity += capacityPerUpgrade;
        body.localScale = Vector3.one * (1f + growPerUpgrade * Level);
        Relayout();
    }

    // 처음 상태로 (빈 수레, 처음 용량·크기)
    public void ResetCart()
    {
        foreach (var l in loads) if (l.visual != null) Destroy(l.visual);
        loads.Clear();
        Level = 0;
        capacity = initialCapacity;
        body.localScale = Vector3.one;
    }


    // 담긴 과일 그림: 모델이 있으면 모델(크기 1 = 지름 1이 되도록 맞춤), 없으면 구
    GameObject MakeVisual(Material m, GameObject prefab)
    {
        GameObject g;
        if (prefab != null)
        {
            g = new GameObject("LoadFruit");
            g.transform.SetParent(loadRoot, false);
            var model = (GameObject)Instantiate(prefab, g.transform, false);
            var rs = model.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                // 부모 g의 로컬 기준으로 바운드 계산 후 지름 1로 정규화
                Bounds b = new Bounds(g.transform.InverseTransformPoint(rs[0].bounds.center), Vector3.zero);
                foreach (var r in rs)
                {
                    b.Encapsulate(g.transform.InverseTransformPoint(r.bounds.min));
                    b.Encapsulate(g.transform.InverseTransformPoint(r.bounds.max));
                }
                float d = Mathf.Max(0.001f, Mathf.Max(b.size.x, b.size.z));
                model.transform.localScale = model.transform.localScale / d;
                model.transform.localPosition = -b.center / d;
            }
        }
        else
        {
            g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(g.GetComponent<Collider>());
            g.name = "LoadFruit";
            g.transform.SetParent(loadRoot, false);
            if (m != null) g.GetComponent<Renderer>().sharedMaterial = m;
        }
        g.transform.localScale = Vector3.one * visualFruitSize;
        return g;
    }

    // 담긴 과일을 바닥부터 격자로 쌓아 보이게 함 (크기 1 기준 body 로컬)
    // 담긴 과일을 바닥부터 격자로 쌓아 보이게 함 (body 로컬).
    //  - 수레가 커져도 과일 그림은 같은 크기로 유지 (body 크기로 나눠 줌) → 커진 수레에 더 많이 놓임
    //  - 앞쪽(카피바라 쪽)부터 채움: 뒤쪽은 카메라 아래쪽으로 잘려 보일 수 있음
    void Relayout()
    {
        float sc = Mathf.Max(0.01f, body.localScale.x);
        float size = visualFruitSize / sc;
        float step = size * 1.05f;
        int nx = Mathf.Max(1, Mathf.FloorToInt(interiorHalf.x * 2f / step));
        int nz = Mathf.Max(1, Mathf.FloorToInt(interiorHalf.z * 2f / step));
        float x0 = interiorCenter.x - (nx - 1) * step * 0.5f;
        float y0 = interiorCenter.y - interiorHalf.y + step * 0.5f;
        float zFront = interiorCenter.z + interiorHalf.z - step * 0.5f;
        for (int i = 0; i < loads.Count; i++)
        {
            var v = loads[i].visual;
            if (v == null) continue;
            int layer = i / (nx * nz);
            int k = i % (nx * nz);
            int ix = k % nx, iz = k / nx;
            v.transform.localScale = Vector3.one * size;
            v.transform.localPosition = new Vector3(x0 + ix * step, y0 + layer * step * 0.8f, zFront - iz * step);
        }
    }
}
