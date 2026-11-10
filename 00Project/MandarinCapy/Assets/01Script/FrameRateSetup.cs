using UnityEngine;

// 프레임 제한: 60fps (사용자 결정 2026-11-09). 박람회 PC에서 GPU를 계속 최대로 쓰지 않게.
// targetFrameRate는 vSync가 꺼져 있어야 적용되므로 vSyncCount = 0으로 둠.
public static class FrameRateSetup
{
    public const int TargetFps = 60;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Apply()
    {
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = TargetFps;
    }
}
