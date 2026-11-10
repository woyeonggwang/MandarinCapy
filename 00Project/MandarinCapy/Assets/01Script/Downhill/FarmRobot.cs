using UnityEngine;

// 밭에서 일하는 수확 로봇 (배경 연출).
// 로봇팔 관절을 천천히 사인파로 움직여 작물을 따는 듯한 동작을 반복.
// 관절 축은 RobotArm6Axis 기준 (J1 로컬 Z, J2·J3·J4 로컬 X).
public class FarmRobot : MonoBehaviour
{
    public Transform j1, j2, j3, j4;
    public float speed = 0.6f;
    public float phase;
    public float yawCenter = 0f;
    public float yawRange = 60f;

    void Start()
    {
        if (phase == 0f) phase = Random.value * 10f;
    }

    void Update()
    {
        float t = Time.time * speed + phase;
        if (j1) j1.localRotation = Quaternion.AngleAxis(yawCenter + Mathf.Sin(t * 0.7f) * yawRange, Vector3.forward);
        float reach = (Mathf.Sin(t) + 1f) * 0.5f;          // 0 = 들어 올림, 1 = 아래로 뻗음
        if (j2) j2.localRotation = Quaternion.AngleAxis(Mathf.Lerp(-15f, -55f, reach), Vector3.right);
        if (j3) j3.localRotation = Quaternion.AngleAxis(Mathf.Lerp(-70f, -95f, reach), Vector3.right);
        if (j4) j4.localRotation = Quaternion.AngleAxis(Mathf.Lerp(-80f, -30f, reach), Vector3.right);
    }
}
