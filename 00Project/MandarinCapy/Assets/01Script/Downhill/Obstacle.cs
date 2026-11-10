using UnityEngine;

public enum ItemType { Blocker, Fruit, Bomb, Upgrade, TimeBonus }

// 하산 길 위의 물체 (장애물·과일·폭탄·수레 업그레이드). 로봇팔로 집거나 날릴 수 있음.
//  - Blocker: 카피바라를 멈추게 함
//  - Fruit: 수레에 담으면 점수 (points)
//  - Bomb: 카피바라나 수레에 닿으면 터져 과일을 잃음
//  - Upgrade: 카피바라가 닿으면(먹으면) 수레가 커짐
//  - TimeBonus: 카피바라가 닿으면(먹으면) 남은 시간 증가
// 길에 놓인 과일·폭탄·업그레이드는 굴러가지 않도록 키네마틱으로 쉬고 있다가, 집거나 맞으면 Wake()로 물리가 켜짐.
[RequireComponent(typeof(Rigidbody))]
public class Obstacle : MonoBehaviour
{
    public string kind = "rock";
    public ItemType type = ItemType.Blocker;
    public int points = 0;
    public bool spin;   // 업그레이드 아이템 표시용 회전
    public GameObject visualPrefab; // 수레에 담겼을 때 보일 모델 (없으면 구)
    public float noCollectUntil;  // 이 시각(Time.time) 전에는 수레에 담기지 않음 (폭발로 튀어 나간 과일)

    public bool IsHeld { get; set; }
    public bool WasThrown { get; set; }
    public bool WasPlaced { get; set; }

    public Rigidbody Body { get; private set; }

    // 이 높이 아래로 떨어지면 길 밖으로 사라진 것으로 보고 제거
    public float killY = -40f;

    void Awake()
    {
        Body = GetComponent<Rigidbody>();
    }

    void Update()
    {
        if (!IsHeld && transform.position.y < killY)
            Destroy(gameObject);
        if (spin && Body != null && Body.isKinematic && !IsHeld)
            transform.Rotate(0f, 90f * Time.deltaTime, 0f, Space.World);
    }

    // 쉬고 있던(키네마틱) 물체의 물리를 켬
    public void Wake()
    {
        if (Body != null && Body.isKinematic && !IsHeld) Body.isKinematic = false;
    }

    // 물체 위쪽 끝(월드)까지의 높이. 집을 때 그리퍼 위치 계산에 사용.
    public float HalfHeight()
    {
        var c = GetComponent<Collider>();
        return c != null ? c.bounds.extents.y : 0.25f;
    }
}
