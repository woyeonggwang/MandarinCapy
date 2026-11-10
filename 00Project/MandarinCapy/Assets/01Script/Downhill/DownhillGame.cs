using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// 하산 과일 수집 게임 관리:
//  - 60초 제한, 점수 = 수레에 담긴 과일 점수 합 (폭탄으로 잃으면 줄어듦)
//  - 걷기 속도 = baseSpeed × (1 - fullSlowdown × 수레 적재율)  → 가득 찰수록 느려짐
//  - 길 위 배치(고정 시드): 장애물 / 폭탄 / 업그레이드(길 가운데) + 과일(길 양옆, 팔로 집어 수레에 담음)
//  - 접촉 판정(FixedUpdate 겹침 검사): 폭탄이 카피바라·수레에 닿으면 폭발, 업그레이드가 카피바라에 닿으면 수레 업그레이드,
//    집고 있지 않은 과일이 수레 안에 들어오면 수집
public class DownhillGame : MonoBehaviour, MandarinCapy.IRoundGame
{
    [Header("연결")]
    public CapybaraWalker walker;
    public ArmController arm;
    public Cart cart;
    public Transform slope;

    [Header("규칙")]
    public float duration = 90f;
    public float baseSpeed = 1.2f;
    public float timeBonus = 10f;        // 시간 아이템 하나당 추가 시간(초)
    public float timeGapBase = 15f;      // 시간 아이템을 n개 먹으면 앞으로 timeGapBase×(n+1) m 안의 시간 아이템을 없앰 (먹을수록 드물게)
    public float timeGapKeepNear = 2f;   // 이미 눈앞(이 거리 안)에 있는 것은 갑자기 사라지지 않게 둠
    public float fullSlowdown = 0.45f;   // 가득 찼을 때 속도 감소 비율
    public int bombFruitLoss = 2;
    public float cartCatchHeight = 0.35f; // 수레 내부 위로 이만큼까지도 담기는 것으로 판정
    public float bombStun = 0.8f;        // 폭발 후 카피바라가 멈칫하는 시간
    public float bombRadius = 2.0f;
    public float bombForce = 6f;

    [Header("배치")]
    public int seed = 7;
    public float spawnStart = 6f;
    public float spawnEnd = 180f;
    public float spawnStep = 4f;
    public float startMargin = 2f;       // 경사길 위쪽 끝에서 출발 지점까지
    public int upgradeEvery = 5;         // 업그레이드가 최소 이 칸수마다 한 번은 나옴
    public int bombEvery = 6;            // 폭탄이 최소 이 칸수마다 한 번은 나옴
    public int timeEvery = 8;            // 시간 아이템이 최소 이 칸수마다 한 번은 나옴
    public int blockerEvery = 3;         // 장애물이 최소 이 칸수마다 한 번은 나옴 (초반부터 길을 막게)
    public float blockerScale = 1.6f;    // 장애물 크기 배율 (카피바라 머리에 가려지지 않게 키움)
    public int fruitsMin = 3;            // 칸마다 길 양옆 과일 개수 (최소~최대)
    public int fruitsMax = 6;
    public float treeStep = 5f;          // 길 양옆 나무 간격
    public float goldenChance = 0.4f;    // 나무에 황금귤이 열릴 확률
    public float goldenHeight = 0.7f;    // 황금귤이 매달린 높이 (팔이 닿는 높이)
    public float goldenX = 0.75f;        // 황금귤 길 중심에서의 거리

    [Header("재질")]
    public Material matRock, matCrate, matLog, matTangerine, matApple, matGolden, matBomb, matBombFuse, matUpgrade, matExplosion, matTime, matLeaves;
    public PhysicsMaterial friction;
    public Material matTrunk, matCanopy;  // 황금귤 가지·잎 (나무 모델과 같은 색)

    [Header("모델 (Blender 카툰 소품, 없으면 기본 도형)")]
    public GameObject mdlTangerine, mdlGolden, mdlApple, mdlBomb, mdlBasket, mdlClock;
    public GameObject mdlRock, mdlCrate, mdlLog, mdlHay, mdlPumpkin, mdlTree;

    [Header("기타")]
    public bool debugHud = false;        // 임시 OnGUI 표시 (개발용)

    public float TimeLeft { get; private set; }
    public bool Running { get; private set; }

