using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace MandarinCapy
{
    [Serializable]
    public class RankEntry
    {
        public string name;
        public int score;
        public int count;
        public long ticks;   // 기록한 시각 (동점이면 먼저 세운 기록이 위)
    }

    [Serializable]
    class RankData
    {
        public List<RankEntry> entries = new List<RankEntry>();
    }

    /// <summary>
    /// 로컬 랭킹 저장소. Application.persistentDataPath/ranking.json 에 JSON으로 저장.
    /// 같은 닉네임이라도 기록을 모두 남기고, 점수 내림차순 → 먼저 세운 기록 순으로 정렬.
    /// </summary>
    public static class RankingStore
    {
        static string FileName = "ranking.json";
        static RankData _data;

        /// <summary>게임별 랭킹 파일 선택 (그 파일을 다시 읽음)</summary>
        public static void UseFile(string fileName)
        {
            if (string.IsNullOrEmpty(fileName) || fileName == FileName) return;
            FileName = fileName;
            _data = null;
        }

        public static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        public static IReadOnlyList<RankEntry> Entries
        {
            get { Load(); return _data.entries; }
        }

        public static void Load()
        {
            if (_data != null) return;
            try
            {
                if (File.Exists(FilePath))
                    _data = JsonUtility.FromJson<RankData>(File.ReadAllText(FilePath));
            }
            catch (Exception e)
            {
                // 깨진 파일은 지우지 않고 옆에 보관
                Debug.LogWarning("랭킹 파일을 읽지 못했습니다: " + e.Message);
                try { File.Copy(FilePath, FilePath + ".broken", true); } catch { }
                _data = null;
            }
            if (_data == null || _data.entries == null) _data = new RankData();
            Sort();
        }

        /// <summary>기록을 추가하고 저장. 반환값은 이 기록의 순위(1부터).</summary>
        public static int Add(string name, int score, int count)
        {
            Load();
            var e = new RankEntry { name = name, score = score, count = count, ticks = DateTime.Now.Ticks };
            _data.entries.Add(e);
            Sort();
            Save();
            return _data.entries.IndexOf(e) + 1;
        }

        public static RankEntry Get(int rank)
        {
            Load();
            return rank >= 1 && rank <= _data.entries.Count ? _data.entries[rank - 1] : null;
        }

        public static void Clear()
        {
            Load();
            _data.entries.Clear();
            Save();
        }

        static void Sort()
        {
            _data.entries.Sort((a, b) =>
            {
                int c = b.score.CompareTo(a.score);
                return c != 0 ? c : a.ticks.CompareTo(b.ticks);
            });
        }

        static void Save()
        {
            try
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                string tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(_data, true));
                if (File.Exists(FilePath)) File.Replace(tmp, FilePath, null);
                else File.Move(tmp, FilePath);
            }
            catch (Exception e)
            {
                Debug.LogError("랭킹 저장 실패: " + e.Message);
            }
        }
    }
}
