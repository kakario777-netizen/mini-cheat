using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

namespace MiniCheat
{
    // 일본어·중국어 → 한글 자동 번역. 한 번 번역한 건 '내 문서\미니 치트\번역 사전.txt' 에 저장해 다시 묻지 않음
    static class Translator
    {
        static Dictionary<string, string> cache;
        static readonly object lk = new object();

        static string FilePath { get { return Path.Combine(Backup.Root, "번역 사전.txt"); } }

        // 한자·가나가 들어 있으면 번역 대상
        public static bool Needs(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s) if (c >= 0xAC00 && c <= 0xD7A3) return false; // 이미 한글
            foreach (char c in s)
                if ((c >= 0x3040 && c <= 0x30FF) || (c >= 0x3400 && c <= 0x4DBF) || (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0xFF66 && c <= 0xFF9F))
                    return true;
            return false;
        }

        static string Norm(string s) { return s.Replace("\r", "").Replace("\n", " ").Replace("\t", " ").Trim(); }

        static void Load()
        {
            lock (lk)
            {
                if (cache != null) return;
                cache = new Dictionary<string, string>();
                try
                {
                    if (File.Exists(FilePath))
                        foreach (var l in File.ReadAllLines(FilePath, Encoding.UTF8))
                        {
                            int t = l.IndexOf('\t');
                            if (t > 0) cache[l.Substring(0, t)] = l.Substring(t + 1);
                        }
                }
                catch { }
            }
        }

        public static string Get(string s)
        {
            if (!Needs(s)) return null;
            Load();
            string t;
            lock (lk) return cache.TryGetValue(Norm(s), out t) ? t : null;
        }

        public static string Or(string s) { return Get(s) ?? s; }

        public static int CountMissing(IEnumerable<string> texts)
        {
            Load();
            lock (lk) return texts.Where(Needs).Select(Norm).Distinct().Count(t => !cache.ContainsKey(t));
        }

        // 빠진 것만 묶어서 번역. progress(끝난 개수, 전체)
        public static void Translate(IEnumerable<string> texts, Action<int, int> progress)
        {
            Load();
            List<string> todo;
            lock (lk) todo = texts.Where(Needs).Select(Norm).Where(t => t.Length > 0).Distinct().Where(t => !cache.ContainsKey(t)).ToList();
            int done = 0;
            int i = 0;
            while (i < todo.Count)
            {
                var batch = new List<string>();
                int len = 0;
                while (i < todo.Count && batch.Count < 80 && (batch.Count == 0 || len + todo[i].Length < 1800))
                {
                    batch.Add(todo[i]); len += todo[i].Length + 1; i++;
                }
                var result = new Dictionary<string, string>();
                var lines = Google(string.Join("\n", batch)).Split('\n').Select(x => x.Trim()).ToList();
                if (lines.Count == batch.Count)
                    for (int k = 0; k < batch.Count; k++) result[batch[k]] = lines[k];
                else
                    foreach (var b in batch) { result[b] = Google(b).Replace("\n", " ").Trim(); Thread.Sleep(80); }
                Save(result);
                done += batch.Count;
                if (progress != null) progress(done, todo.Count);
                Thread.Sleep(150);
            }
        }

        static void Save(Dictionary<string, string> add)
        {
            lock (lk)
            {
                foreach (var kv in add) if (kv.Value.Length > 0) cache[kv.Key] = kv.Value;
                try
                {
                    Directory.CreateDirectory(Backup.Root);
                    File.AppendAllLines(FilePath, add.Where(kv => kv.Value.Length > 0).Select(kv => kv.Key + "\t" + kv.Value.Replace("\t", " ")), Encoding.UTF8);
                }
                catch { }
            }
        }

        static string Google(string text)
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            using (var wc = new WebClient())
            {
                wc.Encoding = Encoding.UTF8;
                wc.Headers.Add("User-Agent", "Mozilla/5.0");
                wc.Headers.Add(HttpRequestHeader.ContentType, "application/x-www-form-urlencoded;charset=UTF-8");
                string res = wc.UploadString("https://translate.googleapis.com/translate_a/single?client=gtx&sl=auto&tl=ko&dt=t", "POST",
                    "q=" + Uri.EscapeDataString(text));
                var segs = Json.Arr(Json.At(Json.Parse(res) as JArr, 0));
                var sb = new StringBuilder();
                if (segs != null)
                    foreach (var seg in segs.Items)
                    {
                        var v = Json.At(seg as JArr, 0) as JVal;
                        if (v != null && v.IsStr) sb.Append(v.Str);
                    }
                return sb.ToString();
            }
        }
    }
}
