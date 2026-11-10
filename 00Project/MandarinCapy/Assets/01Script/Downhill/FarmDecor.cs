using UnityEngine;

// 농장 배경 (게임 시작 시 한 번 생성, 고정 시드):
//  - 흙길 띠, 길가 울타리, 양옆 밭고랑(작물 줄 + 열매 점), 밭에서 일하는 수확 로봇들(로봇팔 + 바퀴 차체)
// 콘셉트: 수확 로봇들이 일하는 농장에서, 일하고 싶은 카피바라가 로봇팔을 달고 수확을 돕는다.
// 콜라이더: 울타리(보이지 않는 벽, 높이 0.7), 밭고랑, 로봇 차체만. 나머지(길·열매·바퀴 등)는 없음.
// (2026-11-09 사용자 요청: 날린 물체가 울타리·밭고랑을 통과하지 않게)
public class FarmDecor : MonoBehaviour
{
    public Transform slope;               // forward = 내리막
    public GameObject robotArmPrefab;     // RobotArm6Axis.fbx
    public int seed = 21;

    [Header("재질")]
    public Material matRoad, matFence, matCrop, matProduceA, matProduceB, matRobotBody, matWheel;

    [Header("배치")]
    public float roadWidth = 3.4f;
    public float fenceX = 2.3f;
    public float fenceStep = 3f;
    public float rowStartX = 3.6f;
    public float rowStep = 1.6f;
    public int rowsPerSide = 8;
    public int produceRows = 4;           // 길에 가까운 몇 줄에만 열매 점
    public float produceStep = 2.6f;
    public int robotCount = 16;
    public int robotNearRows = 3;         // 길에서 몇 번째 고랑 사이까지 로봇을 두는지
    public float robotScale = 1.25f;
    public float cropHeight = 0.26f;

    Transform root;
    float Length { get { return slope.localScale.z; } }

    Vector3 P(float d, float x, float h)
    {
        return slope.position + slope.up * (slope.localScale.y * 0.5f + h) - slope.forward * (Length * 0.5f) + slope.forward * d + slope.right * x;
    }

    void Awake()
    {
        if (slope == null) return;
        root = new GameObject("FarmDecor_Runtime").transform;
        var rnd = new System.Random(seed);
        Quaternion along = Quaternion.LookRotation(slope.forward, slope.up);

        // 흙길
        Box("Road", P(Length * 0.5f, 0f, 0.006f), along, new Vector3(roadWidth, 0.01f, Length), matRoad);

        // 울타리: 기둥 + 가로대 2줄
        for (int s = -1; s <= 1; s += 2)
        {
            for (float d = 0f; d <= Length; d += fenceStep)
                Box("Post", P(d, s * fenceX, 0.3f), along, new Vector3(0.08f, 0.6f, 0.08f), matFence);
            Box("Rail", P(Length * 0.5f, s * fenceX, 0.25f), along, new Vector3(0.04f, 0.05f, Length), matFence);
            Box("Rail", P(Length * 0.5f, s * fenceX, 0.50f), along, new Vector3(0.04f, 0.05f, Length), matFence);
            Wall("FenceWall", P(Length * 0.5f, s * fenceX, 0.35f), along, new Vector3(0.12f, 0.7f, Length));
        }

        // 밭고랑 + 열매
        for (int s = -1; s <= 1; s += 2)
        {
            for (int i = 0; i < rowsPerSide; i++)
            {
                float x = s * (rowStartX + i * rowStep);
                Box("CropRow", P(Length * 0.5f, x, cropHeight * 0.5f), along, new Vector3(0.7f, cropHeight, Length), matCrop, null, true);
                if (i < produceRows)
                {
                    for (float d = (float)rnd.NextDouble() * produceStep; d < Length; d += produceStep)
                    {
                        var m = rnd.NextDouble() < 0.7 ? matProduceA : matProduceB;
                        float side = (float)(rnd.NextDouble() - 0.5) * 0.5f;
                        Ball(P(d, x + side, cropHeight + 0.05f), 0.16f, m);
                    }
                }
            }
        }

        // 수확 로봇
        for (int i = 0; i < robotCount; i++)
        {
            float d = 10f + (Length - 30f) * (i + (float)rnd.NextDouble() * 0.6f) / robotCount;
            int s = (i % 2 == 0) ? -1 : 1;
            int row = rnd.Next(Mathf.Min(robotNearRows, rowsPerSide - 1));
            float x = s * (rowStartX + row * rowStep + rowStep * 0.5f); // 고랑 사이 통로
            MakeRobot(P(d, x, 0f), along, (float)rnd.NextDouble() * 10f, s);
        }
    }