    // ---- IRoundGame ----
    public event System.Action<string, bool> Judged;
    public event System.Action GameEnded;
    public int Count { get { return cart != null ? cart.Count : 0; } }
    public int FruitsCollected { get; private set; }
    public string HudSub
    {
        get
        {
            int t = Mathf.CeilToInt(TimeLeft);
            string time = t <= 10 ? "<color=#D62E1F>" + t + "초</color>" : t + "초";
            return "남은 시간 " + time + "    수레 " + Count + "/" + (cart != null ? cart.capacity : 0);
        }
    }
    public string ResultTitle { get { return "수확 끝!"; } }
    public string GameOverReason { get { return "시간이 다 됐습니다"; } }
    public string ResultSub
    {
        get { return "수레에 남은 과일 " + Count + "개  ·  폭탄 " + BombsHit + "번  ·  업그레이드 " + Upgrades + "번"; }
    }
    public bool Ended { get; private set; }
    public int Score { get { return cart != null ? cart.Score : 0; } }
    public string LastEvent { get; private set; } = "";
    public int BombsHit { get; private set; }
    public int Upgrades { get; private set; }
    public int TimeItems { get; private set; }

    float stunUntil;
    int blockerSeq;
    float eventUntil;
    readonly List<Obstacle> spawned = new List<Obstacle>();
    readonly Collider[] buf = new Collider[64];
    readonly Collider[] bufExp = new Collider[64]; // 폭발용 (접촉 판정 반복 중에 buf를 덮어쓰지 않도록 분리)

    // 경사길 위의 점: dist = 출발점에서 내리막 방향 거리, x = 오른쪽 오프셋
    public Vector3 OnPath(float dist, float x)
    {
        float half = slope.localScale.z * 0.5f;
        return slope.position + slope.up * (slope.localScale.y * 0.5f) - slope.forward * (half - startMargin) + slope.forward * dist + slope.right * x;
    }

    void Start()
    {
        ResetRound();
        // 화면 흐름(GameFlow)이 없는 씬이면 바로 시작 (테스트용)
        if (FindFirstObjectByType<MandarinCapy.GameFlow>() == null) BeginRound();
    }

    // 처음 상태로: 배치 다시 생성, 수레 비우기, 카피바라 원위치
    public void ResetRound()
    {
        Running = false;
        Ended = false;
        TimeLeft = duration;
        BombsHit = 0; Upgrades = 0; TimeItems = 0; FruitsCollected = 0;
        LastEvent = "";
        stunUntil = 0f;
        if (GameSound.I != null) GameSound.I.ResetCombo();

        if (arm != null)
        {
            if (arm.Held != null) arm.Release();
            arm.inputEnabled = false;
        }
        foreach (var o in FindObjectsByType<Obstacle>(FindObjectsSortMode.None)) { o.gameObject.SetActive(false); Destroy(o.gameObject); }
        var old = GameObject.Find("Spawned");
        if (old != null) { old.name = "Spawned_old"; old.SetActive(false); Destroy(old); }
        spawned.Clear();
        if (cart != null) cart.ResetCart();

        walker.walking = false;
        walker.ResetWalk(OnPath(0f, 0f));
        walker.transform.rotation = Quaternion.LookRotation(slope.forward, slope.up);
        if (arm != null) arm.SolveIK(walker.transform.TransformPoint(new Vector3(0f, 1.3f, 0.6f)));
        var cam = Camera.main != null ? Camera.main.GetComponent<DownhillCamera>() : null;
        if (cam != null) cam.SnapNow();

        Spawn();
    }

    public void BeginRound()
    {
        Running = true;
        Ended = false;
        TimeLeft = duration;
        if (arm != null) arm.inputEnabled = true;
    }

    void Update()
    {
        if (Running && !Ended)
        {
            TimeLeft -= Time.deltaTime;
            if (TimeLeft <= 0f) End();
        }
        if (walker != null)
        {
            walker.speed = baseSpeed * (1f - fullSlowdown * (cart != null ? cart.Fill : 0f));
            walker.walking = Running && !Ended && Time.time >= stunUntil;
        }
        if (Time.time > eventUntil) LastEvent = "";
    }



    void End()
    {
        Ended = true;
        Running = false;
        TimeLeft = 0f;
        if (arm != null)
        {
            if (arm.Held != null) arm.Release();
            arm.inputEnabled = false;
        }
        Event("도착! 최종 점수 " + Score, 999f, true);
        if (GameEnded != null) GameEnded();
    }

