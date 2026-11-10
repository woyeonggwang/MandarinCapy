using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MandarinCapy
{
    public enum GameState { Ready, Swinging, Dropping, GameOver }

    /// <summary>
    /// 카피바라 귤 쌓기.
    /// 귤 왕복 → 버튼(스페이스/좌클릭)으로 낙하 → Rigidbody2D 물리로 쌓임.
    /// 귤이 받침 아래로 떨어지거나 쌓인 귤이 무너져 내려가면 게임 오버.
    /// 쌓을수록 왕복 속도가 빨라지고 머리 위 받침(키네마틱)이 좌우로 더 크게 흔들림.
    /// </summary>
    public class StackGame : MonoBehaviour, IRoundGame
    {
        [Header("크기")]
        public float mandarinRadius = 0.45f;

        [Header("이미지 (비어 있으면 임시 도형 사용)")]
        public Sprite[] mandarinSprites;
        public Sprite capybaraIdleSprite;
        public Sprite capybaraEyesClosedSprite;
        public Sprite plateSprite;
        public Sprite backgroundSprite;
        [Tooltip("배경 이미지 위로 보이는 하늘색 (배경 맨 위 픽셀 색)")]
        public Color skyColor = new Color(194f / 255f, 226f / 255f, 248f / 255f);
        [Tooltip("배경 이미지의 풀밭 시작 높이(월드 y)")]
        public float groundTopY = -2.18f;
        [Tooltip("카피바라 발 위치(월드 y)")]
        public float capybaraFeetY = -2.4f;
        [Tooltip("발 기준 정수리 높이 (capybara_idle: (968-190)px / 197ppu)")]
        public float capybaraHeadTop = 3.93f;
        [Tooltip("받침 이미지에서 실제 잎이 그려진 폭 (head_pad_leaf: 487px / 375ppu)")]
        public float plateSpriteVisibleWidth = 1.30f;
        public float plateSpriteLocalY = -0.04f;
        public Vector2 blinkInterval = new Vector2(2.5f, 5f);
        public float blinkDuration = 0.15f;

        [Header("받침 · 가이드")]
        [Tooltip("머리 위 받침(물리 발판) 폭")]
        public float plateWidth = 1.5f;
        public bool showColumnGuide = true;
        public bool showAimLine = true;

        [Header("왕복")]
        public float swingHalfWidth = 3.2f;
        public float swingSpeedBase = 2.6f;          // 유닛/초
        public float swingSpeedPerMandarin = 0.12f;
        public float swingSpeedMax = 7f;
        [Tooltip("탑 꼭대기에서 왕복하는 귤 아래쪽까지의 높이")]
        public float swingHeightAboveTop = 1.6f;

        [Header("물리")]
        public float gravityScale = 1.5f;
        [Tooltip("놓는 순간 아래로 주는 초기 속도")]
        public float dropInitialSpeed = 0f;
        [Tooltip("낙하 속도 상한 (너무 세게 부딪혀 탑이 튕기지 않게)")]
        public float maxFallSpeed = 3.5f;
        public float friction = 1f;
        public float angularDamping = 12f;
        public float linearDamping = 0.6f;
        [Tooltip("귤 콜라이더: 둥근 사각형. 지름 1 기준 안쪽 사각형 크기")]
        public Vector2 mandarinBoxCore = new Vector2(0.8f, 0.66f);
        [Tooltip("귤 콜라이더 모서리 반경 (지름 1 기준). 전체 폭 = core.x + 2r")]
        public float mandarinBoxEdgeRadius = 0.1f;
        [Tooltip("받침이나 귤에 닿은 뒤 이 시간이 지나거나 거의 멈추면 '쌓임'으로 인정")]
        public float settleTime = 0.25f;
        public float settleSpeed = 0.35f;
        [Tooltip("받침 높이보다 이만큼 아래로 내려간 귤이 있으면 게임 오버")]
        public float failDepth = 0.6f;

        [Header("귤 사이 탄성 관절 (탑이 휘청이며 휘어지고, 많이 쏠리면 끊어짐)")]
        public bool useJoints = false; // 시뮬레이션 비교 결과 개선이 없어 기본 끔 (2026-11-06)
        [Tooltip("관절 탄성 주파수(Hz). 낮을수록 더 흐물흐물 휨")]
        public float jointFrequency = 12f;
        public float jointDamping = 1f;
        [Tooltip("이 토크를 넘으면 관절이 끊어짐. 낮을수록 어려움")]
        public float jointBreakTorque = 25f;

        [Header("받침 흔들림 (쌓을수록 커짐)")]
        public float plateSwayPerMandarin = 0.004f; // 유닛
        public float plateSwayMax = 0.04f;
        public float plateSwayFreqBase = 0.15f;      // Hz
        public float plateSwayFreqPerMandarin = 0.005f;
        public float plateSwayFreqMax = 0.3f;

        [Header("점수")]
        public int scorePerMandarin = 100;
        [Tooltip("착지 순간 아래 귤과의 x 차이가 지름 x 이 비율 이하이면 정중앙 판정")]
        public float perfectRatio = 0.12f;
        public int perfectBonusPerCombo = 50;

        [Header("카메라")]
        public float cameraSize = 7f;
        public float cameraFollowLerp = 4f;

        [Header("임시 도형 색")]
        public Color mandarinColor = new Color(1f, 0.55f, 0.1f);
        public Color capybaraColor = new Color(0.62f, 0.45f, 0.3f);

        // ---- 상태 ----
        public GameState State { get; private set; } = GameState.Ready;
        public int Score { get; private set; }
        public int Count => _stack.Count;
        public int Combo { get; private set; }
        public string GameOverReason { get; private set; } = "";
        // IRoundGame (GameFlow 표시용)
        public string HudSub => "귤 " + Count + "개    콤보 " + Combo;
        public string ResultTitle => "게임 오버";
        public string ResultSub => "귤 " + Count + "개";

        /// <summary>착지할 때마다 (판정 문구, 정중앙 여부)</summary>
        public event System.Action<string, bool> Judged;
        /// <summary>게임 오버 순간</summary>
        public event System.Action GameEnded;

        int _beginFrame = -1;

        Camera _cam;
        Rigidbody2D _plateBody;        // 머리 위 받침(키네마틱). 좌우로 흔들림
        Collider2D _plateCollider;
        Collider2D _groundCollider;
        Vector2 _plateHome;
        readonly List<Rigidbody2D> _stack = new List<Rigidbody2D>();
        readonly HashSet<Collider2D> _stackColliders = new HashSet<Collider2D>();
        Rigidbody2D _current;
        Collider2D _currentCollider;
        Rigidbody2D _support;          // 낙하 중인 귤을 받치고 있는 몸체(받침 또는 귤)
        float _swingX;
        int _swingDir = 1;
        float _swayPhase;
        float _touchTime = -1f;
        float _camBaseY;
        PhysicsMaterial2D _material;
        readonly ContactPoint2D[] _contacts = new ContactPoint2D[16];

        float Diameter => mandarinRadius * 2f;
        float PlateTopY => _plateHome.y;

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
            _cam.backgroundColor = skyColor;
            _camBaseY = 1.5f;
            _cam.transform.position = new Vector3(0f, _camBaseY, -10f);

            // 쌓기 안정성: 반복 횟수를 늘리고 고정 시간 간격을 줄임
            Physics2D.velocityIterations = 30;
            Physics2D.positionIterations = 15;
            Time.fixedDeltaTime = 0.01f;

            _material = new PhysicsMaterial2D("Mandarin") { friction = friction, bounciness = 0f };

            BuildWorld();
        }

        // =====================================================================
        // 월드 구성
        // =====================================================================
        bool UseArt => capybaraIdleSprite != null;

        void BuildWorld()
        {
            if (backgroundSprite != null)
            {
                var bg = new GameObject("Background").AddComponent<SpriteRenderer>();
                bg.sprite = backgroundSprite;
                bg.sortingOrder = -100;
                bg.transform.position = new Vector3(0f, _camBaseY, 0f);
            }
            else
            {
                var g = SpriteFactory.Create("GroundVisual", SpriteFactory.Square, new Color(0.45f, 0.7f, 0.4f), new Vector2(60f, 6f), -100);
                g.transform.position = new Vector3(0f, groundTopY - 3f, 0f);
            }

            // 바닥 충돌
            var ground = new GameObject("GroundCollider");
            ground.transform.position = new Vector3(0f, groundTopY - 2f, 0f);
            var gcol = ground.AddComponent<BoxCollider2D>();
            gcol.size = new Vector2(60f, 4f);
            gcol.sharedMaterial = _material;
            _groundCollider = gcol;

            // 카피바라 (그림만. 충돌은 머리 위 받침만 담당 → 받침을 벗어난 귤은 바닥까지 떨어짐)
            float headTopY;
            if (UseArt)
            {
                var capy = new GameObject("Capybara");
                capy.transform.position = new Vector3(0f, capybaraFeetY, 0f);
                _capyRenderer = capy.AddComponent<SpriteRenderer>();
                _capyRenderer.sprite = capybaraIdleSprite;
                _capyRenderer.sortingOrder = 10;
                headTopY = capybaraFeetY + capybaraHeadTop;
                ScheduleBlink();
            }
            else
            {
                var body = SpriteFactory.Create("Capybara", SpriteFactory.Square, capybaraColor, new Vector2(3f, 3.6f), 10);
                body.transform.position = new Vector3(0f, groundTopY + 1.8f, 0f);
                headTopY = groundTopY + 3.6f;
            }

            // 머리 위 받침: 키네마틱 발판. 위쪽 면이 headTopY
            var plateGo = new GameObject("Plate");
            plateGo.transform.position = new Vector3(0f, headTopY, 0f);
            _plateHome = plateGo.transform.position;
            _plateBody = plateGo.AddComponent<Rigidbody2D>();
            _plateBody.bodyType = RigidbodyType2D.Kinematic;
            _plateBody.interpolation = RigidbodyInterpolation2D.Interpolate;
            var pcol = plateGo.AddComponent<BoxCollider2D>();
            pcol.size = new Vector2(plateWidth, 0.2f);
            pcol.offset = new Vector2(0f, -0.1f);
            pcol.sharedMaterial = _material;
            _plateCollider = pcol;

            if (plateSprite != null)
            {
                var leaf = new GameObject("PlateLeaf").AddComponent<SpriteRenderer>();
                leaf.transform.SetParent(plateGo.transform, false);
                leaf.sprite = plateSprite;
                leaf.sortingOrder = 15;
                float s = plateWidth / plateSpriteVisibleWidth;
                leaf.transform.localScale = new Vector3(s, s, 1f);
                leaf.transform.localPosition = new Vector3(0f, plateSpriteLocalY, 0f);
            }
            else
            {
                var leaf = SpriteFactory.Create("PlateLeaf", SpriteFactory.Square, new Color(1f, 0.95f, 0.8f), new Vector2(plateWidth, 0.14f), 15, plateGo.transform);
                leaf.transform.localPosition = new Vector3(0f, -0.07f, 0f);
            }

            // 세로 가이드: "일자로 쌓으세요"를 화면으로 보여줌 (받침과 같이 흔들림)
            if (showColumnGuide)
            {
                var guide = SpriteFactory.Create("ColumnGuide", SpriteFactory.Square, new Color(1f, 1f, 1f, 0.35f), new Vector2(0.06f, 40f), 5, plateGo.transform);
                guide.transform.localPosition = new Vector3(0f, 20f, 0f);
                for (int s = -1; s <= 1; s += 2)
                {
                    var edge = SpriteFactory.Create("ColumnEdge", SpriteFactory.Square, new Color(1f, 1f, 1f, 0.15f), new Vector2(0.04f, 40f), 5, plateGo.transform);
                    edge.transform.localPosition = new Vector3(s * plateWidth * 0.5f, 20f, 0f);
                }
            }

            if (showAimLine)
            {
                _aimLine = SpriteFactory.Create("AimLine", SpriteFactory.Square, new Color(1f, 0.55f, 0.1f, 0.35f), new Vector2(0.05f, 1f), 18);
                _aimLine.enabled = false;
            }
        }

        // =====================================================================
        // 입력 · 상태 갱신
        // =====================================================================
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

            // 시작 버튼을 누른 그 프레임에 바로 떨어지지 않도록 시작 프레임 입력은 무시
            if (Time.frameCount == _beginFrame) pressed = false;

            if (State == GameState.Swinging)
            {
                UpdateSwing(dt);
                if (pressed) Release();
            }

            UpdateAimLine();
            UpdateCapybaraFace();
            UpdateCamera(dt);
        }

        void FixedUpdate()
        {
            StepPhysics(Time.fixedDeltaTime);
        }

        /// <summary>
        /// 물리 한 스텝 뒤의 게임 판정. (테스트에서 Physics2D.Simulate 다음에 직접 호출 가능)
        /// </summary>
        public void StepPhysics(float dt)
        {
            UpdatePlateSway(dt);

            if (State == GameState.Dropping && _current != null)
            {
                // 낙하 속도 상한
                var v = _current.linearVelocity;
                if (v.y < -maxFallSpeed) { v.y = -maxFallSpeed; _current.linearVelocity = v; }

                int n = _current.GetContacts(_contacts);
                bool onStack = false, onGround = false;
                _support = null;
                for (int i = 0; i < n; i++)
                {
                    // 자기 자신이 아닌 쪽 콜라이더를 고름
                    var hit = _contacts[i].collider == _currentCollider ? _contacts[i].otherCollider : _contacts[i].collider;
                    if (hit == _plateCollider || _stackColliders.Contains(hit))
                    {
                        onStack = true;
                        // 아래에서 받치는 것(가장 높은 쪽)을 관절 대상으로
                        var body = hit.attachedRigidbody;
                        if (body != null && body.position.y < _current.position.y &&
                            (_support == null || body.position.y > _support.position.y))
                            _support = body;
                    }
                    else if (hit == _groundCollider) onGround = true;
                }

                if (onGround || _current.position.y < PlateTopY - failDepth)
                {
                    GameOver("귤이 떨어졌습니다");
                    return;
                }

                if (onStack)
                {
                    if (_touchTime < 0f) _touchTime = 0f;
                    _touchTime += dt;
                    if (_touchTime >= settleTime || _current.linearVelocity.magnitude < settleSpeed)
                        Land();
                }
            }

            if (State == GameState.Swinging || State == GameState.Dropping)
            {
                // 이미 쌓인 귤이 받침 아래로 내려가면 무너진 것
                for (int i = 0; i < _stack.Count; i++)
                {
                    if (_stack[i].position.y < PlateTopY - failDepth)
                    {
                        GameOver("탑이 무너졌습니다");
                        return;
                    }
                }
            }
        }

        void UpdatePlateSway(float dt)
        {
            if (_plateBody == null) return;
            if (State == GameState.GameOver) return;
            int n = _stack.Count;
            float amp = Mathf.Min(plateSwayMax, plateSwayPerMandarin * n);
            float freq = Mathf.Min(plateSwayFreqMax, plateSwayFreqBase + plateSwayFreqPerMandarin * n);
            _swayPhase += Mathf.PI * 2f * freq * dt;
            _plateBody.MovePosition(_plateHome + new Vector2(amp * Mathf.Sin(_swayPhase), 0f));
        }

        // =====================================================================
        // 귤
        // =====================================================================
        float TopY()
        {
            float top = PlateTopY;
            for (int i = 0; i < _stack.Count; i++)
                top = Mathf.Max(top, _stack[i].position.y + mandarinRadius);
            return top;
        }

        Vector2 TopTarget()
        {
            // 정중앙 판정 기준: 가장 높은 귤, 없으면 받침 중심
            Rigidbody2D best = null;
            for (int i = 0; i < _stack.Count; i++)
                if (best == null || _stack[i].position.y > best.position.y) best = _stack[i];
            return best != null ? best.position : _plateBody.position;
        }

        void SpawnMandarin()
        {
            int order = 20 + _stack.Count; // 위에 쌓일수록 앞에 그려 아래 귤의 잎을 가림
            SpriteRenderer sr;
            if (mandarinSprites != null && mandarinSprites.Length > 0)
            {
                // 귤 스프라이트는 몸통 원 지름이 1유닛, 기준점이 몸통 중심 (ppu 358)
                sr = SpriteFactory.Create("Mandarin" + (_stack.Count + 1),
                    mandarinSprites[Random.Range(0, mandarinSprites.Length)], Color.white,
                    new Vector2(Diameter, Diameter), order);
            }
            else
            {
                sr = SpriteFactory.Create("Mandarin" + (_stack.Count + 1), SpriteFactory.Circle, mandarinColor,
                    new Vector2(Diameter, Diameter), order);
            }

            // 원은 서로 굴러 떨어져 쌓이지 않으므로 위아래가 평평한 둥근 사각형 콜라이더 사용
            var col = sr.gameObject.AddComponent<BoxCollider2D>();
            col.size = mandarinBoxCore;
            col.edgeRadius = mandarinBoxEdgeRadius;
            col.sharedMaterial = _material;

            var rb = sr.gameObject.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;   // 왕복 중엔 충돌 없이 이동
            rb.simulated = false;
            rb.gravityScale = gravityScale;
            rb.linearDamping = linearDamping;
            rb.angularDamping = angularDamping;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            _current = rb;
            _currentCollider = col;
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
            float y = TopY() + swingHeightAboveTop + mandarinRadius;
            _current.transform.position = new Vector3(_swingX, y, 0f);
        }

        void Release()
        {
            _current.transform.rotation = Quaternion.identity;
            _current.bodyType = RigidbodyType2D.Dynamic;
            _current.simulated = true;
            _current.position = _current.transform.position;
            _current.rotation = 0f;
            _current.linearVelocity = new Vector2(0f, -dropInitialSpeed);
            _touchTime = -1f;
            State = GameState.Dropping;
        }

        void Land()
        {
            Vector2 target = TopTarget();
            float dx = _current.position.x - target.x;
            bool perfect = Mathf.Abs(dx) <= Diameter * perfectRatio;
            if (perfect) Combo++; else Combo = 0;

            if (useJoints && _support != null)
            {
                var j = _current.gameObject.AddComponent<FixedJoint2D>();
                j.connectedBody = _support;
                j.frequency = jointFrequency;
                j.dampingRatio = jointDamping;
                j.breakForce = Mathf.Infinity;
                j.breakTorque = jointBreakTorque;
                j.enableCollision = true;
            }

            _stack.Add(_current);
            _stackColliders.Add(_currentCollider);
            _current = null;
            _currentCollider = null;

            int gained = scorePerMandarin + (perfect ? perfectBonusPerCombo * Combo : 0);
            Score += gained;
            string judge = perfect ? ("PERFECT x" + Combo + "  +" + gained) : ("+" + gained);
            Judged?.Invoke(judge, perfect);

            SpawnMandarin();
        }

        void GameOver(string reason)
        {
            State = GameState.GameOver;
            GameOverReason = reason;
            Combo = 0;
            if (_plateBody != null) _plateBody.linearVelocity = Vector2.zero;
            GameEnded?.Invoke();
            // 왕복 중이던 귤이 있으면 그대로 떨어뜨림
            if (_current != null && !_current.simulated)
            {
                _current.bodyType = RigidbodyType2D.Dynamic;
                _current.simulated = true;
                _current.position = _current.transform.position;
            }
        }

        /// <summary>새 판 시작: 초기화 후 첫 귤을 왕복시킴</summary>
        public void BeginRound()
        {
            ResetRound();
            SpawnMandarin();
            _beginFrame = Time.frameCount;
        }

        /// <summary>
        /// 씬을 다시 불러오지 않고 판을 초기화. (재시작, 타이틀 복귀, 수치 조정 테스트에 사용)
        /// </summary>
        public void ResetRound()
        {
            // 쌓인 귤, 왕복 중인 귤, 게임 오버 후 바닥에 떨어진 귤 모두 정리
            foreach (var r in FindObjectsByType<Rigidbody2D>(FindObjectsSortMode.None))
                if (r != _plateBody && r.name.StartsWith("Mandarin")) DestroyImmediate(r.gameObject);
            _stack.Clear();
            _stackColliders.Clear();
            _current = null;
            _currentCollider = null;
            _support = null;
            _swayPhase = 0f;
            if (_plateBody != null)
            {
                _plateBody.position = _plateHome;
                _plateBody.transform.position = _plateHome;
            }
            Score = 0;
            Combo = 0;
            GameOverReason = "";
            _cam.transform.position = new Vector3(0f, _camBaseY, -10f);
            State = GameState.Ready;
        }

        // =====================================================================
        // 연출
        // =====================================================================
        SpriteRenderer _aimLine;
        SpriteRenderer _capyRenderer;
        float _nextBlinkTime;
        float _blinkEndTime;

        void UpdateAimLine()
        {
            if (_aimLine == null) return;
            bool on = State == GameState.Swinging && _current != null;
            _aimLine.enabled = on;
            if (!on) return;
            float top = _current.transform.position.y - mandarinRadius;
            float bottom = TopY();
            float len = Mathf.Max(0.01f, top - bottom);
            _aimLine.transform.position = new Vector3(_current.transform.position.x, (top + bottom) * 0.5f, 0f);
            _aimLine.transform.localScale = new Vector3(0.05f, len, 1f);
        }

        void ScheduleBlink()
        {
            _nextBlinkTime = Time.time + Random.Range(blinkInterval.x, blinkInterval.y);
        }

        void UpdateCapybaraFace()
        {
            if (_capyRenderer == null || capybaraEyesClosedSprite == null) return;
            if (State == GameState.GameOver)
            {
                // 탑이 무너져도 태연하게 눈을 감고 있음
                _capyRenderer.sprite = capybaraEyesClosedSprite;
                return;
            }
            if (Time.time >= _nextBlinkTime)
            {
                _blinkEndTime = Time.time + blinkDuration;
                ScheduleBlink();
            }
            _capyRenderer.sprite = Time.time < _blinkEndTime ? capybaraEyesClosedSprite : capybaraIdleSprite;
        }

        void UpdateCamera(float dt)
        {
            if (State == GameState.GameOver) return;
            float targetY = Mathf.Max(_camBaseY, TopY() + swingHeightAboveTop + Diameter - cameraSize * 0.45f);
            var cp = _cam.transform.position;
            cp.y = Mathf.Lerp(cp.y, targetY, 1f - Mathf.Exp(-cameraFollowLerp * dt));
            _cam.transform.position = cp;
        }
    }
}
