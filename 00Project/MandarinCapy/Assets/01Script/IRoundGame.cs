namespace MandarinCapy
{
    /// <summary>
    /// GameFlow(처음 화면 → 닉네임 → 조작 안내 → 플레이 → 결과 → 랭킹)가 다루는 게임 한 판의 공통 규약.
    /// 귤 쌓기(StackGame)와 하산 과일 수집(DownhillGame)이 구현.
    /// </summary>
    public interface IRoundGame
    {
        /// <summary>플레이 중 화면 위쪽에 잠깐 띄울 문구 (문구, 강조 여부)</summary>
        event System.Action<string, bool> Judged;
        /// <summary>한 판이 끝났을 때</summary>
        event System.Action GameEnded;

        /// <summary>처음 상태로 되돌림 (처음 화면에서 호출)</summary>
        void ResetRound();
        /// <summary>플레이 시작</summary>
        void BeginRound();

        int Score { get; }
        /// <summary>랭킹에 함께 저장하는 개수 (귤 쌓기: 쌓은 귤, 하산: 모은 과일)</summary>
        int Count { get; }
        /// <summary>플레이 중 점수 아래 줄</summary>
        string HudSub { get; }
        /// <summary>결과 화면 제목</summary>
        string ResultTitle { get; }
        /// <summary>결과 화면 제목 아래 이유 문구</summary>
        string GameOverReason { get; }
        /// <summary>결과 화면 점수 아래 줄</summary>
        string ResultSub { get; }
    }
}
