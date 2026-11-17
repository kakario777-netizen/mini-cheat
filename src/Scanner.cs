using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace MiniCheat
{
    // 메모리에 값이 저장되는 방식
    enum VK : byte { I4, I2, I1, I8, F4, F8, Rgss, Smi }

    enum Cmp { Exact, Unknown, Bigger, Smaller, Changed, Same }

    static class Val
    {
        public static int Size(VK k)
        {
            switch (k)
            {
                case VK.I1: return 1;
                case VK.I2: return 2;
                case VK.I8: case VK.F8: return 8;
                default: return 4;
            }
        }

        public static int Step(VK k) { return Math.Min(Size(k), 4); }

        public static bool IsFloat(VK k) { return k == VK.F4 || k == VK.F8; }

        static bool Sane(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return false;
            double a = Math.Abs(d);
            return a == 0 || (a > 1e-6 && a < 1e12);
        }

        public static double Decode(byte[] b, int o, VK k)
        {
            switch (k)
            {
                case VK.I1: return b[o];
                case VK.I2: return BitConverter.ToInt16(b, o);
                case VK.I4: return BitConverter.ToInt32(b, o);
                case VK.I8: return BitConverter.ToInt64(b, o);
                case VK.F4: { double f = BitConverter.ToSingle(b, o); return Sane(f) ? f : double.NaN; }
                case VK.F8: { double d = BitConverter.ToDouble(b, o); return Sane(d) ? d : double.NaN; }
                // 쯔꾸르 XP/VX/VX Ace (루비): 정수 n 을 n*2+1 로 저장
                case VK.Rgss: { int r = BitConverter.ToInt32(b, o); return (r & 1) == 1 ? (double)(r >> 1) : double.NaN; }
                // 쯔꾸르 MV/MZ (자바스크립트 V8): 정수 n 을 n*2 로 저장하는 경우
                case VK.Smi: { int r = BitConverter.ToInt32(b, o); return (r & 1) == 0 ? (double)(r >> 1) : double.NaN; }
            }
            return double.NaN;
        }

        public static byte[] Encode(double v, VK k)
        {
            switch (k)
            {
                case VK.I1: return new[] { (byte)(long)Math.Round(v) };
                case VK.I2: return BitConverter.GetBytes((short)Math.Round(v));
                case VK.I4: return BitConverter.GetBytes((int)Math.Round(v));
                case VK.I8: return BitConverter.GetBytes((long)Math.Round(v));
                case VK.F4: return BitConverter.GetBytes((float)v);
                case VK.F8: return BitConverter.GetBytes(v);
                case VK.Rgss: return BitConverter.GetBytes((int)Math.Round(v) * 2 + 1);
                case VK.Smi: return BitConverter.GetBytes((int)Math.Round(v) * 2);
            }
            return null;
        }

        public static string Format(double v, VK k)
        {
            if (double.IsNaN(v)) return "??";
            return IsFloat(k) ? v.ToString("0.###") : ((long)v).ToString();
        }

        public static string Name(VK k)
        {
            switch (k)
            {
                case VK.I4: return "4바이트";
                case VK.I2: return "2바이트";
                case VK.I1: return "1바이트";
                case VK.I8: return "8바이트";
                case VK.F4: return "소수(float)";
                case VK.F8: return "소수(double)";
                case VK.Rgss: return "쯔꾸르 ×2+1";
                case VK.Smi: return "쯔꾸르 ×2";
            }
            return "";
        }
    }

    class SnapRegion
    {
        public long Base;
        public byte[] Data;
        public BitArray[] Mask;
    }

    class Scanner
    {
        readonly IntPtr h;
        public readonly VK[] Kinds;
        readonly int minStep;

        // 결과 목록
        public long[] Addr = new long[0];
        public byte[] Kd = new byte[0];
        public double[] Prev = new double[0];
        public int Count;
        public bool Truncated;

        // '처음 값 모름' 상태 (메모리 통째로 기억)
        List<SnapRegion> snap;
        public bool SnapMode { get { return snap != null; } }
        public long SnapCount;

        public double Tol = 0.5;
        public Action<int> Progress;
        int lastP = -1;

        const int Max = 20000000;      // 결과 최대 개수
        const int ConvertAt = 2000000; // 이 개수 이하로 줄면 목록으로 전환
        const int Chunk = 4 << 20;
        const long Piece = 256L << 20;

        public Scanner(IntPtr h, VK[] kinds)
        {
            this.h = h;
            Kinds = kinds;
            minStep = kinds.Min(k => Val.Step(k));
        }

        // ---------- 결과 담기 ----------
        long[] bA; byte[] bK; double[] bP; int bN;

        void BStart(long cap)
        {
            int c = (int)Math.Max(1024, Math.Min(cap, Max));
            bA = new long[c]; bK = new byte[c]; bP = new double[c]; bN = 0;
        }

        bool BAdd(long a, VK k, double v)
        {
            if (bN == bA.Length)
            {
                if (bN >= Max) { Truncated = true; return false; }
                int n = (int)Math.Min((long)bN * 2, Max);
                Array.Resize(ref bA, n); Array.Resize(ref bK, n); Array.Resize(ref bP, n);
            }
            bA[bN] = a; bK[bN] = (byte)k; bP[bN] = v; bN++;
            return true;
        }

        void BFinish()
        {
            Addr = bA; Kd = bK; Prev = bP; Count = bN;
            bA = null; bK = null; bP = null;
        }

        void Report(long done, long total)
        {
            if (Progress == null || total <= 0) return;
            int p = (int)(done * 100 / total);
            if (p != lastP) { lastP = p; Progress(p); }
        }

        bool Read(long addr, byte[] buf, int len)
        {
            IntPtr rd;
            return Native.ReadProcessMemory(h, (IntPtr)addr, buf, (IntPtr)len, out rd) && rd.ToInt64() == len;
        }

        bool Test(Cmp c, double cur, double prev, double t, VK k)
        {
            if (double.IsNaN(cur)) return false;
            bool f = Val.IsFloat(k);
            switch (c)
            {
                case Cmp.Exact: return f ? Math.Abs(cur - t) < Tol : cur == t;
                case Cmp.Bigger: return cur > prev;
                case Cmp.Smaller: return cur < prev;
                case Cmp.Changed: return double.IsNaN(prev) || (f ? Math.Abs(cur - prev) > 1e-6 : cur != prev);
                case Cmp.Same: return f ? Math.Abs(cur - prev) <= 1e-6 : cur == prev;
            }
            return false;
        }

        // ---------- 메모리 영역 ----------
        public static List<KeyValuePair<long, long>> Regions(IntPtr h)
        {
            var list = new List<KeyValuePair<long, long>>();
            long a = 0x10000;
            Native.MBI m;
            var sz = (IntPtr)Marshal.SizeOf(typeof(Native.MBI));
            while (a < 0x7FFFFFFF0000L)
            {
                if (Native.VirtualQueryEx(h, (IntPtr)a, out m, sz) == IntPtr.Zero) break;
                long b = m.BaseAddress.ToInt64(), s = m.RegionSize.ToInt64();
                // 쓰기 가능한 메모리만 (게임 수치는 여기에 있음)
                if (m.State == 0x1000 && (m.Protect & 0xCC) != 0 && (m.Protect & 0x101) == 0 && m.Type != 0x40000)
                    for (long p = 0; p < s; p += Piece)
                        list.Add(new KeyValuePair<long, long>(b + p, Math.Min(Piece, s - p)));
                long n = b + s;
                if (n <= a) break;
                a = n;
            }
            return list;
        }

        public static double ReadValue(IntPtr h, long addr, VK k)
        {
            var b = new byte[8];
            IntPtr rd;
            if (!Native.ReadProcessMemory(h, (IntPtr)addr, b, (IntPtr)Val.Size(k), out rd)) return double.NaN;
            return Val.Decode(b, 0, k);
        }

        public static bool WriteValue(IntPtr h, long addr, VK k, double v)
        {
            var b = Val.Encode(v, k);
            IntPtr w;
            return Native.WriteProcessMemory(h, (IntPtr)addr, b, (IntPtr)b.Length, out w);
        }

        // ---------- 첫 검색 ----------
        public void First(Cmp c, double t)
        {
            lastP = -1; Truncated = false;
            var regs = Regions(h);
            long total = 0, done = 0;
            foreach (var r in regs) total += r.Value;

            if (c == Cmp.Unknown)
            {
                snap = new List<SnapRegion>();
                SnapCount = 0;
                foreach (var r in regs)
                {
                    var d = new byte[r.Value];
                    if (Read(r.Key, d, d.Length))
                    {
                        snap.Add(new SnapRegion { Base = r.Key, Data = d, Mask = new BitArray[Kinds.Length] });
                        foreach (var k in Kinds)
                            if (d.Length >= Val.Size(k)) SnapCount += (d.Length - Val.Size(k)) / Val.Step(k) + 1;
                    }
                    done += r.Value; Report(done, total);
                }
                return;
            }

            int n = Kinds.Length;
            var size = new int[n]; var step = new int[n];
            var fast = new bool[n]; var raw = new int[n];
            for (int j = 0; j < n; j++)
            {
                var k = Kinds[j];
                size[j] = Val.Size(k); step[j] = Val.Step(k);
                if ((k == VK.I4 || k == VK.Rgss || k == VK.Smi) && t == Math.Floor(t) && Math.Abs(t) < 1e9)
                {
                    fast[j] = true;
                    raw[j] = BitConverter.ToInt32(Val.Encode(t, k), 0);
                }
            }

            BStart(1 << 16);
            var buf = new byte[Chunk + 8];
            foreach (var r in regs)
            {
                for (long off = 0; off < r.Value; off += Chunk)
                {
                    int len = (int)Math.Min(Chunk + 8, r.Value - off);
                    if (Read(r.Key + off, buf, len))
                    {
                        int lim = Math.Min(Chunk, len);
                        for (int o = 0; o < lim; o += minStep)
                        {
                            for (int j = 0; j < n; j++)
                            {
                                if (o % step[j] != 0 || o + size[j] > len) continue;
                                double v;
                                if (fast[j])
                                {
                                    if (BitConverter.ToInt32(buf, o) != raw[j]) continue;
                                    v = t;
                                }
                                else
                                {
                                    v = Val.Decode(buf, o, Kinds[j]);
                                    if (!Test(c, v, double.NaN, t, Kinds[j])) continue;
                                }
                                if (!BAdd(r.Key + off + o, Kinds[j], v)) { BFinish(); return; }
                            }
                        }
                    }
                    done += Math.Min(Chunk, r.Value - off);
                    Report(done, total);
                }
            }
            BFinish();
        }

        // ---------- 다음 검색 ----------
        public void Next(Cmp c, double t)
        {
            lastP = -1; Truncated = false;
            if (snap != null) { NextSnap(c, t); return; }

            BStart(Count / 4);
            var buf = new byte[65536 + 16];
            int i = 0;
            while (i < Count)
            {
                long start = Addr[i], end = start;
                int j = i;
                while (j < Count && Addr[j] - start < 65536)
                {
                    long e = Addr[j] + Val.Size((VK)Kd[j]);
                    if (e > end) end = e;
                    j++;
                }
                bool ok = Read(start, buf, (int)(end - start));
                for (int q = i; q < j; q++)
                {
                    var k = (VK)Kd[q];
                    double cur = ok ? Val.Decode(buf, (int)(Addr[q] - start), k) : ReadValue(h, Addr[q], k);
                    if (Test(c, cur, Prev[q], t, k)) BAdd(Addr[q], k, cur);
                }
                i = j;
                Report(i, Count);
            }
            BFinish();
        }

        void NextSnap(Cmp c, double t)
        {
            long total = 0, done = 0;
            foreach (var r in snap) total += r.Data.Length;
            SnapCount = 0;
            var keep = new List<SnapRegion>();

            foreach (var r in snap)
            {
                int dlen = r.Data.Length;
                var cur = new byte[dlen];
                if (Read(r.Base, cur, dlen))
                {
                    long cnt = 0;
                    for (int j = 0; j < Kinds.Length; j++)
                    {
                        var k = Kinds[j];
                        int sz = Val.Size(k), st = Val.Step(k);
                        int slots = dlen >= sz ? (dlen - sz) / st + 1 : 0;
                        var old = r.Mask[j];
                        var m = new BitArray(slots);
                        for (int s = 0; s < slots; s++)
                        {
                            if (old != null && !old[s]) continue;
                            int o = s * st;
                            if (Test(c, Val.Decode(cur, o, k), Val.Decode(r.Data, o, k), t, k)) { m[s] = true; cnt++; }
                        }
                        r.Mask[j] = m;
                    }
                    if (cnt > 0) { r.Data = cur; keep.Add(r); SnapCount += cnt; }
                }
                done += dlen; Report(done, total);
            }
            snap = keep;

            if (SnapCount <= ConvertAt)
            {
                BStart(SnapCount);
                foreach (var r in snap)
                    for (int o = 0; o < r.Data.Length; o += minStep)
                        for (int j = 0; j < Kinds.Length; j++)
                        {
                            var k = Kinds[j];
                            int st = Val.Step(k);
                            if (o % st != 0) continue;
                            int s = o / st;
                            var m = r.Mask[j];
                            if (m == null || s >= m.Length || !m[s]) continue;
                            BAdd(r.Base + o, k, Val.Decode(r.Data, o, k));
                        }
                BFinish();
                snap = null;
            }
        }
    }
}
