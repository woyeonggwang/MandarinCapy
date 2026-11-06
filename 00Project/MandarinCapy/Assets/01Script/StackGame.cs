using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace MandarinCapy
{
    public enum GameState { Ready, Swinging, Dropping, GameOver }

    /// <summary>
    /// 카피바라 귤 쌓기 — 임시 도형 프로토타입.
    /// 귤 왕복 → 버튼(스페이스/좌클릭)으로 낙하 → 쌓기 → 기울기 한계 초과 또는 귤 낙하 시 게임 오버.
    /// 탑의 기울기는 물리가 아니라 스크립트로 계산합니다(결과 재현성, 수치 조정 용이).
    /// 물리(Rigidbody2D)는 무너지는 연출에서만 사용합니다.
    /// </summary>
    public class StackGame : MonoBehaviour
    {
        [Header("크기")]
        public float mandarinRadius = 0.45f;
        public Vector2 capybaraBodySize = new Vector2(3.2f, 1.7f);
        public float groundY = -3.5f;

        [Header("왕복")]
        public float swingHalfWidth = 3.2f;
        public float swingSpeedBase = 3f;          // 유닛/초
        public float swingSpeedPerMandarin = 0.18f;
        public float swingSpeedMax = 9f;
        public float swingHeightAboveTop = 2.6f;

        [Header("낙하")]
        public float dropSpeed = 16f;

        [Header("기울기 (무게중심 기반)")]
        [Tooltip("무게중심이 기준점에서 1유닛 벗어날 때 기울기(도)")]
        public float tiltPerUnitCom = 14f;
        [Tooltip("쌓은 개수당 기울기 증폭 비율")]
        public float tiltHeightFactor = 0.05f;
        public float maxTilt = 20f;
        [Tooltip("아래 귤 중심과의 차이가 지름 x 이 비율을 넘으면 미끄러져 떨어짐")]
        public float maxOffsetRatio = 0.95f;

        [Header("흔들림 (연출 + 타이밍 난이도)")]
        public float swayPerMandarin = 0.4f;       // 도
        public float swayMax = 7f;
        public float swayFreqBase = 0.6f;          // Hz
        public float swayFreqPerMandarin = 0.03f;
        public float swayFreqMax = 1.6f;

        [Header("점수")]
        public int scorePerMandarin = 100;
        [Tooltip("차이가 지름 x 이 비율 이하이면 정중앙 판정")]
        public float perfectRatio = 0.12f;
        public int perfectBonusPerCombo = 50;

        [Header("카메라")]
        public float cameraSize = 7f;
        public float cameraFollowLerp = 4f;

        [Header("색 (임시)")]
        public Color mandarinColor = new Color(1f, 0.55f, 0.1f);
        public Color capybaraColor = new Color(0.62f, 0.45f, 0.3f);
        public Color backgroundColor = new Color(0.78f, 0.9f, 0.95f);

        // ---- 상태 ----
        public GameState State { get; private set; } = GameState.Ready;
        public int Score { get; private set; }
        public int Count => _stack.Count;
        public int Combo { get; private set; }
        public float StaticTilt { get; private set; }
        public string GameOverReason { get; private set; } = "";

        Camera _cam;
        Transform _tower;              // 카피바라 머리 위 기준점. 회전 = 기울기 + 흔들림
        readonly List<Transform> _stack = new List<Transform>();
        Transform _current;
        float _swingX;
        int _swingDir = 1;
        float _swayPhase;
        float _gameOverTime;
        float _camBaseY;
        string _lastJudge = "";
        float _lastJudgeTime = -10f;

        float Diameter => mandarinRadius * 2f;
        float TopLocalX => _stack.Count == 0 ? 0f : _stack[_stack.Count - 1].localPosition.x;
        float NextLocalY => mandarinRadius + _stack.Count * Diameter;

        void Awake()
        {
            _cam = Camera.main;
            if (_cam == null)
            {
                var camGo = new GameObject("Main Camera");
                camGo.tag = "MainCamera";
                _cam = camGo.AddComponent<Camera>();
            }
            _cam.orthographic = true;
            _cam.orthographicSize = cameraSize;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = backgroundColor;
            _camBaseY = 1.5f;
            _cam.transform.position = new Vector3(0f, _camBaseY, -10f);

            BuildWorld();
        }

        void BuildWorld()
        {
            // 바닥
            var ground = SpriteFactory.Create("Ground", SpriteFactory.Square, new Color(0.45f, 0.7f, 0.4f), new Vector2(40f, 4f), 0);
            ground.transform.position = new Vector3(0f, groundY - 2f, 0f);
            ground.gameObject.AddComponent<BoxCollider2D>();

            // 카피바라 (임시 도형). 루트는 스케일 1로 두고 부위를 형제로 배치
            var capy = new GameObject("Capybara").transform;
            float bodyY = groundY + capybaraBodySize.y * 0.5f + 0.35f;
            capy.position = new Vector3(0f, bodyY, 0f);

            var body = SpriteFactory.Create("Body", SpriteFactory.Square, capybaraColor, capybaraBodySize, 10, capy);
            body.gameObject.AddComponent<BoxCollider2D>();
            // 다리
            for (int i = 0; i < 2; i++)
            {
                var leg = SpriteFactory.Create("Leg" + i, SpriteFactory.Square, capybaraColor * 0.85f, new Vector2(0.45f, 0.5f), 9, capy);
                leg.transform.localPosition = new Vector3(i == 0 ? -1.0f : 1.0f, -capybaraBodySize.y * 0.5f - 0.15f, 0f);
            }
            // 코 (오른쪽)
            var snout = SpriteFactory.Create("Snout", SpriteFactory.Square, capybaraColor * 0.8f, new Vector2(0.7f, 0.9f), 11, capy);
            snout.transform.localPosition = new Vector3(capybaraBodySize.x * 0.5f + 0.2f, 0.1f, 0f);
            // 태연한 눈 (반쯤 감은 가로선)
            var eye = SpriteFactory.Create("Eye", SpriteFactory.Square, new Color(0.15f, 0.1f, 0.08f), new Vector2(0.35f, 0.08f), 12, capy);
            eye.transform.localPosition = new Vector3(capybaraBodySize.x * 0.5f - 0.35f, 0.35f, 0f);
            // 귀
            var ear = SpriteFactory.Create("Ear", SpriteFactory.Square, capybaraColor * 0.75f, new Vector2(0.25f, 0.25f), 9, capy);
            ear.transform.localPosition = new Vector3(capybaraBodySize.x * 0.5f - 0.7f, capybaraBodySize.y * 0.5f + 0.08f, 0f);

            // 탑 기준점: 몸통 윗면 중앙
            _tower = new GameObject("Tower").transform;
            _tower.position = new Vector3(0f, bodyY + capybaraBodySize.y * 0.5f, 0f);
        }

        static bool Pressed()
        {
            var k = Keyboard.current;
            var m = Mouse.current;
            return (k != null && k.spaceKey.wasPressedThisFrame) ||
                   (m != null && m.leftButton.wasPressedThisFrame);
        }

        void Update()
        {
            float dt = Time.deltaTime;
            bool pressed = Pressed();

            UpdateSway(dt);

            switch (State)
            {
                case GameState.Ready:
                    if (pressed) SpawnMandarin();
                    break;
                case GameState.Swinging:
                    UpdateSwing(dt);
                    if (pressed) State = GameState.Dropping;
                    break;
                case GameState.Dropping:
                    UpdateDrop(dt);
                    break;
                case GameState.GameOver:
                    if (pressed && Time.time - _gameOverTime > 1.0f)
                        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                    break;
            }

            UpdateCamera(dt);
        }

        void UpdateSway(float dt)
        {
            if (State == GameState.GameOver) return;
            int n = _stack.Count;
            float amp = Mathf.Min(swayMax, swayPerMandarin * n);
            float freq = Mathf.Min(swayFreqMax, swayFreqBase + swayFreqPerMandarin * n);
            _swayPhase += Mathf.PI * 2f * freq * dt;
            float sway = amp * Mathf.Sin(_swayPhase);
            _tower.localRotation = Quaternion.Euler(0f, 0f, StaticTilt + sway);
        }

        Vector3 TopWorld()
        {
            return _tower.TransformPoint(new Vector3(TopLocalX, NextLocalY, 0f));
        }

        void SpawnMandarin()
        {
            var sr = SpriteFactory.Create("Mandarin" + (_stack.Count + 1), SpriteFactory.Circle, mandarinColor,
                new Vector2(Diameter, Diameter), 20);
            // 꼭지(임시)
            var leaf = SpriteFactory.Create("Leaf", SpriteFactory.Square, new Color(0.25f, 0.6f, 0.2f), new Vector2(0.18f, 0.12f), 21);
            leaf.transform.SetParent(sr.transform, false);
            leaf.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            leaf.transform.localScale = new Vector3(0.18f / Diameter, 0.12f / Diameter, 1f);

            _current = sr.transform;
            _swingX = -swingHalfWidth;
            _swingDir = 1;
            PositionSwinging();
            State = GameState.Swinging;
        }

        float SwingSpeed => Mathf.Min(swingSpeedMax, swingSpeedBase + swingSpeedPerMandarin * _stack.Count);

        void UpdateSwing(float dt)
        {
            _swingX += _swingDir * SwingSpeed * dt;
            if (_swingX > swingHalfWidth) { _swingX = swingHalfWidth; _swingDir = -1; }
            else if (_swingX < -swingHalfWidth) { _swingX = -swingHalfWidth; _swingDir = 1; }
            PositionSwinging();
        }

        void PositionSwinging()
        {
            float y = TopWorld().y + swingHeightAboveTop;
            _current.position = new Vector3(_swingX, y, 0f);
        }

        void UpdateDrop(float dt)
        {
            var p = _current.position;
            p.y -= dropSpeed * dt;
            _current.position = p;

            Vector3 local = _tower.InverseTransformPoint(p);
            if (local.y > NextLocalY) return;

            // 착지 판정
            float offset = local.x - TopLocalX;
            bool supported;
            if (_stack.Count == 0)
                supported = Mathf.Abs(local.x) <= capybaraBodySize.x * 0.5f;
            else
                supported = Mathf.Abs(offset) <= Diameter * maxOffsetRatio;

            if (!supported)
            {
                Collapse("귤이 떨어졌습니다", _current);
                return;
            }

            Land(local.x, offset);
        }

        void Land(float localX, float offset)
        {
            bool perfect = _stack.Count > 0 && Mathf.Abs(offset) <= Diameter * perfectRatio;
            if (_stack.Count == 0 && Mathf.Abs(localX) <= Diameter * perfectRatio) perfect = true;

            if (perfect)
            {
                localX = TopLocalX; // 정중앙이면 바로 아래 귤에 맞춤
                Combo++;
            }
            else Combo = 0;

            _current.SetParent(_tower, true);
            _current.localPosition = new Vector3(localX, NextLocalY, 0f);
            _current.localRotation = Quaternion.identity;
            _stack.Add(_current);
            _current = null;

            int gained = scorePerMandarin + (perfect ? perfectBonusPerCombo * Combo : 0);
            Score += gained;
            _lastJudge = perfect ? ("PERFECT x" + Combo + "  +" + gained) : ("+" + gained);
            _lastJudgeTime = Time.time;

            RecalcTilt();

            if (Mathf.Abs(StaticTilt) > maxTilt)
            {
                Collapse("탑이 너무 기울었습니다", null);
                return;
            }

            SpawnMandarin();
        }

        void RecalcTilt()
        {
            if (_stack.Count == 0) { StaticTilt = 0f; return; }
            float sum = 0f;
            foreach (var m in _stack) sum += m.localPosition.x;
            float com = sum / _stack.Count;
            // 오른쪽(+x)으로 쏠리면 시계 방향(-z)으로 기울어짐
            StaticTilt = -com * tiltPerUnitCom * (1f + tiltHeightFactor * _stack.Count);
        }

        void Collapse(string reason, Transform fallingOne)
        {
            State = GameState.GameOver;
            GameOverReason = reason;
            _gameOverTime = Time.time;
            Combo = 0;

            float dirSign = Mathf.Sign(StaticTilt == 0f ? (fallingOne != null ? -fallingOne.position.x : 1f) : StaticTilt);
            // 연출: 쌓인 귤에 물리 적용
            for (int i = 0; i < _stack.Count; i++)
            {
                var m = _stack[i];
                m.SetParent(null, true);
                AddPhysics(m, new Vector2(-dirSign * (1.5f + i * 0.25f) + Random.Range(-0.5f, 0.5f), Random.Range(0.5f, 2f)));
            }
            if (fallingOne != null)
                AddPhysics(fallingOne, new Vector2(0f, -2f));
        }

        static void AddPhysics(Transform t, Vector2 velocity)
        {
            var col = t.gameObject.AddComponent<CircleCollider2D>();
            col.radius = 0.5f; // 스프라이트 로컬 기준(지름 1)
            var rb = t.gameObject.AddComponent<Rigidbody2D>();
            rb.linearVelocity = velocity;
            rb.angularVelocity = Random.Range(-200f, 200f);
        }

        void UpdateCamera(float dt)
        {
            if (State == GameState.GameOver) return;
            float targetY = Mathf.Max(_camBaseY, TopWorld().y + swingHeightAboveTop - cameraSize * 0.55f);
            var cp = _cam.transform.position;
            cp.y = Mathf.Lerp(cp.y, targetY, 1f - Mathf.Exp(-cameraFollowLerp * dt));
            _cam.transform.position = cp;
        }

        // ---- 임시 HUD (점수·랭킹 UI는 11/7 작업에서 교체) ----
        GUIStyle _big, _mid;

        void OnGUI()
        {
            if (_big == null)
            {
                _big = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                _big.normal.textColor = new Color(0.2f, 0.15f, 0.1f);
                _mid = new GUIStyle(_big) { fontSize = 24, fontStyle = FontStyle.Normal };
            }

            GUI.Label(new Rect(20, 10, 600, 40), $"점수 {Score}   귤 {Count}개   콤보 {Combo}", new GUIStyle(_mid) { alignment = TextAnchor.MiddleLeft });
            GUI.Label(new Rect(20, 45, 600, 40), $"기울기 {StaticTilt:0.0}° / 한계 {maxTilt:0}°", new GUIStyle(_mid) { alignment = TextAnchor.MiddleLeft });

            float w = Screen.width, h = Screen.height;
            if (Time.time - _lastJudgeTime < 0.8f && State != GameState.GameOver)
                GUI.Label(new Rect(0, h * 0.2f, w, 50), _lastJudge, _big);

            if (State == GameState.Ready)
            {
                GUI.Label(new Rect(0, h * 0.3f, w, 60), "카피바라 귤 쌓기", _big);
                GUI.Label(new Rect(0, h * 0.3f + 60, w, 40), "스페이스바 또는 마우스 왼쪽 버튼으로 귤을 떨어뜨리세요", _mid);
            }
            else if (State == GameState.GameOver)
            {
                GUI.Label(new Rect(0, h * 0.3f, w, 60), "게임 오버", _big);
                GUI.Label(new Rect(0, h * 0.3f + 60, w, 40), GameOverReason, _mid);
                GUI.Label(new Rect(0, h * 0.3f + 100, w, 40), $"최종 점수 {Score}  (귤 {Count}개)", _mid);
                if (Time.time - _gameOverTime > 1.0f)
                    GUI.Label(new Rect(0, h * 0.3f + 150, w, 40), "누르면 다시 시작", _mid);
            }
        }
    }
}
