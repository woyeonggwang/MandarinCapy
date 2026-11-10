using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// 카피바라 등에 달린 6축 로봇팔.
// 마우스가 가리키는 지점을 TCP 목표로 삼아 해석적 IK로 관절 각도를 구하고,
// 각 관절이 목표 각도를 빠르게 따라간다(관절 공간 보간 → 베이스가 빙 돌아가며 움직임).
//  - J1(베이스): 목표 방향으로 회전 (로컬 Z)
//  - J2·J3(어깨·팔꿈치): 2링크 해석 IK (로컬 X)
//  - J4(손목1): 툴이 아래를 향하도록 (로컬 X)
// 조작:
//  - 왼쪽 버튼 누름: 그리퍼 근처 물체 집기 / 뗌: 직전 짧은 시간의 최고 속도가 빠르면 날리기, 느리면 내려놓기
//  - 빈손으로 휘둘러 아래팔·손목·그리퍼가 물체에 닿으면 휘두른 속도로 쳐 냄
// 관절 축 정보는 CLAUDE.md "6축 로봇팔 모델" 참고 (루트 로컬 좌표계에서 위쪽 = -Z).
public class ArmController : MonoBehaviour
{
    [Header("관절 (RobotArm6Axis 계층)")]
    public Transform armRoot;
    public Transform j1, j2, j3, j4, j5, j6, tcp, fingerL, fingerR;

    [Header("치수 (모델 로컬 단위, m)")]
    public Vector3 shoulderLocal = new Vector3(0f, 0f, -0.25f); // 루트 기준 J2 위치
    public float upperArm = 0.415f;   // J2→J3
    public float foreArm = 0.355f;    // J3→J4
    public float wristToTcp = 0.25f;  // J4→TCP (툴이 아래를 향할 때)

    [Header("조작")]
    public Camera cam;
    public Transform ignoreRoot;            // 레이캐스트·충돌에서 무시할 자기 몸(카피바라)
    public float jointSmoothTime = 0.05f;   // 관절이 목표 각도를 따라가는 시간 (작을수록 민첩)
    public float jointMaxSpeed = 900f;      // 관절 최대 각속도 (도/초)
    public float hoverHeight = 0.06f;       // 빈손일 때 가리킨 표면 위 높이
    public float fallbackPlaneHeight = 0.35f; // 바닥을 못 찾았을 때 쓰는 카피바라 기준 가상 평면 높이
    public float maxRayDistance = 25f;
    public float carryHeight = 0.35f;       // 들고 빠르게 움직일 때 물체 바닥을 띄우는 높이
    public float placeHeight = 0.03f;       // 들고 천천히 움직이면 이 높이까지 내려감 (세심하게 내려놓기)
    public float liftSpeedRef = 1.2f;       // 이 속도(m/s)에서 carryHeight까지 올라감
    public float grabRadius = 0.32f;        // TCP에서 이 거리 안의 물체를 집음
    public float grabAssistRadius = 0.55f;  // 근처에 없으면 이 거리 안의 '쉬고 있는(키네마틱)' 물체를 집음 (나무의 황금귤 등)

    [Header("놓기 / 날리기")]
    public float throwSpeedThreshold = 1.5f; // 직전 peakWindow 동안의 최고 속도가 이 이상이면 날리기
    public float peakWindow = 0.12f;         // 손을 떼기 직전 이 시간 안의 최고 속도를 사용
    public float throwMultiplier = 2.2f;
    public float maxThrowSpeed = 16f;
    public float placeVelocityKeep = 0.15f;  // 내려놓을 때 남기는 속도 비율

    [Header("쳐 내기 (빈손 휘두르기)")]
    public bool enableSwat = true;
    public float swatMinSpeed = 1.0f;        // 이 속도 이상으로 닿아야 쳐 냄
    public float swatMultiplier = 1.6f;
    public float swatLift = 0.25f;           // 쳐 낼 때 위로 띄우는 비율
    public float swatCooldown = 0.25f;       // 같은 물체를 연속으로 치지 않는 시간
    public float linkRadius = 0.05f;         // 모델 로컬 단위 (스케일 곱해짐)

    [Header("그리퍼")]
    public float fingerOpen = 0.035f;
    public float fingerClosed = 0.014f;

    public bool inputEnabled = true;

    public event System.Action<Obstacle, bool> Released;  // (물체, 날렸는지)
    public event System.Action<Obstacle> Grabbed;
    public event System.Action<Obstacle, float> Swatted;  // (물체, 속도)

    public Obstacle Held { get; private set; }
    public Vector3 TcpVelocity { get; private set; }      // 월드 속도 (걷는 속도 포함)
    public Vector3 TcpRelVelocity { get; private set; }   // 카피바라 몸 기준 속도 (팔을 휘두른 속도)
    public string LastAction { get; private set; } = "";