    void Event(string s, float time, bool strong = false)
    {
        LastEvent = s;
        eventUntil = Time.time + time;
        if (Judged != null) Judged(s, strong);
    }

    // ---------- 접촉 판정 ----------

    void FixedUpdate()
    {
        if (walker == null || cart == null) return;
        Transform capy = walker.transform;

        // 폭탄은 수레에 닿을 때만 터짐 (카피바라 몸은 그냥 지나감, 사용자 결정 2026-11-09)
        int n;

        // 수레 (내부 + 위쪽 여유)
        Vector3 c, h; Quaternion r;
        cart.GetInterior(out c, out h, out r, cartCatchHeight);
        n = Physics.OverlapBoxNonAlloc(c, h, buf, r, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var o = buf[i].GetComponentInParent<Obstacle>();
            if (o == null || o.IsHeld) continue;
            if (o.type == ItemType.Bomb) { Explode(o); continue; }  // 폭탄은 길에 놓인 채로도 수레에 닿으면 터짐
            // 과일·아이템은 플레이어가 집거나 던지거나 쳐 낸 것만 수집 (길가에 쉬고 있는 것을 수레가 지나가며 줍지 않게)
            if (o.Body != null && o.Body.isKinematic) continue;
            if (o.type == ItemType.Upgrade) EatUpgrade(o);
            else if (o.type == ItemType.TimeBonus) EatTime(o);
            else if (o.type == ItemType.Fruit && !Ended && Time.time >= o.noCollectUntil)
            {
                if (cart.TryCollect(o))
                {
                    Event("+" + o.points + " " + FruitName(o.kind), 1.2f);
                    if (GameSound.I != null) GameSound.I.Fruit();
                }
                else
                {
                    // 가득 참: 밖으로 튕겨 냄
                    o.Wake();
                    o.Body.linearVelocity = (capy.right * (Random.value < 0.5f ? -1f : 1f) + Vector3.up) * 2.5f;
                    Event("수레가 가득 찼습니다!", 1.2f);
                }
            }
        }
    }

    void EatUpgrade(Obstacle o)
    {
        if (o == null) return;
        Destroy(o.gameObject);
        o.type = ItemType.Blocker; // 같은 프레임 중복 처리 방지
        o.IsHeld = true;
        cart.Upgrade();
        Upgrades++;
        if (GameSound.I != null) GameSound.I.Item();
        Event("수레 업그레이드! 용량 " + cart.capacity, 1.5f);
    }

    void EatTime(Obstacle o)
    {
        if (o == null || Ended) return;
        Destroy(o.gameObject);
        o.type = ItemType.Blocker; // 같은 프레임 중복 처리 방지
        o.IsHeld = true;
        TimeLeft += timeBonus;
        TimeItems++;
        ThinTimeItems();
        if (GameSound.I != null) GameSound.I.Item();
        Event("+" + Mathf.RoundToInt(timeBonus) + "초!", 1.5f);
    }

    // 시간 아이템을 먹을수록 다음 시간 아이템까지의 간격을 늘림 (4개 이상은 먹기 힘들게)
    void ThinTimeItems()
    {
        if (walker == null) return;
        Vector3 origin = OnPath(0f, 0f);
        float here = Vector3.Dot(walker.transform.position - origin, slope.forward);
        float until = here + timeGapBase * (TimeItems + 1);
        foreach (var o in spawned.ToArray())
        {
            if (o == null || o.type != ItemType.TimeBonus || o.IsHeld) continue;
            float d = Vector3.Dot(o.transform.position - origin, slope.forward);
            if (d - here <= timeGapKeepNear || d >= until) continue;
            spawned.Remove(o);
            o.gameObject.SetActive(false);
            Destroy(o.gameObject);
        }
    }


