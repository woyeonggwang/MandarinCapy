using UnityEngine;

// 카피바라 걷기/대기 애니메이션 전환.
// 걷는 중(walking이고 막히지 않음)이면 Walk, 아니면 Idle. Walk 재생 속도는 걷는 속도에 비례.
// Walk에는 걸음마다 귀가 흔들리는 동작이 들어 있음 (Blender roboarm.blend의 Capybara_Rig 액션).
public class CapybaraAnimator : MonoBehaviour
{
    public Animator animator;
    public CapybaraWalker walker;
    public string walkState = "Walk";
    public string idleState = "Idle";
    public float walkRefSpeed = 0.6f;     // 이 속도(m/s)에서 Walk를 1배속으로 재생
    public float minWalkAnimSpeed = 0.8f;
    public float maxWalkAnimSpeed = 2.6f;
    public float crossFade = 0.15f;

    bool moving;
    bool started;

    void Update()
    {
        if (animator == null || walker == null) return;
        bool m = walker.walking && !walker.Blocked;
        if (!started || m != moving)
        {
            animator.CrossFadeInFixedTime(m ? walkState : idleState, started ? crossFade : 0f);
            moving = m;
            started = true;
        }
        animator.speed = moving ? Mathf.Clamp(walker.speed / walkRefSpeed, minWalkAnimSpeed, maxWalkAnimSpeed) : 1f;
    }
}