    // 관절 각도 (현재 / 목표 / SmoothDamp 속도)
    float yaw, a2, a3, a4;
    float tYaw, tA2, tA3, tA4;
    float vYaw, vA2, vA3, vA4;
    bool hasTarget;

    Vector3 lastTcpPos, lastTcpLocal, lastWristLocal;
    Vector3 wristRelVel;
    Vector3 heldOffset;
    float fingerPos;
    float carryLift;

    struct VelSample { public float t; public Vector3 v; }
    readonly List<VelSample> velHistory = new List<VelSample>();
    readonly Dictionary<Obstacle, float> lastSwat = new Dictionary<Obstacle, float>();

    void Start()
    {
        if (cam == null) cam = Camera.main;
        fingerPos = fingerOpen;
        lastTcpPos = tcp.position;
        lastTcpLocal = ToBody(tcp.position);
        lastWristLocal = ToBody(j4.position);
    }

    Vector3 ToBody(Vector3 world) { return ignoreRoot != null ? ignoreRoot.InverseTransformPoint(world) : world; }
    Vector3 FromBodyDir(Vector3 v) { return ignoreRoot != null ? ignoreRoot.TransformVector(v) : v; }

    void Update()
    {
        float dt = Mathf.Max(Time.deltaTime, 1e-4f);

        if (inputEnabled && Mouse.current != null)
        {
            Vector3 target;
            if (TryGetMouseTarget(out target))
            {
                ComputeIK(ClampToReach(target), out tYaw, out tA2, out tA3, out tA4, hasTarget ? tYaw : yaw);
                if (!hasTarget) { yaw = tYaw; a2 = tA2; a3 = tA3; a4 = tA4; hasTarget = true; }
            }

            if (Mouse.current.leftButton.wasPressedThisFrame && Held == null) TryGrab();
            if (Mouse.current.leftButton.wasReleasedThisFrame && Held != null) Release();
        }

        if (hasTarget)
        {
            yaw = Mathf.SmoothDampAngle(yaw, tYaw, ref vYaw, jointSmoothTime, jointMaxSpeed, dt);
            a2 = Mathf.SmoothDampAngle(a2, tA2, ref vA2, jointSmoothTime, jointMaxSpeed, dt);
            a3 = Mathf.SmoothDampAngle(a3, tA3, ref vA3, jointSmoothTime, jointMaxSpeed, dt);
            a4 = Mathf.SmoothDampAngle(a4, tA4, ref vA4, jointSmoothTime, jointMaxSpeed, dt);
            ApplyAngles();
        }

        // 들고 있을 때 높이: 빠르게 움직이면 들어 올리고, 천천히 움직이면 내려감
        float liftGoal = Mathf.Lerp(placeHeight, carryHeight, Mathf.Clamp01(TcpRelVelocity.magnitude / liftSpeedRef));
        carryLift = Mathf.Lerp(carryLift, liftGoal, 1f - Mathf.Exp(-4f * dt));

        // 속도 (월드 / 몸 기준)
        Vector3 v = (tcp.position - lastTcpPos) / dt;
        TcpVelocity = Vector3.Lerp(TcpVelocity, v, 0.6f);
        lastTcpPos = tcp.position;
        Vector3 localNow = ToBody(tcp.position);
        TcpRelVelocity = Vector3.Lerp(TcpRelVelocity, FromBodyDir((localNow - lastTcpLocal) / dt), 0.6f);
        lastTcpLocal = localNow;
        Vector3 wristNow = ToBody(j4.position);
        wristRelVel = Vector3.Lerp(wristRelVel, FromBodyDir((wristNow - lastWristLocal) / dt), 0.6f);
        lastWristLocal = wristNow;

        float now = Time.time;
        velHistory.Add(new VelSample { t = now, v = TcpRelVelocity });
        while (velHistory.Count > 0 && now - velHistory[0].t > peakWindow) velHistory.RemoveAt(0);

        // 손가락
        float fTarget = Held != null ? fingerClosed : fingerOpen;
        fingerPos = Mathf.MoveTowards(fingerPos, fTarget, 0.15f * dt);
        if (fingerL) fingerL.localPosition = new Vector3(fingerPos, fingerL.localPosition.y, fingerL.localPosition.z);
        if (fingerR) fingerR.localPosition = new Vector3(-fingerPos, fingerR.localPosition.y, fingerR.localPosition.z);
    }

    void FixedUpdate()
    {
        if (Held != null && Held.Body != null)
            Held.Body.MovePosition(tcp.position + heldOffset);
        else if (enableSwat)
            CheckSwat();
    }