    public void Explode(Obstacle bomb)
    {
        if (bomb == null || bomb.IsHeld) return;
        Vector3 pos = bomb.transform.position;
        bomb.IsHeld = true; // 중복 폭발 방지
        Destroy(bomb.gameObject);
        BombsHit++;
        if (GameSound.I != null) GameSound.I.Boom();

        // 연출
        var fx = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(fx.GetComponent<Collider>());
        fx.name = "Explosion";
        fx.transform.position = pos;
        if (matExplosion != null) fx.GetComponent<Renderer>().sharedMaterial = matExplosion;
        fx.AddComponent<ExplosionFx>();

        // 주변 물체 날림
        int n = Physics.OverlapSphereNonAlloc(pos, bombRadius, bufExp, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < n; i++)
        {
            var o = bufExp[i].GetComponentInParent<Obstacle>();
            if (o == null || o.IsHeld || o.Body == null) continue;
            o.Wake();
            o.Body.AddExplosionForce(bombForce, pos, bombRadius, 0.6f, ForceMode.VelocityChange);
        }

        // 수레 과일 잃음 → 밖으로 튀어 나감
        var kinds = new List<string>();
        var lost = cart.RemoveFruits(bombFruitLoss, kinds);
        Vector3 c, h; Quaternion r;
        cart.GetInterior(out c, out h, out r, 0f);
        for (int i = 0; i < lost.Count; i++)
        {
            var f = MakeFruit(kinds[i], c + Vector3.up * (h.y + 0.45f) + walker.transform.right * ((i - 1) * 0.25f), false);
            f.noCollectUntil = Time.time + 1.0f;
            f.Body.linearVelocity = (Random.insideUnitSphere * 0.8f + Vector3.up * 1.2f - walker.transform.forward * 0.6f).normalized * 4f;
        }
        stunUntil = Time.time + bombStun;
        Event(lost.Count > 0 ? "펑! 과일 " + lost.Count + "개를 잃었습니다" : "펑!", 1.5f);
    }

    // ---------- 배치 ----------

    void Spawn()
    {
        var root = new GameObject("Spawned").transform;
        blockerSeq = 0;
        var rnd = new System.Random(seed);
        int sinceUpgrade = 0, sinceBomb = 0, sinceTime = 0, sinceBlocker = 1;
        for (float d = spawnStart; d <= spawnEnd; d += spawnStep)
        {
            double r = rnd.NextDouble();
            float cx = (float)(rnd.NextDouble() * 0.6 - 0.3);
            sinceUpgrade++; sinceBomb++; sinceTime++; sinceBlocker++;
            if (sinceBlocker >= blockerEvery) r = 0.1;           // 최소 blockerEvery칸에 한 번은 장애물 (초반 10m부터)
            else if (sinceUpgrade >= upgradeEvery) r = 0.4;          // 최소 upgradeEvery칸에 한 번은 업그레이드
            else if (sinceBomb >= bombEvery) r = 0.3;           // 최소 bombEvery칸에 한 번은 폭탄
            else if (sinceTime >= timeEvery) r = 0.55;          // 최소 timeEvery칸에 한 번은 시간 아이템
            if (r < 0.25) { MakeBlocker(rnd, d, cx, root); sinceBlocker = 0; }
            else if (r < 0.35) { MakeBomb(OnPath(d, cx), root); sinceBomb = 0; }
            else if (r < 0.50) { MakeUpgrade(OnPath(d + 1.5f, SideX(rnd)), root); sinceUpgrade = 0; }
            else if (r < 0.58) { MakeTimeItem(OnPath(d + 1.5f, SideX(rnd)), root); sinceTime = 0; }

            int fruits = fruitsMin + rnd.Next(fruitsMax - fruitsMin + 1);
            for (int i = 0; i < fruits; i++)
            {
                float side = rnd.NextDouble() < 0.5 ? -1f : 1f;
                float x = side * (0.45f + (float)rnd.NextDouble() * 0.4f);
                float dd = d + (float)(rnd.NextDouble() * 2.0 - 1.0);
                double k = rnd.NextDouble();
                string kind = k < 0.7 ? "tangerine" : "apple";   // 황금귤은 나무에서만 열림
                var f = MakeFruit(kind, OnPath(dd, x), true);
                f.transform.SetParent(root, true);
            }
        }
        SpawnTrees(new System.Random(seed + 101), root);
        ClearOverlaps();
    }

    // 장애물과 겹쳐 생긴 아이템(과일·폭탄 등) 제거: 겹친 채 시작하면 물리 엔진이 장애물을 밀어내 뒤집힘
    void ClearOverlaps()
    {
        Physics.SyncTransforms();
        foreach (var b in spawned.ToArray())
        {
            if (b == null || b.type != ItemType.Blocker) continue;
            var bc = b.GetComponent<BoxCollider>();
            if (bc == null) continue;
            int n = Physics.OverlapBoxNonAlloc(b.transform.TransformPoint(bc.center), bc.size * 0.5f + Vector3.one * 0.05f, buf, b.transform.rotation, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                var o = buf[i].GetComponentInParent<Obstacle>();
                if (o == null || o == b || o.type == ItemType.Blocker) continue;
                spawned.Remove(o);
                o.gameObject.SetActive(false);
                Destroy(o.gameObject);
            }
        }
    }


