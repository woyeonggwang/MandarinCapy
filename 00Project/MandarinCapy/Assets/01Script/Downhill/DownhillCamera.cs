using UnityEngine;

// 카피바라 뒤에서 앞(산 아래)을 바라보는 추적 카메라.
public class DownhillCamera : MonoBehaviour
{
    public Transform target;              // 카피바라
    public Transform slope;               // forward = 내리막 방향
    public float back = 2.4f;             // 뒤로
    public float height = 3.4f;           // 위로 (월드 기준)
    public float lookAhead = 1.8f;        // 이만큼 앞을 바라봄
    public float lookHeight = 0f;
    public float sharpness = 6f;

    void LateUpdate()
    {
        if (target == null) return;
        Vector3 dir = slope != null ? slope.forward : target.forward;
        Vector3 flat = Vector3.ProjectOnPlane(dir, Vector3.up).normalized;
        Vector3 wantPos = target.position - flat * back + Vector3.up * height;
        Vector3 look = target.position + dir * lookAhead + Vector3.up * lookHeight;
        float k = 1f - Mathf.Exp(-sharpness * Time.deltaTime);
        transform.position = Vector3.Lerp(transform.position, wantPos, k);
        Quaternion wantRot = Quaternion.LookRotation(look - transform.position, Vector3.up);
        transform.rotation = Quaternion.Slerp(transform.rotation, wantRot, k);
    }

    public void SnapNow()
    {
        if (target == null) return;
        Vector3 dir = slope != null ? slope.forward : target.forward;
        Vector3 flat = Vector3.ProjectOnPlane(dir, Vector3.up).normalized;
        transform.position = target.position - flat * back + Vector3.up * height;
        transform.rotation = Quaternion.LookRotation(target.position + dir * lookAhead + Vector3.up * lookHeight - transform.position, Vector3.up);
    }
}
