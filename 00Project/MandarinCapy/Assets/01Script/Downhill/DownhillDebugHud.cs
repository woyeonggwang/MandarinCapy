using UnityEngine;

// 시제품 확인용 임시 HUD.
public class DownhillDebugHud : MonoBehaviour
{
    public CapybaraWalker walker;
    public ArmController arm;
    GUIStyle style;

    void OnGUI()
    {
        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label) { fontSize = 22 };
            style.normal.textColor = Color.white;
        }
        string s = "";
        if (walker != null)
            s += "하산 거리 " + walker.Distance.ToString("F1") + " m   기다린 시간 " + walker.WaitTime.ToString("F1") + " s" + (walker.Blocked ? "   [막힘: " + walker.Blocker.kind + "]" : "") + "\n";
        if (arm != null)
            s += "팔 속도 " + arm.TcpRelVelocity.magnitude.ToString("F1") + " m/s   " + (arm.Held != null ? "들고 있음: " + arm.Held.kind : "") + "\n" + arm.LastAction;
        GUI.Label(new Rect(20, 20, 1200, 120), s, style);
    }
}