    // 길 양옆 나무 (장식 + 일부에 황금귤). 줄기만 콜라이더, 잎은 콜라이더 없음(팔·마우스 레이를 막지 않게).
    // 길 양옆 나무 (장식 + 일부에 황금귤). 줄기만 콜라이더, 잎은 콜라이더 없음(팔·마우스 레이를 막지 않게).
    void SpawnTrees(System.Random rnd, Transform root)
    {
        var treeRoot = new GameObject("Trees").transform;
        treeRoot.SetParent(root, false);
        for (float d = 3f; d <= spawnEnd + 15f; d += treeStep)
        {
            for (int s = -1; s <= 1; s += 2)
            {
                float dd = d + (float)(rnd.NextDouble() * 2.0 - 1.0);
                float x = s * (1.45f + (float)rnd.NextDouble() * 0.35f);
                float hScale = 0.9f + (float)rnd.NextDouble() * 0.3f;
                Vector3 basePos = OnPath(dd, x);
                var tree = new GameObject("Tree").transform;
                tree.SetParent(treeRoot, false);
                tree.position = basePos;
                tree.rotation = Quaternion.Euler(0f, (float)rnd.NextDouble() * 360f, 0f);

                if (mdlTree != null)
                {
                    var m = (GameObject)Instantiate(mdlTree, tree, false);
                    m.name = "Model";
                    tree.localScale = Vector3.one * (0.85f * hScale);
                    var cap = tree.gameObject.AddComponent<CapsuleCollider>();
                    cap.center = new Vector3(0f, 0.6f, 0f); cap.radius = 0.1f; cap.height = 1.2f;
                }
                else
                {
                    var trunk = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    trunk.name = "Trunk";
                    trunk.transform.SetParent(tree, false);
                    trunk.transform.localScale = new Vector3(0.16f, 0.6f * hScale, 0.16f);
                    trunk.transform.localPosition = Vector3.up * 0.6f * hScale;
                    trunk.GetComponent<Renderer>().sharedMaterial = matLog;
                    float topY = 1.2f * hScale;
                    Leaf(tree, new Vector3(0f, topY + 0.15f, 0f), 1.0f);
                    Leaf(tree, new Vector3(0.3f, topY + 0.2f, 0.15f), 0.7f);
                }
                tree.gameObject.isStatic = true;

                if (rnd.NextDouble() < goldenChance)
                {
                    Vector3 hang = OnPath(dd, s * goldenX) + Vector3.up * goldenHeight;
                    var g = MakeFruit("golden", hang, true);
                    g.transform.position = hang; // MakeFruit의 바닥 오프셋 대신 공중에 매달림
                    g.transform.rotation = Quaternion.identity;
                    g.transform.SetParent(root, true);
                    Material wood = matTrunk != null ? matTrunk : matLog;
                    // 꼭지: 과일 위에서 잎 쪽으로 길이 0.35
                    var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Object.Destroy(stem.GetComponent<Collider>());
                    stem.name = "Stem";
                    stem.transform.SetParent(g.transform, false);
                    stem.transform.localPosition = new Vector3(0f, 0.275f, 0f);
                    stem.transform.localScale = new Vector3(0.026f, 0.175f, 0.026f);
                    stem.GetComponent<Renderer>().sharedMaterial = wood;

                    // 꼭지 위 작은 잎 + 줄기에서 그까지 이어진 가지
                    Vector3 leafPos = hang + Vector3.up * 0.45f;
                    var leaf = Leaf(tree, tree.InverseTransformPoint(leafPos), 0.45f);
                    leaf.transform.localScale = leaf.transform.localScale / tree.localScale.x;
                    Vector3 from = tree.position + Vector3.up * (0.95f * hScale);
                    Vector3 dir = leafPos - from;
                    var branch = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    Object.Destroy(branch.GetComponent<Collider>());
                    branch.name = "Branch";
                    branch.transform.SetParent(root, false);
                    branch.transform.position = from + dir * 0.5f;
                    branch.transform.rotation = Quaternion.FromToRotation(Vector3.up, dir.normalized);
                    branch.transform.localScale = new Vector3(0.1f, dir.magnitude * 0.5f, 0.1f);   // 멀리서도 이어져 보이게 굵게
                    branch.GetComponent<Renderer>().sharedMaterial = wood;
                }
            }
        }
    }

