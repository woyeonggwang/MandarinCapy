using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace MandarinCapy
{
    public enum FlowState { Title, NameEntry, HowTo, Playing, Result, Ranking, Story }

    /// <summary>
    /// 화면 흐름: 처음 화면 → 닉네임 입력 → 조작 안내 → 플레이 → 결과 → 랭킹 TOP 10 → (15초 입력 없으면) 처음 화면.
    /// UI는 코드로 생성 (Canvas 1920x1080 기준). Ctrl+Shift+R: 랭킹 초기화(부스 운영자용).
    /// </summary>
    public class GameFlow : MonoBehaviour
    {
        [Header("참조")]
        [Tooltip("IRoundGame을 구현한 게임 (StackGame 또는 DownhillGame). 비어 있으면 씬에서 찾음")]
        [FormerlySerializedAs("game")] public MonoBehaviour gameBehaviour;
        IRoundGame game;

        [Header("문구 (게임별)")]
        public string titleText = "카피바라 귤 쌓기";
        [Tooltip("제목 아래 이야기 한 줄 (비우면 표시 안 함)")]
        public string subtitleText = "";
        public string countLabel = "귤";
        [Tooltip("랭킹 파일 이름 (persistentDataPath 아래). 게임마다 따로 둠")]
        public string rankingFileName = "ranking.json";
        public bool showSpaceKey = true;
        public string howToLine1 = "좌우로 움직이는 귤을 눌러서 떨어뜨리세요";
        public string howToLine2 = "카피바라 머리 위 잎에서 시작해 일자로 쌓으세요";
        public string howToLine3 = "가운데에 맞출수록 콤보 보너스!  ·  귤이 떨어지면 끝";
        public string titlePromptText = "스페이스바 또는 마우스 클릭으로 시작";
        public TMP_FontAsset font;
        public TMP_FontAsset fontBold;
        [Tooltip("글자 외곽선 색")]
        public Color outlineColor = Color.white;
        public Sprite panelSprite;
        public Sprite medalGold, medalSilver, medalBronze;
        public Sprite iconSettings, iconStory;   // 처음 화면 설정·스토리 버튼 아이콘 (랭킹 버튼 메달과 같은 그림체)
        public Sprite iconMinus, iconPlus;       // 설정 볼륨 -/+ (글자는 글꼴마다 위치가 달라 가운데 정렬이 어긋나서 이미지로)

        // 버튼은 패널 9-slice 모서리(48px)를 절반 크기로 그려 작은 버튼도 둥근 사각형으로 (1이면 작은 버튼이 마름모처럼 보임)
        const float ButtonPPU = 2f;
        public Sprite keySpaceSprite, mouseLeftSprite;

        [System.Serializable]
        public class StoryPanel
        {
            public Sprite image;            // 웹툰 컷 그림 (없으면 '그림 준비 중' 자리표시)
            [TextArea(2, 4)] public string text;  // 아래 자막 (대사·내레이션)
        }

        [Header("스토리 (처음 화면 → 스토리 → 닉네임)")]
        public bool showStory = true;
        public StoryPanel[] story;

        [Header("규칙")]
        public int nameMaxLength = 8;
        [Tooltip("랭킹 화면에서 입력이 없으면 처음 화면으로 돌아가는 시간(초)")]
        public float rankingIdleSeconds = 15f;
        [Tooltip("닉네임 입력·조작 안내 화면에서 입력이 없으면 처음 화면으로 돌아가는 시간(초)")]
        public float setupIdleSeconds = 30f;
        [Tooltip("게임 오버 후 결과 화면이 뜨기까지(무너지는 연출 감상)")]
        public float resultDelay = 1.5f;
        [Tooltip("결과 화면에서 자동으로 랭킹 화면으로 넘어가는 시간")]
        public float resultAutoSeconds = 6f;

        [Header("색")]
        public Color textColor = new Color(0.29f, 0.17f, 0.1f);
        public Color accentColor = new Color(0.93f, 0.45f, 0.08f);
        public Color highlightRow = new Color(1f, 0.8f, 0.45f, 0.9f);

        public FlowState State { get; private set; } = FlowState.Title;

        // 닉네임: 완성형 한글, 영문, 숫자 (공백 불가)
        static readonly Regex NameRegex = new Regex("^[가-힣A-Za-z0-9]+$");

        RectTransform _canvas;
        GameObject _dim, _title, _name, _howTo, _hud, _result, _ranking, _toast;
        TextMeshProUGUI _titleBest, _titlePrompt, _nameError, _hudScore, _hudSub, _hudName, _judge;
        TextMeshProUGUI _resultTitle, _resultReason, _resultScore, _resultSub, _resultRank, _rankingFooter, _toastText;
        TMP_InputField _nameInput;
        RectTransform _rankRows;

        string _nickname = "";
        int _lastRank = -1;
        float _stateTime;
        float _lastInputTime;
        float _judgeTime = -10f;
        float _gameOverTime = -1f;
        float _toastUntil;
        bool _submitPending;
        RectTransform _rankBtnRect;   // 처음 화면 오른쪽 아래 '랭킹 보기' 버튼
        bool _rankPopup;              // 처음 화면에서 랭킹 팝업을 띄운 상태
        float _popupTime;
        RectTransform _setBtnRect;    // 처음 화면 오른쪽 아래 버튼 줄: 설정(맨 위)
        RectTransform _storyBtnRect;  //                         스토리 보기(가운데), 랭킹 보기(맨 아래)
        GameObject _settings;
        bool _settingsOpen;
        TextMeshProUGUI _setBgm, _setSfx, _setScreen;
        GameObject _story;
        Image _storyImage;
        TextMeshProUGUI _storyText, _storyPage, _storyPlaceholder;
        RectTransform _storySkipRect;
        int _storyIndex;

        // =====================================================================
        void Start()
        {
            game = gameBehaviour as IRoundGame;
            if (game == null)
            {
                foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
                    if (mb is IRoundGame rg) { game = rg; gameBehaviour = mb; break; }
            }
            game.Judged += OnJudged;
            game.GameEnded += OnGameEnded;
            RankingStore.UseFile(rankingFileName);
            RankingStore.Load();
            BuildUI();
            Go(FlowState.Title);
        }

        void OnDestroy()
        {
            Cursor.visible = true;
            if (game != null) { game.Judged -= OnJudged; game.GameEnded -= OnGameEnded; }
        }

        // =====================================================================
        // 입력
        // =====================================================================
        static bool PressedAny()
        {
            var k = Keyboard.current; var m = Mouse.current;
            return (k != null && (k.spaceKey.wasPressedThisFrame || k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame)) ||
                   (m != null && m.leftButton.wasPressedThisFrame);
        }

        static bool AnyActivity()
        {
            var k = Keyboard.current; var m = Mouse.current;
            return (k != null && k.anyKey.wasPressedThisFrame) ||
                   (m != null && (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame));
        }

        void Update()
        {
            float now = Time.unscaledTime;
            if (AnyActivity()) _lastInputTime = now;
            float idle = now - _lastInputTime;
            float inState = now - _stateTime;

            // 부스 운영자용 숨은 단축키: Ctrl+Shift+R 랭킹 초기화
            var k = Keyboard.current;
            if (k != null && k.rKey.wasPressedThisFrame && k.ctrlKey.isPressed && k.shiftKey.isPressed)
            {
                RankingStore.Clear();
                _lastRank = -1;
                ShowToast("랭킹이 초기화되었습니다");
                RefreshTitleBest();
                if (State == FlowState.Ranking) FillRanking();
            }

            bool pressed = PressedAny() && inState > 0.25f; // 화면 전환 직후의 같은 입력은 무시

            switch (State)
            {
                case FlowState.Title:
                    if (_settingsOpen)
                    {
                        if (k != null && k.escapeKey.wasPressedThisFrame) CloseSettings();
                        break;
                    }
                    if (_rankPopup)
                    {
                        int pl = Mathf.CeilToInt(rankingIdleSeconds - idle);
                        _rankingFooter.text = "누르면 닫기  (" + Mathf.Max(0, pl) + "초 후 자동)";
                        bool esc = k != null && k.escapeKey.wasPressedThisFrame;
                        if ((PressedAny() && now - _popupTime > 0.3f) || esc || idle > rankingIdleSeconds) CloseRankingPopup();
                        break;
                    }
                    _titlePrompt.color = WithAlpha(_titlePrompt.color, 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(now * 2.5f)));
                    // 랭킹 버튼 위를 누른 클릭은 게임 시작으로 보지 않음 (버튼은 떼는 순간 동작)
                    if (pressed && !MouseOverRankButton()) Go(FlowState.NameEntry); // 스토리는 '스토리 보기' 버튼으로만
                    break;

                case FlowState.Story:
                    if (k != null && k.escapeKey.wasPressedThisFrame) EndStory();
                    else if (pressed && !MouseOver(_storySkipRect)) NextStory();
                    else if (idle > setupIdleSeconds) Go(FlowState.Title);
                    break;

                case FlowState.NameEntry:
                    UpdateNameEntry();
                    if (k != null && k.escapeKey.wasPressedThisFrame) Go(FlowState.Title);
                    else if (idle > setupIdleSeconds) Go(FlowState.Title);
                    break;

                case FlowState.HowTo:
                    if (pressed) Go(FlowState.Playing);
                    else if (idle > setupIdleSeconds) Go(FlowState.Title);
                    break;

                case FlowState.Playing:
                    // 커서: 플레이 중에는 숨김. 우클릭·Esc로 보이게, 다시 좌클릭하면 숨김
                    {
                        var mm = Mouse.current;
                        if ((k != null && k.escapeKey.wasPressedThisFrame) || (mm != null && mm.rightButton.wasPressedThisFrame)) Cursor.visible = true;
                        else if (mm != null && mm.leftButton.wasPressedThisFrame && Cursor.visible) Cursor.visible = false;
                    }
                    _hudScore.text = "점수 " + game.Score.ToString("N0");
                    _hudSub.text = game.HudSub;
                    float ja = Mathf.Clamp01(1f - (now - _judgeTime) / 0.9f);
                    _judge.color = WithAlpha(_judge.color, ja);
                    _judge.rectTransform.anchoredPosition = new Vector2(0f, 300f + (1f - ja) * 40f);
                    if (_gameOverTime > 0f && now - _gameOverTime > resultDelay) ShowResult();
                    break;

                case FlowState.Result:
                    if ((pressed && inState > 0.8f) || inState > resultAutoSeconds) Go(FlowState.Ranking);
                    break;

                case FlowState.Ranking:
                    int left = Mathf.CeilToInt(rankingIdleSeconds - idle);
                    _rankingFooter.text = "누르면 처음 화면으로  (" + Mathf.Max(0, left) + "초 후 자동)";
                    if ((pressed && inState > 0.8f) || idle > rankingIdleSeconds) Go(FlowState.Title);
                    break;
            }

            if (_toast.activeSelf && now > _toastUntil) _toast.SetActive(false);
        }

        void UpdateNameEntry()
        {
            if (!_nameInput.isFocused) _nameInput.ActivateInputField();

            var k = Keyboard.current;
            if (_submitPending)
            {
                // 한글 조합 중 Enter는 먼저 조합을 확정하므로 한 프레임 뒤에 읽음
                _submitPending = false;
                TrySubmitName();
                return;
            }
            if (k != null && (k.enterKey.wasPressedThisFrame || k.numpadEnterKey.wasPressedThisFrame))
                _submitPending = true;
        }

        void TrySubmitName()
        {
            string n = (_nameInput.text ?? "").Trim();
            if (n.Length == 0) { _nameError.text = "닉네임을 입력해 주세요"; ReFocus(); return; }
            if (n.Length > nameMaxLength) { _nameError.text = "최대 " + nameMaxLength + "자까지 입력할 수 있습니다"; ReFocus(); return; }
            if (!NameRegex.IsMatch(n)) { _nameError.text = "한글(완성된 글자)·영문·숫자만 쓸 수 있습니다 (공백 불가)"; ReFocus(); return; }
            _nickname = n;
            Go(FlowState.HowTo);
        }

        void ReFocus()
        {
            _nameInput.ActivateInputField();
        }

        char ValidateNameChar(string text, int index, char c)
        {
            // 공백 금지, 허용 문자: 한글(완성형·자모 - 조합 중), 영문, 숫자
            if (char.IsWhiteSpace(c)) return '\0';
            if ((c >= '가' && c <= '힣') || (c >= 'ㄱ' && c <= 'ㅣ') ||
                (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                return c;
            return '\0';
        }

        // =====================================================================
        // 상태 전환
        // =====================================================================
        void Go(FlowState s)
        {
            State = s;
            _stateTime = Time.unscaledTime;
            _lastInputTime = Time.unscaledTime;

            _title.SetActive(s == FlowState.Title);
            _name.SetActive(s == FlowState.NameEntry);
            _howTo.SetActive(s == FlowState.HowTo);
            _hud.SetActive(s == FlowState.Playing);
            _result.SetActive(s == FlowState.Result);
            _ranking.SetActive(s == FlowState.Ranking);
            _story.SetActive(s == FlowState.Story);
            if (_settingsOpen) CloseSettings();
            _dim.SetActive(s == FlowState.NameEntry || s == FlowState.HowTo || s == FlowState.Result || s == FlowState.Ranking);
            Cursor.visible = s != FlowState.Playing;   // 플레이 시작 시 커서 숨김, 다른 화면에서는 항상 보임
            Cursor.lockState = CursorLockMode.None;    // 마우스 위치로 팔을 움직이므로 잠그지 않음

            switch (s)
            {
                case FlowState.Title:
                    _rankPopup = false;
                    game.ResetRound();
                    _nickname = "";
                    _lastRank = -1;
                    RefreshTitleBest();
                    break;
                case FlowState.NameEntry:
                    _nameInput.text = "";
                    _nameError.text = "";
                    _submitPending = false;
                    _nameInput.ActivateInputField();
                    break;
                case FlowState.HowTo:
                    EventSystem.current.SetSelectedGameObject(null);
                    break;
                case FlowState.Playing:
                    _gameOverTime = -1f;
                    _judgeTime = -10f;
                    _hudName.text = _nickname;
                    game.BeginRound();
                    break;
                case FlowState.Ranking:
                    FillRanking();
                    break;
                case FlowState.Story:
                    _storyIndex = 0;
                    ShowStoryPage();
                    break;
            }
        }

        void OnJudged(string text, bool perfect)
        {
            _judge.text = text;
            _judge.color = perfect ? accentColor : textColor;
            _judge.fontSize = perfect ? 72 : 60;
            _judgeTime = Time.unscaledTime;
        }

        void OnGameEnded()
        {
            if (State != FlowState.Playing) return;
            _gameOverTime = Time.unscaledTime;
            _lastRank = RankingStore.Add(_nickname, game.Score, game.Count);
        }

        void ShowResult()
        {
            _resultTitle.text = game.ResultTitle;
            _resultReason.text = game.GameOverReason;
            _resultScore.text = game.Score.ToString("N0") + "점";
            _resultSub.text = game.ResultSub;
            _resultRank.text = _lastRank > 0
                ? _nickname + " 님은  " + _lastRank + "위  (전체 " + RankingStore.Entries.Count + "개 기록)"
                : "";
            Go(FlowState.Result);
        }

        void RefreshTitleBest()
        {
            var best = RankingStore.Get(1);
            _titleBest.text = best != null ? "최고 기록  " + best.name + "  " + best.score.ToString("N0") + "점" : "첫 번째 기록에 도전하세요!";
        }

        // 처음 화면 버튼(랭킹·설정) 위를 누른 클릭은 게임 시작으로 보지 않음
        bool MouseOverRankButton()
        {
            return MouseOver(_rankBtnRect) || MouseOver(_setBtnRect) || MouseOver(_storyBtnRect);
        }

        bool MouseOver(RectTransform r)
        {
            var m = Mouse.current;
            if (r == null || m == null || !r.gameObject.activeInHierarchy || !m.leftButton.wasPressedThisFrame) return false;
            return RectTransformUtility.RectangleContainsScreenPoint(r, m.position.ReadValue(), null);
        }

        // ---------------- 스토리 ----------------
        void ShowStoryPage()
        {
            var p = story[_storyIndex];
            bool hasImg = p != null && p.image != null;
            _storyImage.sprite = hasImg ? p.image : null;
            _storyImage.color = hasImg ? Color.white : new Color(0.98f, 0.9f, 0.72f);
            _storyImage.preserveAspect = hasImg;
            _storyPlaceholder.gameObject.SetActive(!hasImg);
            _storyPlaceholder.text = "그림 준비 중  ·  " + (_storyIndex + 1) + "컷";
            _storyText.text = p != null ? p.text : "";
            _storyPage.text = (_storyIndex + 1) + " / " + story.Length + "   클릭하면 다음";
        }

        void NextStory()
        {
            if (_storyIndex + 1 >= story.Length) { EndStory(); return; }
            _storyIndex++;
            _stateTime = Time.unscaledTime;
            ShowStoryPage();
        }

        void EndStory()
        {
            if (State == FlowState.Story) Go(FlowState.Title);
        }

        // ---------------- 설정 ----------------
        void OpenSettings()
        {
            if (State != FlowState.Title || _rankPopup || _settingsOpen) return;
            _settingsOpen = true;
            _settings.SetActive(true);
            RefreshSettings();
        }

        void CloseSettings()
        {
            _settingsOpen = false;
            _settings.SetActive(false);
            _stateTime = Time.unscaledTime; // 닫는 클릭이 곧바로 게임 시작으로 이어지지 않게
        }

        void ChangeVolume(bool bgm, float delta)
        {
            if (bgm) GameSound.BgmMul = Mathf.Clamp01(Mathf.Round((GameSound.BgmMul + delta) * 10f) / 10f);
            else
            {
                GameSound.SfxMul = Mathf.Clamp01(Mathf.Round((GameSound.SfxMul + delta) * 10f) / 10f);
                if (GameSound.I != null) GameSound.I.Fruit(); // 효과음 크기 미리 듣기
            }
            GameSound.SaveVolumes();
            RefreshSettings();
        }

        void ToggleScreen()
        {
            bool full = !Screen.fullScreen;
            if (full) Screen.SetResolution(Screen.currentResolution.width, Screen.currentResolution.height, FullScreenMode.FullScreenWindow);
            else Screen.SetResolution(1600, 900, FullScreenMode.Windowed);
            _setScreen.text = full ? "전체 화면" : "창 모드";
        }

        void RefreshSettings()
        {
            _setBgm.text = Mathf.RoundToInt(GameSound.BgmMul * 100f) + "%";
            _setSfx.text = Mathf.RoundToInt(GameSound.SfxMul * 100f) + "%";
            _setScreen.text = Screen.fullScreen ? "전체 화면" : "창 모드";
        }

        void OpenRankingPopup()
        {
            if (State != FlowState.Title || _rankPopup) return;
            _rankPopup = true;
            _popupTime = Time.unscaledTime;
            _lastInputTime = Time.unscaledTime;
            _rankBtnRect.gameObject.SetActive(false);
            // 팝업이 떠 있는 동안엔 제목도 어둡게 덮도록 Dim을 랭킹 패널 바로 뒤로
            // (Dim이 앞쪽에 있으므로 옮기면 랭킹 인덱스가 하나 줄어듦 → -1 위치가 랭킹 바로 뒤)
            _dim.transform.SetSiblingIndex(_ranking.transform.GetSiblingIndex() - 1);
            _dim.SetActive(true);
            _ranking.SetActive(true);
            FillRanking();
        }

        void CloseRankingPopup()
        {
            _rankPopup = false;
            _ranking.SetActive(false);
            _dim.SetActive(false);
            _dim.transform.SetSiblingIndex(0); // 다른 화면(결과 등)에서는 원래처럼 맨 뒤
            _rankBtnRect.gameObject.SetActive(true);
            _stateTime = Time.unscaledTime; // 닫는 클릭이 곧바로 게임 시작으로 이어지지 않게
        }

        void ShowToast(string msg)
        {
            _toastText.text = msg;
            _toast.SetActive(true);
            _toastUntil = Time.unscaledTime + 2.5f;
        }

        // =====================================================================
        // 랭킹 표
        // =====================================================================
        void FillRanking()
        {
            for (int i = _rankRows.childCount - 1; i >= 0; i--) Destroy(_rankRows.GetChild(i).gameObject);

            var list = RankingStore.Entries;
            if (list.Count == 0)
            {
                MakeText(_rankRows, "아직 기록이 없습니다", 44, textColor, TextAlignmentOptions.Center, new Vector2(0, -60), new Vector2(800, 80));
                return;
            }

            int shown = Mathf.Min(10, list.Count);
            for (int i = 0; i < shown; i++) MakeRow(i + 1, list[i], i, _lastRank == i + 1);

            if (_lastRank > 10)
            {
                MakeText(_rankRows, "· · ·", 40, textColor, TextAlignmentOptions.Center, new Vector2(0, -RowY(10) + 10), new Vector2(800, 40));
                MakeRow(_lastRank, list[_lastRank - 1], 10.6f, true);
            }
        }

        static float RowY(float slot) => 40f + slot * 66f;

        void MakeRow(int rank, RankEntry e, float slot, bool highlight)
        {
            var row = new GameObject("Row" + rank, typeof(RectTransform));
            var rt = (RectTransform)row.transform;
            rt.SetParent(_rankRows, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(860, 60);
            rt.anchoredPosition = new Vector2(0, -RowY(slot));

            if (highlight)
            {
                var bg = row.AddComponent<Image>();
                bg.color = highlightRow;
                if (panelSprite != null) { bg.sprite = panelSprite; bg.type = Image.Type.Sliced; bg.pixelsPerUnitMultiplier = 3f; }
            }

            Sprite medal = rank == 1 ? medalGold : rank == 2 ? medalSilver : rank == 3 ? medalBronze : null;
            if (medal != null)
            {
                var img = MakeImage(rt, medal, new Vector2(-370, 0), new Vector2(56, 56));
                img.preserveAspect = true;
            }
            else
            {
                Bold(MakeText(rt, rank.ToString(), 40, textColor, TextAlignmentOptions.Center, new Vector2(-370, 0), new Vector2(90, 60)));
            }
            MakeText(rt, e.name, 40, textColor, TextAlignmentOptions.Left, new Vector2(-100, 0), new Vector2(420, 60));
            MakeText(rt, countLabel + " " + e.count, 30, WithAlpha(textColor, 0.7f), TextAlignmentOptions.Right, new Vector2(125, 0), new Vector2(140, 60));
            Bold(MakeText(rt, e.score.ToString("N0"), 42, textColor, TextAlignmentOptions.Right, new Vector2(300, 0), new Vector2(200, 60))); // 오른쪽 끝 400 (강조 줄 테두리 430과 30 띄움)
        }

        // =====================================================================
        // UI 생성
        // =====================================================================
        void BuildUI()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.transform.SetParent(transform, false);
            }

            var cgo = new GameObject("UICanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            cgo.transform.SetParent(transform, false);
            var canvas = cgo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 10;
            var scaler = cgo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _canvas = (RectTransform)cgo.transform;

            // 화면 어둡게 (패널 화면에서 글씨가 잘 보이도록)
            _dim = new GameObject("Dim", typeof(RectTransform), typeof(Image));
            var drt = (RectTransform)_dim.transform;
            drt.SetParent(_canvas, false);
            Stretch(drt);
            _dim.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.35f);

            BuildTitle();
            BuildNameEntry();
            BuildHowTo();
            BuildHud();
            BuildResult();
            BuildRanking();
            BuildStory();
            BuildSettings();
            BuildToast();
        }

        void BuildTitle()
        {
            _title = Group("Title");
            var t = (RectTransform)_title.transform;
            var title = MakeText(t, titleText, 128, textColor, TextAlignmentOptions.Center, new Vector2(0, 380), new Vector2(1600, 170));
            Bold(title);
            AddOutline(title, outlineColor, 0.55f);
            bool hasSub = !string.IsNullOrEmpty(subtitleText);
            if (hasSub)
            {
                var sub = MakeText(t, subtitleText, 42, textColor, TextAlignmentOptions.Center, new Vector2(0, 275), new Vector2(1700, 60));
                Bold(sub);
                AddOutline(sub, outlineColor, 0.45f);
            }
            _titleBest = MakeText(t, "", 40, textColor, TextAlignmentOptions.Center, new Vector2(0, hasSub ? 205 : 270), new Vector2(1400, 60));
            AddOutline(_titleBest, outlineColor, 0.45f);
            _titlePrompt = MakeText(t, titlePromptText, 52, textColor, TextAlignmentOptions.Center, new Vector2(0, -440), new Vector2(1600, 80));
            Bold(_titlePrompt);
            AddOutline(_titlePrompt, outlineColor, 0.45f);

            // 오른쪽 아래 버튼 3개 (아래부터 랭킹 보기 / 스토리 보기 / 설정), 모두 같은 크기·아이콘 배치
            _rankBtnRect = TitleButton(t, "랭킹 보기", 0, OpenRankingPopup, medalGold);
            _rankBtnRect.name = "RankingButton";
            _storyBtnRect = TitleButton(t, "스토리 보기", 1, OpenStory, iconStory);
            _setBtnRect = TitleButton(t, "설정", 2, OpenSettings, iconSettings);
        }

        void BuildNameEntry()
        {
            _name = Group("NameEntry");
            var panel = MakePanel((RectTransform)_name.transform, new Vector2(0, 60), new Vector2(1000, 520));
            Bold(MakeText(panel, "닉네임을 입력하세요", 60, textColor, TextAlignmentOptions.Center, new Vector2(0, 170), new Vector2(900, 90)));

            // 입력칸
            var box = MakePanel(panel, new Vector2(0, 30), new Vector2(640, 130));
            box.GetComponent<Image>().color = Color.white;
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(RectMask2D));
            var vrt = (RectTransform)viewport.transform;
            vrt.SetParent(box, false);
            Stretch(vrt);
            vrt.offsetMin = new Vector2(20, 8); vrt.offsetMax = new Vector2(-20, -8);
            var txt = MakeText(vrt, "", 68, textColor, TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
            Stretch(txt.rectTransform);
            txt.richText = false;
            var ph = MakeText(vrt, "닉네임", 68, WithAlpha(textColor, 0.3f), TextAlignmentOptions.Center, Vector2.zero, Vector2.zero);
            Stretch(ph.rectTransform);
            _nameInput = box.gameObject.AddComponent<TMP_InputField>();
            _nameInput.textViewport = vrt;
            _nameInput.textComponent = txt;
            _nameInput.placeholder = ph;
            _nameInput.fontAsset = font;
            _nameInput.pointSize = 68;
            _nameInput.characterLimit = nameMaxLength;
            _nameInput.lineType = TMP_InputField.LineType.SingleLine;
            _nameInput.richText = false;
            _nameInput.onValidateInput = ValidateNameChar;
            _nameInput.caretWidth = 4;
            _nameInput.caretColor = textColor;
            _nameInput.customCaretColor = true;

            MakeText(panel, "한글 · 영문 · 숫자  최대 " + nameMaxLength + "자   |   Enter로 확인", 34, WithAlpha(textColor, 0.8f), TextAlignmentOptions.Center, new Vector2(0, -100), new Vector2(900, 50));
            _nameError = MakeText(panel, "", 34, new Color(0.8f, 0.15f, 0.1f), TextAlignmentOptions.Center, new Vector2(0, -170), new Vector2(940, 50));
        }

        void BuildHowTo()
        {
            _howTo = Group("HowTo");
            var panel = MakePanel((RectTransform)_howTo.transform, new Vector2(0, 40), new Vector2(1300, 720));
            Bold(MakeText(panel, "조작 방법", 70, textColor, TextAlignmentOptions.Center, new Vector2(0, 262), new Vector2(1000, 100)));
            if (showSpaceKey)
            {
                if (keySpaceSprite != null) MakeImage(panel, keySpaceSprite, new Vector2(-260, 120), new Vector2(360, 180)).preserveAspect = true;
                MakeText(panel, "또는", 44, textColor, TextAlignmentOptions.Center, new Vector2(-20, 120), new Vector2(140, 60));
                if (mouseLeftSprite != null) MakeImage(panel, mouseLeftSprite, new Vector2(200, 120), new Vector2(180, 180)).preserveAspect = true;
            }
            else if (mouseLeftSprite != null) MakeImage(panel, mouseLeftSprite, new Vector2(0, 120), new Vector2(180, 180)).preserveAspect = true;
            MakeText(panel, howToLine1, 44, textColor, TextAlignmentOptions.Center, new Vector2(0, -20), new Vector2(1200, 60));
            MakeText(panel, howToLine2, 44, textColor, TextAlignmentOptions.Center, new Vector2(0, -85), new Vector2(1200, 60));
            Bold(MakeText(panel, howToLine3, 40, accentColor, TextAlignmentOptions.Center, new Vector2(0, -150), new Vector2(1200, 60)));
            Bold(MakeText(panel, "누르면 시작", 50, textColor, TextAlignmentOptions.Center, new Vector2(0, -270), new Vector2(800, 70)));
        }

        void BuildHud()
        {
            _hud = Group("HUD");
            var t = (RectTransform)_hud.transform;
            var panel = MakePanel(t, Vector2.zero, new Vector2(620, 224));   // 글자가 테두리에 붙지 않게 여유
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0f, 1f);
            panel.anchoredPosition = new Vector2(30, -30);
            _hudScore = MakeText(panel, "점수 0", 60, textColor, TextAlignmentOptions.Left, new Vector2(0, 36), new Vector2(520, 80));
            Bold(_hudScore);
            _hudSub = MakeText(panel, "", 38, textColor, TextAlignmentOptions.Left, new Vector2(0, -50), new Vector2(520, 60));

            _hudName = MakeText(t, "", 48, textColor, TextAlignmentOptions.Right, Vector2.zero, new Vector2(600, 80));
            var nrt = _hudName.rectTransform;
            nrt.anchorMin = nrt.anchorMax = nrt.pivot = new Vector2(1f, 1f);
            nrt.anchoredPosition = new Vector2(-40, -40);
            Bold(_hudName);
            AddOutline(_hudName, outlineColor, 0.45f);

            _judge = MakeText(t, "", 64, textColor, TextAlignmentOptions.Center, new Vector2(0, 300), new Vector2(1200, 100));
            Bold(_judge);
            AddOutline(_judge, outlineColor, 0.45f);
        }

        void BuildResult()
        {
            _result = Group("Result");
            var panel = MakePanel((RectTransform)_result.transform, new Vector2(0, 40), new Vector2(1260, 640));   // 긴 닉네임·큰 숫자도 테두리에 걸치지 않게
            _resultTitle = Bold(MakeText(panel, "게임 오버", 90, textColor, TextAlignmentOptions.Center, new Vector2(0, 220), new Vector2(1000, 120)));
            _resultReason = MakeText(panel, "", 40, WithAlpha(textColor, 0.8f), TextAlignmentOptions.Center, new Vector2(0, 130), new Vector2(1000, 60));
            _resultScore = MakeText(panel, "", 110, accentColor, TextAlignmentOptions.Center, new Vector2(0, 20), new Vector2(1000, 140));
            Bold(_resultScore);
            _resultSub = MakeText(panel, "", 44, textColor, TextAlignmentOptions.Center, new Vector2(0, -75), new Vector2(1140, 60));
            _resultRank = MakeText(panel, "", 50, textColor, TextAlignmentOptions.Center, new Vector2(0, -155), new Vector2(1140, 70));
            Bold(_resultRank);
            MakeText(panel, "누르면 랭킹 보기", 38, WithAlpha(textColor, 0.8f), TextAlignmentOptions.Center, new Vector2(0, -250), new Vector2(800, 60));
        }

        void BuildRanking()
        {
            _ranking = Group("Ranking");
            var panel = MakePanel((RectTransform)_ranking.transform, new Vector2(0, 0), new Vector2(1060, 1000));
            Bold(MakeText(panel, "랭킹 TOP 10", 76, textColor, TextAlignmentOptions.Center, new Vector2(0, 408), new Vector2(1000, 110)));

            var rows = new GameObject("Rows", typeof(RectTransform));
            _rankRows = (RectTransform)rows.transform;
            _rankRows.SetParent(panel, false);
            _rankRows.anchorMin = _rankRows.anchorMax = new Vector2(0.5f, 1f);
            _rankRows.pivot = new Vector2(0.5f, 1f);
            _rankRows.sizeDelta = new Vector2(900, 780);
            _rankRows.anchoredPosition = new Vector2(0, -130);

            _rankingFooter = MakeText(panel, "", 34, WithAlpha(textColor, 0.8f), TextAlignmentOptions.Center, new Vector2(0, -446), new Vector2(1000, 50)); // 패널 아래 테두리와 11위 줄 사이
        }

        // 배치 (1920×1080 기준): 자막 상자 아래 50~250, 그 아래 쪽수 글자, 그림은 270~1050(16:9면 폭 약 1387 → 좌우 여백 약 266),
        // 건너뛰기 버튼은 오른쪽 여백 안(폭 220)에 두어 그림과 겹치지 않게
        void BuildStory()
        {
            _story = Group("Story");
            var t = (RectTransform)_story.transform;
            var bg = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            var bgr = (RectTransform)bg.transform; bgr.SetParent(t, false); Stretch(bgr);
            bg.GetComponent<Image>().color = new Color(0.12f, 0.1f, 0.08f, 1f);
            var img = new GameObject("Image", typeof(RectTransform), typeof(Image));
            var ir = (RectTransform)img.transform; ir.SetParent(t, false); Stretch(ir);
            ir.offsetMin = new Vector2(270, 270); ir.offsetMax = new Vector2(-270, -30);
            _storyImage = img.GetComponent<Image>();
            _storyImage.raycastTarget = false;
            _storyPlaceholder = MakeText(ir, "", 54, WithAlpha(textColor, 0.5f), TextAlignmentOptions.Center, Vector2.zero, new Vector2(1200, 100));
            Bold(_storyPlaceholder);
            // 자막 상자 (테두리에서 글자까지 여유를 두고, 2줄까지)
            var cap = MakePanel(t, Vector2.zero, new Vector2(1700, 200));
            cap.anchorMin = cap.anchorMax = cap.pivot = new Vector2(0.5f, 0f);
            cap.anchoredPosition = new Vector2(0, 50);
            _storyText = MakeText(cap, "", 44, textColor, TextAlignmentOptions.Center, Vector2.zero, new Vector2(1560, 140));
            _storyText.textWrappingMode = TextWrappingModes.Normal;
            Bold(_storyText);
            // 쪽수·안내는 자막 상자 바깥 아래(어두운 배경 위)
            _storyPage = MakeText(t, "", 26, new Color(1f, 0.95f, 0.85f, 0.75f), TextAlignmentOptions.Right, Vector2.zero, new Vector2(1700, 40));
            var pr = _storyPage.rectTransform;
            pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0.5f, 0f);
            pr.anchoredPosition = new Vector2(0, 6);
            // 오른쪽 위 '건너뛰기' (그림 오른쪽 여백 안)
            var skip = MakeButton(t, "건너뛰기 >>", Vector2.zero, new Vector2(246, 80), 28, EndStory);
            skip.anchorMin = skip.anchorMax = skip.pivot = new Vector2(1f, 1f);
            skip.anchoredPosition = new Vector2(-12, -30);
            _storySkipRect = skip;
        }

        void BuildSettings()
        {
            _settings = new GameObject("Settings", typeof(RectTransform), typeof(Image));
            var t = (RectTransform)_settings.transform;
            t.SetParent(_canvas, false);
            Stretch(t);
            _settings.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.45f); // 뒤 클릭 막기 + 어둡게
            var panel = MakePanel(t, new Vector2(0, 20), new Vector2(980, 690));
            Bold(MakeText(panel, "설정", 70, textColor, TextAlignmentOptions.Center, new Vector2(0, 245), new Vector2(800, 100)));

            SettingRow(panel, "배경음", 110, out _setBgm, () => ChangeVolume(true, -0.1f), () => ChangeVolume(true, 0.1f));
            SettingRow(panel, "효과음", -10, out _setSfx, () => ChangeVolume(false, -0.1f), () => ChangeVolume(false, 0.1f));

            MakeText(panel, "화면", 46, textColor, TextAlignmentOptions.Left, new Vector2(-250, -130), new Vector2(300, 70));
            var scr = MakeButton(panel, "", new Vector2(130, -130), new Vector2(380, 90), 42, ToggleScreen);
            _setScreen = scr.GetComponentInChildren<TextMeshProUGUI>();

            MakeButton(panel, "닫기", new Vector2(0, -255), new Vector2(300, 90), 44, CloseSettings);
            _settings.SetActive(false);
        }

        void SettingRow(RectTransform panel, string label, float y, out TextMeshProUGUI value, UnityEngine.Events.UnityAction minus, UnityEngine.Events.UnityAction plus)
        {
            MakeText(panel, label, 46, textColor, TextAlignmentOptions.Left, new Vector2(-250, y), new Vector2(300, 70));
            IconButton(panel, iconMinus, "-", new Vector2(-20, y), minus);
            value = Bold(MakeText(panel, "100%", 46, textColor, TextAlignmentOptions.Center, new Vector2(130, y), new Vector2(180, 70)));
            IconButton(panel, iconPlus, "+", new Vector2(280, y), plus);
        }

        // 글자 대신 이미지를 버튼 정중앙에 (이미지가 없으면 글자)
        void IconButton(RectTransform panel, Sprite icon, string fallback, Vector2 pos, UnityEngine.Events.UnityAction onClick)
        {
            var b = MakeButton(panel, icon != null ? "" : fallback, pos, new Vector2(100, 90), 56, onClick);
            if (icon != null) MakeImage(b, icon, Vector2.zero, new Vector2(40, 40)).preserveAspect = true;
        }


        RectTransform MakeButton(RectTransform parent, string label, Vector2 pos, Vector2 size, int fontSize, UnityEngine.Events.UnityAction onClick)
        {
            var b = MakePanel(parent, pos, size);
            b.name = "Button_" + label;
            var img = b.GetComponent<Image>();
            img.raycastTarget = true;
            img.pixelsPerUnitMultiplier = ButtonPPU;
            var button = b.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            var cb = button.colors; cb.highlightedColor = new Color(1f, 0.93f, 0.8f); cb.pressedColor = new Color(0.9f, 0.8f, 0.65f); button.colors = cb;
            button.onClick.AddListener(onClick);
            Bold(MakeText(b, label, fontSize, textColor, TextAlignmentOptions.Center, Vector2.zero, size));
            return b;
        }

        // 버튼 왼쪽에 아이콘, 글자는 오른쪽으로 (랭킹 보기 버튼과 같은 배치)
        // 처음 화면 오른쪽 아래 버튼 (slot 0이 맨 아래). 폭 360, 아이콘은 왼쪽, 글자는 나머지 칸 가운데
        RectTransform TitleButton(RectTransform parent, string label, int slot, UnityEngine.Events.UnityAction onClick, Sprite icon)
        {
            var b = MakeButton(parent, label, Vector2.zero, new Vector2(360, 110), 46, onClick);
            b.anchorMin = b.anchorMax = b.pivot = new Vector2(1f, 0f);
            b.anchoredPosition = new Vector2(-40, 40 + slot * (110 + 20));
            AddButtonIcon(b, icon);
            return b;
        }

        // 버튼 왼쪽에 아이콘(64), 글자는 오른쪽 칸에 맞춰 (폭 360 기준: 아이콘 -150~-86, 글자 -66~+144)
        void AddButtonIcon(RectTransform btn, Sprite icon)
        {
            if (icon == null) return;
            MakeImage(btn, icon, new Vector2(-118, 0), new Vector2(64, 64)).preserveAspect = true;
            var label = btn.GetComponentInChildren<TextMeshProUGUI>();
            if (label != null)
            {
                label.rectTransform.anchoredPosition = new Vector2(39, 0);
                label.rectTransform.sizeDelta = new Vector2(210, 80);
                // 글자가 아이콘·테두리에 걸치지 않게 칸에 맞춰 줄임 (최대는 원래 크기)
                label.fontSizeMax = label.fontSize;
                label.fontSizeMin = 28;
                label.enableAutoSizing = true;
            }
        }



        void BuildToast()
        {
            _toast = new GameObject("Toast", typeof(RectTransform));
            var t = (RectTransform)_toast.transform;
            t.SetParent(_canvas, false);
            var panel = MakePanel(t, new Vector2(0, -420), new Vector2(760, 110));
            _toastText = MakeText(panel, "", 42, textColor, TextAlignmentOptions.Center, Vector2.zero, new Vector2(720, 90));
            Bold(_toastText);
            _toast.SetActive(false);
        }

        // ---- UI 헬퍼 ----
        GameObject Group(string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(_canvas, false);
            Stretch(rt);
            return go;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero; rt.offsetMax = Vector2.zero;
        }

        RectTransform MakePanel(RectTransform parent, Vector2 pos, Vector2 size)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var img = go.GetComponent<Image>();
            if (panelSprite != null) { img.sprite = panelSprite; img.type = Image.Type.Sliced; img.pixelsPerUnitMultiplier = 1f; }
            else img.color = new Color(0.98f, 0.92f, 0.8f, 0.97f);
            return rt;
        }

        TextMeshProUGUI MakeText(RectTransform parent, string text, int size, Color color, TextAlignmentOptions align, Vector2 pos, Vector2 box)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = box;
            rt.anchoredPosition = pos;
            var t = go.GetComponent<TextMeshProUGUI>();
            if (font != null) t.font = font;
            t.text = text;
            t.fontSize = size;
            t.color = color;
            t.alignment = align;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Overflow;
            t.raycastTarget = false;
            return t;
        }

        Image MakeImage(RectTransform parent, Sprite sprite, Vector2 pos, Vector2 size)
        {
            var go = new GameObject("Image", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            var img = go.GetComponent<Image>();
            img.sprite = sprite;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>글자 뒤에 두꺼운 같은 글자를 깔아 외곽선을 만듦 (TextStroke 참고). thickness 0~1</summary>
        static void AddOutline(TextMeshProUGUI t, Color c, float thickness)
        {
            TextStroke.Attach(t, c, thickness);
        }

        TextMeshProUGUI Bold(TextMeshProUGUI t)
        {
            if (fontBold != null) t.font = fontBold;
            else t.fontStyle = FontStyles.Bold;
            return t;
        }

        static Color WithAlpha(Color c, float a) { c.a = a; return c; }
    

        // 처음 화면 '스토리 보기' 버튼 → 스토리 (끝나거나 건너뛰면 처음 화면으로)
        void OpenStory()
        {
            if (State != FlowState.Title || _rankPopup || _settingsOpen) return;
            if (story == null || story.Length == 0) return;
            Go(FlowState.Story);
        }
}
}
