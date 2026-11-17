using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace MiniCheat
{
    // lz-string (쯔꾸르 MV 세이브 압축) — compressToBase64 / decompressFromBase64
    static class LZString
    {
        const string Key = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=";

        public static string CompressToBase64(string input)
        {
            string res = Compress(input, 6);
            switch (res.Length % 4)
            {
                case 1: return res + "===";
                case 2: return res + "==";
                case 3: return res + "=";
            }
            return res;
        }

        public static string DecompressFromBase64(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            var map = new int[128];
            for (int i = 0; i < Key.Length; i++) map[Key[i]] = i;
            return Decompress(input.Length, 32, idx => idx < input.Length ? map[input[idx] & 127] : 0);
        }

        class Bits
        {
            public readonly StringBuilder Out = new StringBuilder();
            public int Val, Pos;
            readonly int per;
            public Bits(int bitsPerChar) { per = bitsPerChar; }

            public void Put(int value, int count)
            {
                for (int i = 0; i < count; i++)
                {
                    Val = (Val << 1) | (value & 1);
                    if (Pos == per - 1) { Pos = 0; Out.Append(Key[Val]); Val = 0; }
                    else Pos++;
                    value >>= 1;
                }
            }
        }

        static string Compress(string s, int bitsPerChar)
        {
            var dict = new Dictionary<string, int>();
            var toCreate = new HashSet<string>();
            string w = "";
            int enlargeIn = 2, dictSize = 3, numBits = 2;
            var b = new Bits(bitsPerChar);

            Action emitW = () =>
            {
                if (toCreate.Contains(w))
                {
                    if (w[0] < 256) { b.Put(0, numBits); b.Put(w[0], 8); }
                    else { b.Put(1, numBits); b.Put(w[0], 16); }
                    enlargeIn--;
                    if (enlargeIn == 0) { enlargeIn = 1 << numBits; numBits++; }
                    toCreate.Remove(w);
                }
                else b.Put(dict[w], numBits);
                enlargeIn--;
                if (enlargeIn == 0) { enlargeIn = 1 << numBits; numBits++; }
            };

            for (int ii = 0; ii < s.Length; ii++)
            {
                string c = s[ii].ToString();
                if (!dict.ContainsKey(c)) { dict[c] = dictSize++; toCreate.Add(c); }
                string wc = w + c;
                if (dict.ContainsKey(wc)) { w = wc; continue; }
                emitW();
                dict[wc] = dictSize++;
                w = c;
            }
            if (w != "") emitW();

            b.Put(2, numBits);
            while (true)
            {
                b.Val <<= 1;
                if (b.Pos == bitsPerChar - 1) { b.Out.Append(Key[b.Val]); break; }
                b.Pos++;
            }
            return b.Out.ToString();
        }

        static string Decompress(int length, int resetValue, Func<int, int> next)
        {
            var dict = new List<string> { "0", "1", "2" };
            int enlargeIn = 4, numBits = 3;
            int val = next(0), pos = resetValue, index = 1;
            var result = new StringBuilder();

            Func<int, int> read = n =>
            {
                int bits = 0, power = 1, max = 1 << n;
                while (power != max)
                {
                    int resb = val & pos;
                    pos >>= 1;
                    if (pos == 0) { pos = resetValue; val = next(index++); }
                    if (resb > 0) bits |= power;
                    power <<= 1;
                }
                return bits;
            };

            string c;
            switch (read(2))
            {
                case 0: c = ((char)read(8)).ToString(); break;
                case 1: c = ((char)read(16)).ToString(); break;
                default: return "";
            }
            dict.Add(c);
            string w = c;
            result.Append(c);
            while (true)
            {
                if (index > length) return "";
                int code = read(numBits);
                switch (code)
                {
                    case 0: dict.Add(((char)read(8)).ToString()); code = dict.Count - 1; enlargeIn--; break;
                    case 1: dict.Add(((char)read(16)).ToString()); code = dict.Count - 1; enlargeIn--; break;
                    case 2: return result.ToString();
                }
                if (enlargeIn == 0) { enlargeIn = 1 << numBits; numBits++; }
                string entry;
                if (code < dict.Count && code > 2) entry = dict[code];
                else if (code == dict.Count) entry = w + w[0];
                else return null;
                result.Append(entry);
                dict.Add(w + entry[0]);
                enlargeIn--;
                w = entry;
                if (enlargeIn == 0) { enlargeIn = 1 << numBits; numBits++; }
            }
        }
    }

    enum SaveFmt { MV, MZ, MZRaw, Gzip, PlainJson }

    // 세이브 파일 ↔ JSON 텍스트
    static class SaveCodec
    {
        static readonly UTF8Encoding Utf8Strict = new UTF8Encoding(false, true);

        public static string Read(string path, out SaveFmt fmt)
        {
            var b = File.ReadAllBytes(path);
            if (b.Length == 0) throw new Exception("빈 파일이에요");
            if (b[0] == '{' || b[0] == '[') { fmt = SaveFmt.PlainJson; return Encoding.UTF8.GetString(b); }
            if (b[0] == 0x1f && b.Length > 1 && b[1] == 0x8b) { fmt = SaveFmt.Gzip; return Gunzip(b); }

            if (path.EndsWith(".rmmzsave", StringComparison.OrdinalIgnoreCase) || b[0] == 0x78)
            {
                // MZ: zlib 압축 결과를 '바이트 문자열'로 만든 뒤 UTF-8 로 저장함
                byte[] z = null;
                try
                {
                    string s = Utf8Strict.GetString(b);
                    z = new byte[s.Length];
                    for (int i = 0; i < s.Length; i++)
                    {
                        if (s[i] > 255) { z = null; break; }
                        z[i] = (byte)s[i];
                    }
                }
                catch { z = null; }
                if (z != null) { fmt = SaveFmt.MZ; return Inflate(z); }
                fmt = SaveFmt.MZRaw;
                return Inflate(b);
            }

            fmt = SaveFmt.MV;
            string json = LZString.DecompressFromBase64(Encoding.ASCII.GetString(b).Trim());
            if (string.IsNullOrEmpty(json)) throw new Exception("세이브를 풀지 못했어요");
            return json;
        }

        public static void Write(string path, string json, SaveFmt fmt)
        {
            byte[] outBytes;
            switch (fmt)
            {
                case SaveFmt.PlainJson: outBytes = new UTF8Encoding(false).GetBytes(json); break;
                case SaveFmt.Gzip: outBytes = Gzip(json); break;
                case SaveFmt.MZRaw: outBytes = Deflate(json); break;
                case SaveFmt.MZ:
                    {
                        var z = Deflate(json);
                        var chars = new char[z.Length];
                        for (int i = 0; i < z.Length; i++) chars[i] = (char)z[i];
                        outBytes = new UTF8Encoding(false).GetBytes(chars);
                        break;
                    }
                default: outBytes = Encoding.ASCII.GetBytes(LZString.CompressToBase64(json)); break;
            }
            string tmp = path + ".minicheat.tmp";
            File.WriteAllBytes(tmp, outBytes);
            // 확인: 다시 읽어서 같은지
            SaveFmt f2;
            if (Read(tmp, out f2) != json) { File.Delete(tmp); throw new Exception("저장 확인에 실패해서 원본을 건드리지 않았어요"); }
            File.Copy(tmp, path, true);
            File.Delete(tmp);
        }

        static string Inflate(byte[] z)
        {
            using (var ms = new MemoryStream(z, 2, z.Length - 2))
            using (var ds = new DeflateStream(ms, CompressionMode.Decompress))
            using (var o = new MemoryStream())
            {
                ds.CopyTo(o);
                return Encoding.UTF8.GetString(o.ToArray());
            }
        }

        static byte[] Deflate(string json)
        {
            var data = new UTF8Encoding(false).GetBytes(json);
            using (var o = new MemoryStream())
            {
                o.WriteByte(0x78); o.WriteByte(0x9C);
                using (var ds = new DeflateStream(o, CompressionMode.Compress, true)) ds.Write(data, 0, data.Length);
                uint a = 1, bsum = 0;
                foreach (byte x in data) { a = (a + x) % 65521; bsum = (bsum + a) % 65521; }
                uint ad = (bsum << 16) | a;
                o.WriteByte((byte)(ad >> 24)); o.WriteByte((byte)(ad >> 16)); o.WriteByte((byte)(ad >> 8)); o.WriteByte((byte)ad);
                return o.ToArray();
            }
        }

        static string Gunzip(byte[] b)
        {
            using (var ms = new MemoryStream(b))
            using (var gz = new GZipStream(ms, CompressionMode.Decompress))
            using (var o = new MemoryStream())
            {
                gz.CopyTo(o);
                return Encoding.UTF8.GetString(o.ToArray());
            }
        }

        static byte[] Gzip(string json)
        {
            var data = new UTF8Encoding(false).GetBytes(json);
            using (var o = new MemoryStream())
            {
                using (var gz = new GZipStream(o, CompressionMode.Compress, true)) gz.Write(data, 0, data.Length);
                return o.ToArray();
            }
        }
    }
}