    GameObject Leaf(Transform tree, Vector3 lp, float size)
    {
        var l = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(l.GetComponent<Collider>());
        l.name = "Leaves";
        l.transform.SetParent(tree, false);
        l.transform.localPosition = lp;
        l.transform.localScale = Vector3.one * size * 0.8f;
        l.GetComponent<Renderer>().sharedMaterial = matCanopy != null ? matCanopy : matLeaves;
        return l;
    }

    // 모델로 물체 만들기: 빈 부모(콜라이더·물리 담당) 아래에 모델을 자식으로 붙임.
    //  center=true면 모델 바운드 중심이 부모 원점에 오게 옮김(상자형), false면 모델 원점 그대로(구형: 몸통 중심이 원점).
    //  size: 구형은 지름(x), 상자형은 박스 콜라이더 크기. 모델이 없으면 기본 도형을 이 크기로 만듦.
    GameObject Build(GameObject prefab, bool sphere, Vector3 size, bool center, Material fallbackMat)
    {
        if (prefab == null)
        {
            var p = GameObject.CreatePrimitive(sphere ? PrimitiveType.Sphere : PrimitiveType.Cube);
            p.transform.localScale = size;
            if (fallbackMat != null) p.GetComponent<Renderer>().sharedMaterial = fallbackMat;
            return p;
        }
        var g = new GameObject();
        var m = (GameObject)Instantiate(prefab, g.transform, false);
        m.name = "Model";
        if (center)
        {
            var rs = m.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                Bounds b = rs[0].bounds;
                foreach (var r in rs) b.Encapsulate(r.bounds);
                m.transform.localPosition -= b.center; // g는 원점·회전 없음 → 월드 = 로컬
            }
        }
        if (sphere) g.AddComponent<SphereCollider>().radius = size.x * 0.5f;
        else g.AddComponent<BoxCollider>().size = size;
        return g;
    }

    // 길 양옆(팔이 닿는 거리) x 오프셋
    static float SideX(System.Random rnd)
    {
        float side = rnd.NextDouble() < 0.5 ? -1f : 1f;
        return side * (0.45f + (float)rnd.NextDouble() * 0.4f);
    }

    Obstacle Setup(GameObject g, string kind, ItemType type, float mass, bool resting, Transform root)
    {
        g.name = type + "_" + kind;
        if (root != null) g.transform.SetParent(root, true);
        var col = g.GetComponent<Collider>();
        if (col != null && friction != null) col.sharedMaterial = friction;
        var rb = g.AddComponent<Rigidbody>();
        rb.mass = mass;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.angularDamping = 1.5f;
        if (resting) rb.isKinematic = true;
        var o = g.AddComponent<Obstacle>();
        o.kind = kind;
        o.type = type;
        spawned.Add(o);
        return o;
    }

    void MakeBlocker(System.Random rnd, float d, float x, Transform root)
    {
        rnd.Next(5);                 // 난수 순서 유지용 (배치 위치가 바뀌지 않게)
        int t = blockerSeq++ % 5;    // 5종이 모두 보이도록 돌아가며 배치
        string[] kinds = { "rock", "crate", "log", "hay", "pumpkin" };
        GameObject[] models = { mdlRock, mdlCrate, mdlLog, mdlHay, mdlPumpkin };
        Vector3[] sizes = { new Vector3(0.46f, 0.3f, 0.42f), new Vector3(0.43f, 0.425f, 0.43f), new Vector3(0.28f, 0.28f, 0.9f),
                            new Vector3(0.6f, 0.42f, 0.46f), new Vector3(0.5f, 0.4f, 0.5f) };
        float[] masses = { 6f, 2f, 3f, 2.5f, 4f };
        Material[] fb = { matRock, matCrate, matLog, matCrate, matTangerine };
        var g = Build(models[t], false, sizes[t], true, fb[t]);
        // 길 방향으로 놓음 (통나무는 길이가 길 방향) + 살짝 비틀어 자연스럽게
        float yaw = t == 2 ? 0f : Mathf.Sin(d * 12.9898f) * 20f; // rnd를 쓰지 않아 기존 배치 순서 유지
        g.transform.rotation = Quaternion.LookRotation(slope.forward, slope.up) * Quaternion.Euler(0f, yaw, 0f);
        g.transform.localScale = g.transform.localScale * blockerScale;   // 모델·콜라이더 함께 확대
        g.transform.position = OnPath(d, x) + slope.up * (sizes[t].y * 0.5f * blockerScale + 0.01f);
        Setup(g, kinds[t], ItemType.Blocker, masses[t], false, root);
    }

    void MakeBomb(Vector3 p, Transform root)
    {
        var g = Build(mdlBomb, true, Vector3.one * 0.3f, false, matBomb);
        g.transform.position = p + slope.up * 0.16f;
        g.transform.rotation = Quaternion.LookRotation(slope.forward, slope.up);
        if (mdlBomb == null)
        {
            var fuse = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(fuse.GetComponent<Collider>());
            fuse.transform.SetParent(g.transform, false);
            fuse.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            fuse.transform.localScale = new Vector3(0.15f, 0.12f, 0.15f);
            if (matBombFuse != null) fuse.GetComponent<Renderer>().sharedMaterial = matBombFuse;
        }
        var o = Setup(g, "bomb", ItemType.Bomb, 1f, true, root);
        o.points = 0;
        o.Body.angularDamping = 4f;   // 치운 폭탄이 경사를 굴러 길로 되돌아오지 않게
        o.Body.linearDamping = 0.4f;
    }

    void MakeUpgrade(Vector3 p, Transform root)
    {
        var g = Build(mdlBasket, false, new Vector3(0.33f, 0.31f, 0.33f), true, matUpgrade);
        g.transform.position = p + slope.up * 0.2f;
        g.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
        var o = Setup(g, "upgrade", ItemType.Upgrade, 0.5f, true, root);
        o.spin = true;
    }

    void MakeTimeItem(Vector3 p, Transform root)
    {
        // 자명종 시계 (모델이 없으면 초록 상자)
        var g = Build(mdlClock, false, new Vector3(0.28f, 0.38f, 0.2f), true, matTime);
        g.transform.position = p + slope.up * 0.24f;
        g.transform.rotation = Quaternion.identity;
        var o = Setup(g, "time", ItemType.TimeBonus, 0.4f, true, root);
        o.spin = true;
    }

    Obstacle MakeFruit(string kind, Vector3 p, bool resting)
    {
        float size = kind == "apple" ? 0.22f : 0.2f;
        GameObject prefab = kind == "apple" ? mdlApple : (kind == "golden" ? mdlGolden : mdlTangerine);
        Material fm = kind == "apple" ? matApple : (kind == "golden" ? matGolden : matTangerine);
        var g = Build(prefab, true, Vector3.one * size, false, fm);
        g.transform.position = p + (slope != null ? slope.up : Vector3.up) * (size * 0.5f + 0.01f);
        g.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        var o = Setup(g, kind, ItemType.Fruit, 0.3f, resting, null);
        o.points = kind == "apple" ? 15 : (kind == "golden" ? 50 : 10);
        o.visualPrefab = prefab;
        return o;
    }

    static string FruitName(string kind)
    {
        if (kind == "apple") return "사과";
        if (kind == "golden") return "황금귤";
        return "귤";
    }

    // ---------- 임시 HUD ----------

    GUIStyle big, small;

    void OnGUI()
    {
        if (!debugHud) return;
        if (big == null)
        {
            big = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold };
            big.normal.textColor = Color.white;
            small = new GUIStyle(GUI.skin.label) { fontSize = 22 };
            small.normal.textColor = Color.white;
        }
        GUI.Label(new Rect(24, 16, 900, 50), "남은 시간 " + Mathf.CeilToInt(TimeLeft) + "   점수 " + Score, big);
        string s = "수레 " + (cart != null ? cart.Count + " / " + cart.capacity : "-") + "   속도 " + (walker != null ? walker.speed.ToString("F2") : "-") + " m/s   거리 " + (walker != null ? walker.Distance.ToString("F1") : "-") + " m";
        if (arm != null && arm.LastAction != "") s += "\n" + arm.LastAction;
        GUI.Label(new Rect(24, 64, 1200, 80), s, small);
        if (LastEvent != "")
        {
            var c = new GUIStyle(big) { alignment = TextAnchor.MiddleCenter };
            GUI.Label(new Rect(0, Screen.height * 0.18f, Screen.width, 60), LastEvent, c);
        }
    }
}