    // 직전 peakWindow 안에서 가장 빠른 몸 기준 속도
    public Vector3 PeakRelVelocity()
    {
        Vector3 best = TcpRelVelocity;
        foreach (var s in velHistory) if (s.v.sqrMagnitude > best.sqrMagnitude) best = s.v;
        return best;
    }

    // ---------- 목표점 ----------

    bool TryGetMouseTarget(out Vector3 target)
    {
        target = Vector3.zero;
        if (cam == null) return false;
        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        var hits = Physics.RaycastAll(ray, maxRayDistance, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        RaycastHit bestHit = default(RaycastHit);
        bool found = false;
        foreach (var h in hits)
        {
            if (ignoreRoot != null && h.collider.transform.IsChildOf(ignoreRoot)) continue;
            if (Held != null && h.collider.transform.IsChildOf(Held.transform)) continue;
            // 들고 있을 때는 다른 물체 위가 아니라 바닥 기준으로 움직임
            if (Held != null && h.collider.GetComponentInParent<Obstacle>() != null) continue;
            if (h.distance < best) { best = h.distance; bestHit = h; found = true; }
        }

        Vector3 point;
        if (found) point = bestHit.point;
        else
        {
            // 바닥 밖(하늘 등)을 가리켜도 팔이 멈추지 않도록 카피바라 기준 가상 평면과 교차
            Transform body = ignoreRoot != null ? ignoreRoot : armRoot;
            Plane p = new Plane(body.up, body.position + body.up * fallbackPlaneHeight);
            float enter;
            if (!p.Raycast(ray, out enter)) return false;
            point = ray.GetPoint(enter);
        }

        Vector3 up = Vector3.up;
        if (Held != null)
            target = point + up * (carryLift + 2f * Held.HalfHeight());
        else
            target = point + up * hoverHeight;
        return true;
    }

    // 손목이 닿을 수 있는 범위로 목표를 제한 (팔이 닿는 범위의 제약 = 게임 규칙)
    Vector3 ClampToReach(Vector3 tcpTarget)
    {
        Vector3 local = armRoot.InverseTransformPoint(tcpTarget);
        Vector3 wrist = local + new Vector3(0f, 0f, -wristToTcp);
        Vector3 d = wrist - shoulderLocal;
        float max = (upperArm + foreArm) * 0.995f;
        if (d.magnitude > max)
        {
            d = d.normalized * max;
            wrist = shoulderLocal + d;
            local = wrist - new Vector3(0f, 0f, -wristToTcp);
            return armRoot.TransformPoint(local);
        }
        return tcpTarget;
    }

    // ---------- IK ----------

    void ComputeIK(Vector3 tcpTargetWorld, out float oYaw, out float o2, out float o3, out float o4, float keepYaw)
    {
        Vector3 p = armRoot.InverseTransformPoint(tcpTargetWorld);
        Vector3 w = p + new Vector3(0f, 0f, -wristToTcp); // 손목은 TCP 바로 위 (위쪽 = -Z)
        Vector3 d = w - shoulderLocal;
        float h = Mathf.Sqrt(d.x * d.x + d.y * d.y);
        float v = -d.z;

        oYaw = h > 0.02f ? Mathf.Atan2(d.x, -d.y) * Mathf.Rad2Deg : keepYaw;

        float D = Mathf.Sqrt(h * h + v * v);
        D = Mathf.Clamp(D, Mathf.Abs(upperArm - foreArm) + 0.01f, upperArm + foreArm - 0.0005f);
        float cosE = (upperArm * upperArm + foreArm * foreArm - D * D) / (2f * upperArm * foreArm);
        float beta = Mathf.PI - Mathf.Acos(Mathf.Clamp(cosE, -1f, 1f));
        float alpha = Mathf.Acos(Mathf.Clamp((upperArm * upperArm + D * D - foreArm * foreArm) / (2f * upperArm * D), -1f, 1f));
        float phi1 = Mathf.Atan2(h, v) - alpha;          // 위팔 기울기 (수직 기준, 목표 쪽으로)
        float phi4 = Mathf.PI - phi1 - beta;             // 손목: 툴이 아래를 향하도록

        o2 = -phi1 * Mathf.Rad2Deg;
        o3 = -beta * Mathf.Rad2Deg;
        o4 = -phi4 * Mathf.Rad2Deg;
    }

    void ApplyAngles()
    {
        j1.localRotation = Quaternion.AngleAxis(yaw, Vector3.forward);
        j2.localRotation = Quaternion.AngleAxis(a2, Vector3.right);
        j3.localRotation = Quaternion.AngleAxis(a3, Vector3.right);
        j4.localRotation = Quaternion.AngleAxis(a4, Vector3.right);
        if (j5) j5.localRotation = Quaternion.identity;
    }

    // 즉시 해당 자세로 (테스트·초기화용). 이후 Update의 목표도 같은 값으로 맞춘다.
    public void SolveIK(Vector3 tcpTargetWorld)
    {
        ComputeIK(tcpTargetWorld, out tYaw, out tA2, out tA3, out tA4, yaw);
        yaw = tYaw; a2 = tA2; a3 = tA3; a4 = tA4;
        vYaw = vA2 = vA3 = vA4 = 0f;
        hasTarget = true;
        ApplyAngles();
    }

    // ---------- 쳐 내기 ----------

    void CheckSwat()
    {
        float s = armRoot.lossyScale.x;
        float r = linkRadius * s;
        // 아래팔(J3→J4), 손목~그리퍼(J4→TCP) 두 구간
        SwatSegment(j3.position, j4.position, r, wristRelVel);
        SwatSegment(j4.position, tcp.position, r * 1.3f, TcpRelVelocity);
    }

    void SwatSegment(Vector3 a, Vector3 b, float radius, Vector3 segVel)
    {
        float speed = segVel.magnitude;
        if (speed < swatMinSpeed) return;
        var cols = Physics.OverlapCapsule(a, b, radius, ~0, QueryTriggerInteraction.Ignore);
        foreach (var c in cols)
        {
            var o = c.GetComponentInParent<Obstacle>();
            if (o == null || o.IsHeld || o.Body == null) continue;
            float last;
            if (lastSwat.TryGetValue(o, out last) && Time.time - last < swatCooldown) continue;
            lastSwat[o] = Time.time;
            Vector3 dir = segVel / speed;
            Vector3 hitVel = dir * speed * swatMultiplier + Vector3.up * speed * swatLift;
            hitVel = Vector3.ClampMagnitude(hitVel, maxThrowSpeed);
            o.Wake();
            o.Body.AddForce(hitVel, ForceMode.VelocityChange);
            o.Body.AddTorque(Random.insideUnitSphere * speed * 2f, ForceMode.VelocityChange);
            o.WasThrown = true;
            LastAction = "쳐 내기: " + o.kind + " (" + speed.ToString("F1") + " m/s)";
            if (Swatted != null) Swatted(o, speed);
        }
    }

    // ---------- 집기 / 놓기 ----------

    void TryGrab()
    {
        var best = FindGrabbable(grabRadius, false);
        if (best == null) best = FindGrabbable(grabAssistRadius, true); // 보정: 가만히 있는 물체만
        if (best != null) Grab(best);
    }

    Obstacle FindGrabbable(float radius, bool restingOnly)
    {
        var cols = Physics.OverlapSphere(tcp.position, radius, ~0, QueryTriggerInteraction.Ignore);
        Obstacle best = null;
        float bestD = float.MaxValue;
        foreach (var c in cols)
        {
            var o = c.GetComponentInParent<Obstacle>();
            if (o == null || o.IsHeld) continue;
            if (restingOnly && (o.Body == null || !o.Body.isKinematic)) continue;
            float dd = (c.ClosestPoint(tcp.position) - tcp.position).sqrMagnitude;
            if (dd < bestD) { bestD = dd; best = o; }
        }
        return best;
    }

    public void Grab(Obstacle o)
    {
        Held = o;
        o.IsHeld = true;
        o.Body.isKinematic = true;
        o.Body.interpolation = RigidbodyInterpolation.Interpolate;
        heldOffset = Vector3.down * o.HalfHeight(); // 물체 윗면이 그리퍼 끝에 오도록
        LastAction = "집기: " + o.kind;
        if (Grabbed != null) Grabbed(o);
    }

    public void Release()
    {
        var o = Held;
        Held = null;
        if (o == null) return;
        o.IsHeld = false;
        o.Body.isKinematic = false;
        Vector3 rel = PeakRelVelocity();
        Vector3 carrier = TcpVelocity - TcpRelVelocity;   // 카피바라가 걷는 속도
        bool thrown = rel.magnitude >= throwSpeedThreshold;
        if (thrown)
        {
            o.Body.linearVelocity = Vector3.ClampMagnitude(rel * throwMultiplier, maxThrowSpeed) + carrier;
            o.Body.angularVelocity = Random.insideUnitSphere * 6f;
            o.WasThrown = true;
            LastAction = "날리기: " + o.kind + " (" + rel.magnitude.ToString("F1") + " m/s)";
        }
        else
        {
            o.Body.linearVelocity = TcpRelVelocity * placeVelocityKeep + carrier;
            o.Body.angularVelocity = Vector3.zero;
            o.WasPlaced = true;
            LastAction = "내려놓기: " + o.kind;
        }
        lastSwat[o] = Time.time; // 놓자마자 팔에 다시 맞지 않게
        if (Released != null) Released(o, thrown);
    }
}
