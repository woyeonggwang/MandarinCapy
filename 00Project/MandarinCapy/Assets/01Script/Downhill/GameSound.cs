using UnityEngine;

// 게임 사운드: BGM(반복), 걷기 '뽈뽈', 폭발 '띠용', 과일 '띠링'(연속으로 담을수록 음이 한 단계씩 올라감)
// 볼륨은 사용자 지정값 (BGM 0.15, 과일 0.4, 걷기 0.25, 폭발 0.4)
public class GameSound : MonoBehaviour
{
    public static GameSound I { get; private set; }

    // 설정 화면의 전체 볼륨 배율 (0~1, PlayerPrefs에 저장). 각 소리의 기본 볼륨에 곱해짐
    public static float BgmMul = 1f, SfxMul = 1f;
    const string KeyBgm = "vol_bgm", KeySfx = "vol_sfx";
    public static void LoadVolumes() { BgmMul = PlayerPrefs.GetFloat(KeyBgm, 1f); SfxMul = PlayerPrefs.GetFloat(KeySfx, 1f); }
    public static void SaveVolumes() { PlayerPrefs.SetFloat(KeyBgm, BgmMul); PlayerPrefs.SetFloat(KeySfx, SfxMul); PlayerPrefs.Save(); }

    [Header("소리")]
    public AudioClip bgm, step, boom, fruit, item;

    [Header("날리기 (로봇팔로 던지거나 쳐 낼 때)")]
    public ArmController arm;
    public AudioClip whoosh;
    public AudioClip hitStone, hitWood, hitFruit, hitHay, hitPumpkin, hitMetal, hitWicker, hitClock;
    [Range(0f, 1f)] public float hitVolume = 0.4f;
    [Range(0f, 1f)] public float whooshVolume = 0.3f;

    [Header("볼륨")]
    [Range(0f, 1f)] public float bgmVolume = 0.15f;
    [Range(0f, 1f)] public float fruitVolume = 0.4f;
    [Range(0f, 1f)] public float stepVolume = 0.25f;
    [Range(0f, 1f)] public float boomVolume = 0.4f;

    [Header("걷기")]
    public CapybaraWalker walker;
    public float stepsPerCycle = 2f;     // 걷기 애니메이션 한 바퀴(1초 × 애니 속도)당 발소리 수
    public float stepPitchAlt = 1.15f;   // 번갈아 높은 음 (뽈-뽈)

    [Header("과일 단계")]
    public float comboWindow = 2.5f;     // 이 시간 안에 다음 과일을 담으면 한 단계 위 음
    static readonly int[] Scale = { 0, 2, 4, 5, 7, 9, 11, 12, 14, 16 }; // 도레미파솔라시도레미

    AudioSource bgmSrc, sfxSrc, stepSrc;
    float nextStep;
    bool altStep;
    int fruitStep = -1;
    float lastFruit = -99f;

    void Awake()
    {
        I = this;
        LoadVolumes();
        bgmSrc = gameObject.AddComponent<AudioSource>();
        bgmSrc.playOnAwake = false;
        bgmSrc.loop = true;
        sfxSrc = gameObject.AddComponent<AudioSource>();
        sfxSrc.playOnAwake = false;
        stepSrc = gameObject.AddComponent<AudioSource>();
        stepSrc.playOnAwake = false;
    }

    void Start()
    {
        if (bgm != null)
        {
            bgmSrc.clip = bgm;
            bgmSrc.volume = bgmVolume * BgmMul;
            bgmSrc.Play();
        }
    }

    void OnDestroy() { if (I == this) I = null; }

    void Update()
    {
        bgmSrc.volume = bgmVolume * BgmMul;
        if (walker == null || step == null) return;
        bool moving = walker.walking && !walker.Blocked && walker.speed > 0.05f;
        if (!moving) { nextStep = Time.time + 0.05f; return; }
        if (Time.time < nextStep) return;
        // CapybaraAnimator와 같은 식: 애니 속도 = speed / 0.6 (0.8~2.6), 걷기 한 바퀴 = 1초
        float animSpeed = Mathf.Clamp(walker.speed / 0.6f, 0.8f, 2.6f);
        nextStep = Time.time + 1f / (animSpeed * stepsPerCycle);
        altStep = !altStep;
        stepSrc.pitch = (altStep ? stepPitchAlt : 1f) * Random.Range(0.97f, 1.03f);
        stepSrc.PlayOneShot(step, stepVolume * SfxMul);
    }

    // 과일을 수레에 담음: 연속이면 한 단계 위 음
    public void Fruit()
    {
        if (fruit == null) return;
        if (Time.time - lastFruit > comboWindow) fruitStep = -1;
        fruitStep = Mathf.Min(fruitStep + 1, Scale.Length - 1);
        lastFruit = Time.time;
        PlayPitched(fruit, fruitVolume, Mathf.Pow(2f, Scale[fruitStep] / 12f));
    }

    // 업그레이드·시간 아이템: 과일 소리를 높은 음으로
    // 업그레이드·시간 아이템을 수레에 넣음: 과일과 다른 '뽰로롱' 소리
    public void Item()
    {
        if (item != null) PlayPitched(item, fruitVolume, 1f);
    }

    void OnEnable()
    {
        if (arm != null) { arm.Released += OnReleased; arm.Swatted += OnSwatted; }
    }

    void OnDisable()
    {
        if (arm != null) { arm.Released -= OnReleased; arm.Swatted -= OnSwatted; }
    }

    void OnReleased(Obstacle o, bool thrown)
    {
        if (thrown && o != null) Fling(o.kind, o.Body != null ? o.Body.linearVelocity.magnitude : 4f);
    }

    void OnSwatted(Obstacle o, float speed)
    {
        if (o != null) Fling(o.kind, speed * 2f);
    }

    // 날아가는 소리: 물체 재질 타격음 + 휘(빠를수록 크고 높게)
    public void Fling(string kind, float speed)
    {
        float k = Mathf.Clamp01(speed / 8f);
        var hit = HitFor(kind);
        if (hit != null) PlayPitched(hit, hitVolume * Mathf.Lerp(0.7f, 1f, k), Random.Range(0.94f, 1.06f));
        if (whoosh != null) PlayPitched(whoosh, whooshVolume * Mathf.Lerp(0.5f, 1f, k), Mathf.Lerp(0.85f, 1.25f, k));
    }

    AudioClip HitFor(string kind)
    {
        switch (kind)
        {
            case "rock": return hitStone;
            case "crate": case "log": return hitWood;
            case "hay": return hitHay;
            case "pumpkin": return hitPumpkin;
            case "tangerine": case "apple": case "golden": return hitFruit;
            case "bomb": return hitMetal;
            case "upgrade": return hitWicker;
            case "time": return hitClock;
        }
        return hitWood;
    }

    public void Boom()
    {
        if (boom != null) PlayPitched(boom, boomVolume, Random.Range(0.95f, 1.05f));
    }

    // 음높이가 다른 소리가 겹쳐도 서로 끊기지 않게 임시 AudioSource 사용
    void PlayPitched(AudioClip clip, float volume, float pitch)
    {
        var src = gameObject.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.clip = clip;
        src.volume = volume * SfxMul;
        src.pitch = pitch;
        src.Play();
        Destroy(src, clip.length / Mathf.Max(0.1f, pitch) + 0.1f);
    }

    public void ResetCombo() { fruitStep = -1; lastFruit = -99f; }
}