    void MakeRobot(Vector3 pos, Quaternion along, float phase, int side)
    {
        var r = new GameObject("FarmRobot").transform;
        r.SetParent(root, false);
        r.position = pos;
        r.rotation = along;
        r.localScale = Vector3.one * robotScale;
        Box("Chassis", r.TransformPoint(new Vector3(0f, 0.32f, 0f)), r.rotation, new Vector3(0.7f, 0.3f, 1.0f), matRobotBody, r, true);
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sz = -1; sz <= 1; sz += 2)
            {
                var w = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                Destroy(w.GetComponent<Collider>());
                w.transform.SetParent(r, false);
                w.transform.localPosition = new Vector3(sx * 0.38f, 0.16f, sz * 0.35f);
                w.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                w.transform.localScale = new Vector3(0.32f, 0.05f, 0.32f);
                w.GetComponent<Renderer>().sharedMaterial = matWheel;
            }
        // 차체 위 과일 상자
        Box("Bin", r.TransformPoint(new Vector3(0f, 0.55f, -0.3f)), r.rotation, new Vector3(0.5f, 0.18f, 0.35f), matFence, r);
        Ball(r.TransformPoint(new Vector3(-0.1f, 0.66f, -0.3f)), 0.13f, matProduceA, r);
        Ball(r.TransformPoint(new Vector3(0.1f, 0.66f, -0.28f)), 0.13f, matProduceB, r);

        if (robotArmPrefab != null)
        {
            var arm = Instantiate(robotArmPrefab, r);
            arm.transform.localPosition = new Vector3(0f, 0.47f, 0.2f);
            arm.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            arm.transform.localScale = Vector3.one * 0.8f;
            var fr = arm.AddComponent<FarmRobot>();
            fr.j1 = Find(arm.transform, "J1_Base"); fr.j2 = Find(arm.transform, "J2_Shoulder");
            fr.j3 = Find(arm.transform, "J3_Elbow"); fr.j4 = Find(arm.transform, "J4_Wrist1");
            fr.phase = phase;
            fr.yawCenter = side > 0 ? 90f : -90f;   // 길 바깥쪽(밭) 방향을 주로 봄
            fr.yawRange = 50f;
        }
    }

    static Transform Find(Transform t, string n)
    {
        foreach (var c in t.GetComponentsInChildren<Transform>()) if (c.name == n) return c;
        return null;
    }

    GameObject Box(string name, Vector3 pos, Quaternion rot, Vector3 size, Material m, Transform parent = null, bool solid = false)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
        if (!solid) Destroy(g.GetComponent<Collider>());
        g.name = name;
        g.transform.SetParent(parent != null ? parent : root, true);
        g.transform.position = pos;
        g.transform.rotation = rot;
        g.transform.localScale = parent != null ? Div(size, parent.lossyScale) : size;
        g.GetComponent<Renderer>().sharedMaterial = m;
        if (solid) g.isStatic = true;
        return g;
    }

    // 보이지 않는 벽 (콜라이더만)
    GameObject Wall(string name, Vector3 pos, Quaternion rot, Vector3 size)
    {
        var g = new GameObject(name);
        g.transform.SetParent(root, true);
        g.transform.position = pos;
        g.transform.rotation = rot;
        g.AddComponent<BoxCollider>().size = size;
        g.isStatic = true;
        return g;
    }

    GameObject Ball(Vector3 pos, float size, Material m, Transform parent = null)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(g.GetComponent<Collider>());
        g.name = "Produce";
        g.transform.SetParent(parent != null ? parent : root, true);
        g.transform.position = pos;
        g.transform.localScale = Vector3.one * size;
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g;
    }

    static Vector3 Div(Vector3 a, Vector3 b) { return new Vector3(a.x / b.x, a.y / b.y, a.z / b.z); }
}
