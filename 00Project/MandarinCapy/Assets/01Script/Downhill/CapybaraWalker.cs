using UnityEngine;

// 경사길을 따라 태평하게 내려가는 카피바라 (시제품: 직선 경사).
// 앞에 장애물이 있으면 멈춰서 기다린다.
public class CapybaraWalker : MonoBehaviour
{
    public Transform slope;               // 경사길 (forward = 내리막 방향)
    public float speed = 1.0f;            // m/s
    public float checkDistance = 0.15f;    // 몸 앞쪽 이 거리 안에 장애물이 있으면 멈춤
    public Vector3 checkHalfExtents = new Vector3(0.3f, 0.25f, 0.05f);
    public float bodyFrontOffset = 0.55f;  // 몸 중심에서 앞쪽 끝까지
    public float bodyCenterHeight = 0.3f;

    public bool Blocked { get; private set; }
    public Obstacle Blocker { get; private set; }
    public float Distance { get; private set; }
    public float WaitTime { get; private set; }
    public bool walking = true;

    Rigidbody body;
    Vector3 startPos;

    Vector3 Dir { get { return slope != null ? slope.forward : transform.forward; } }
    Vector3 Up { get { return slope != null ? slope.up : Vector3.up; } }

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        startPos = transform.position;
    }

    public void ResetWalk(Vector3 pos)
    {
        startPos = pos;
        transform.position = pos;
        if (body) body.position = pos;
        Distance = 0f;
        WaitTime = 0f;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        Blocker = FindBlocker();
        Blocked = Blocker != null;

        if (walking && !Blocked)
        {
            Vector3 next = (body ? body.position : transform.position) + Dir * speed * dt;
            if (body) body.MovePosition(next); else transform.position = next;
            Distance += speed * dt;
        }
        else if (walking)
        {
            WaitTime += dt;
        }
    }

    Obstacle FindBlocker()
    {
        Vector3 center = transform.position + Up * bodyCenterHeight + Dir * bodyFrontOffset;
        Quaternion rot = Quaternion.LookRotation(Dir, Up);
        var hits = Physics.BoxCastAll(center, checkHalfExtents, Dir, rot, checkDistance, ~0, QueryTriggerInteraction.Ignore);
        Obstacle best = null;
        float bestD = float.MaxValue;
        foreach (var h in hits)
        {
            var o = h.collider.GetComponentInParent<Obstacle>();
            if (o == null || o.IsHeld || o.type != ItemType.Blocker) continue;
            if (h.distance < bestD) { bestD = h.distance; best = o; }
        }
        // 이미 겹쳐 있는 경우 (BoxCast는 시작 위치에서 겹친 콜라이더를 distance 0으로 돌려줌)
        return best;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Vector3 center = transform.position + Up * bodyCenterHeight + Dir * (bodyFrontOffset + checkDistance * 0.5f);
        Gizmos.DrawWireCube(center, new Vector3(checkHalfExtents.x * 2f, checkHalfExtents.y * 2f, checkDistance));
    }
}
