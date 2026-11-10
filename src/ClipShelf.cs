// 截图盒 ClipShelf
// 截图（任何工具：Win+Shift+S、微信 Alt+A、QQ、Snipaste、PrintScreen……）一进剪贴板就自动收进这个置顶小盒子，
// 要发的时候从盒子里拖出去（可以点选多张一起拖）。全屏游戏 / 名单里的程序在前台时自动隐藏。
// 编译：build.cmd（系统自带 .NET Framework 4.x 的 csc，C# 5 语法）

using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("截图盒")]
[assembly: AssemblyProduct("截图盒 ClipShelf")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace ClipShelf
{
    // ------------------------------------------------------------------ Win32
    static class W32
    {
        public const int WM_CLIPBOARDUPDATE = 0x031D;
        public const int WM_MOUSEACTIVATE = 0x0021;
        public const int MA_NOACTIVATE = 3;
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;
        public const int WS_CAPTION = 0x00C00000;
        public const int WS_EX_TOPMOST = 0x8, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_NOOWNERZORDER = 0x200;
        public const uint WDA_NONE = 0x0, WDA_EXCLUDEFROMCAPTURE = 0x11;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

        [DllImport("user32.dll", SetLastError = true)] public static extern bool AddClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
        [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
        [DllImport("user32.dll")] public static extern IntPtr GetClipboardOwner();
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFO mi);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern bool SetWindowDisplayAffinity(IntPtr h, uint aff);
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int val, int size);
        public delegate bool EnumWindowsProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr l);
        public delegate void WinEventDelegate(IntPtr hook, uint ev, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventDelegate proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr h);
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
    }

    // ------------------------------------------------------------------ 日志 / 设置
    static class Log
    {
        public static string PathName;
        static readonly object L = new object();
        public static void W(string msg)
        {
            try
            {
                lock (L)
                {
                    if (PathName == null) return;
                    FileInfo fi = new FileInfo(PathName);
                    if (fi.Exists && fi.Length > 1024 * 1024)
                    {
                        string old = PathName + ".old";
                        if (File.Exists(old)) File.Delete(old);
                        File.Move(PathName, old);
                    }
                    File.AppendAllText(PathName, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + msg + Environment.NewLine, new UTF8Encoding(false));
                }
            }
            catch { }
        }
    }

    class Settings
    {
        readonly string file;
        readonly List<string> order = new List<string>();
        readonly Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Settings(string path)
        {
            file = path;
            try
            {
                if (!File.Exists(path)) return;
                foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    Put(line.Substring(0, i).Trim(), line.Substring(i + 1).Trim());
                }
            }
            catch (Exception ex) { Log.W("读设置失败: " + ex.Message); }
        }

        void Put(string k, string v) { if (!map.ContainsKey(k)) order.Add(k); map[k] = v; }
        public bool Has(string k) { return map.ContainsKey(k); }
        public string Get(string k, string def) { string v; return map.TryGetValue(k, out v) ? v : def; }
        public int GetInt(string k, int def) { int v; return int.TryParse(Get(k, ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : def; }
        public bool GetBool(string k, bool def)
        {
            string v = Get(k, "").ToLowerInvariant();
            if (v == "1" || v == "true" || v == "yes") return true;
            if (v == "0" || v == "false" || v == "no") return false;
            return def;
        }
        public void Set(string k, object v) { Put(k, Convert.ToString(v, CultureInfo.InvariantCulture)); }
        public void Set(string k, bool v) { Put(k, v ? "1" : "0"); }

        public void Save()
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# 截图盒设置。手动改完请右键托盘图标「退出」再重新打开。");
                sb.AppendLine("# GameProcs = 这些程序在前台时盒子自动隐藏（逗号分隔，进程名）");
                sb.AppendLine("# MaxItems  = 盒子最多留几张，更早的自动删掉");
                sb.AppendLine("# OpenMode  = click（点一下展开）或 hover（鼠标划过就展开）");
                sb.AppendLine("# StayOpenSeconds = 截图后保持展开多少秒（每截一张重新计时）");
                foreach (string k in order) sb.AppendLine(k + "=" + map[k]);
                File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
            }
            catch (Exception ex) { Log.W("写设置失败: " + ex.Message); }
        }
    }

    static class Autostart
    {
        const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "ClipShelf";
        public static bool IsOn()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(Key))
                return k != null && k.GetValue(Name) != null;
        }
        public static void Set(bool on)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(Key))
            {
                if (on) k.SetValue(Name, "\"" + Application.ExecutablePath + "\"");
                else k.DeleteValue(Name, false);
            }
        }
    }

    static class Shortcut
    {
        public static void Create(string lnk, string target, string workDir, string icon, string desc)
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object sh = Activator.CreateInstance(t);
            object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, sh, new object[] { lnk });
            Type st = sc.GetType();
            st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
            st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { workDir });
            st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { icon });
            st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { desc });
            st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
            Marshal.FinalReleaseComObject(sc);
            Marshal.FinalReleaseComObject(sh);
        }
    }

    // ------------------------------------------------------------------ 外观
    static class Theme
    {
        public static readonly Color Body = Color.FromArgb(30, 31, 34);
        public static readonly Color Header = Color.FromArgb(40, 42, 46);
        public static readonly Color HeaderHover = Color.FromArgb(60, 63, 69);
        public static readonly Color Card = Color.FromArgb(43, 45, 49);
        public static readonly Color CardHover = Color.FromArgb(53, 56, 61);
        public static readonly Color Line = Color.FromArgb(66, 69, 76);
        public static readonly Color ThumbBg = Color.FromArgb(21, 22, 24);
        public static readonly Color Text = Color.FromArgb(240, 241, 243);
        public static readonly Color Sub = Color.FromArgb(152, 157, 166);
        public static readonly Color Accent = Color.FromArgb(76, 141, 255);
        public static readonly Color Danger = Color.FromArgb(240, 82, 82);
        public static readonly Color Pill = Color.FromArgb(62, 66, 74);
        public static readonly Color Scroll = Color.FromArgb(96, 101, 111);
    }

    class Metrics
    {
        public readonly float S;
        public readonly int FormW, HeaderH, Pad, Gap, CardPad, CapH, InnerW, MinBoxH, MaxBoxH, Radius, EmptyH, Badge, ChipW;
        public Metrics(float s)
        {
            S = s;
            FormW = Px(204); HeaderH = Px(32); Pad = Px(8); Gap = Px(7); CardPad = Px(5); CapH = Px(19);
            InnerW = FormW - 2 - 2 * Pad - 2 * CardPad;   // 2 = 窗口 1px 边框 × 2
            MinBoxH = Px(40); MaxBoxH = Px(150); Radius = Px(6); EmptyH = Px(96); Badge = Px(20); ChipW = Px(66);
        }
        public int Px(float v) { return (int)Math.Round(v * S); }
    }

    static class G
    {
        public static GraphicsPath Round(RectangleF r, float rad)
        {
            GraphicsPath p = new GraphicsPath();
            if (rad <= 0.5f) { p.AddRectangle(r); return p; }
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    // ------------------------------------------------------------------ 截图条目
    class ShotItem
    {
        public string Path;
        public string Hash;
        public DateTime Time;
        public int W, H;
        public Bitmap Thumb;
        public int BoxH;
        public DateTime FlashUntil;
    }

    static class Shots
    {
        public static string HashOf(Bitmap bmp)
        {
            Rectangle r = new Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData d = bmp.LockBits(r, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int rowBytes = bmp.Width * 4;
                byte[] row = new byte[rowBytes];
                using (MD5 md5 = MD5.Create())
                {
                    byte[] dims = BitConverter.GetBytes(((long)bmp.Width << 32) | (uint)bmp.Height);
                    md5.TransformBlock(dims, 0, dims.Length, null, 0);
                    for (int y = 0; y < bmp.Height; y++)
                    {
                        Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), row, 0, rowBytes);
                        md5.TransformBlock(row, 0, rowBytes, null, 0);
                    }
                    md5.TransformFinalBlock(new byte[0], 0, 0);
                    return BitConverter.ToString(md5.Hash).Replace("-", "").Substring(0, 12).ToLowerInvariant();
                }
            }
            finally { bmp.UnlockBits(d); }
        }

        public static Bitmap LoadFile(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            using (MemoryStream ms = new MemoryStream(b))
            using (Image img = Image.FromStream(ms))
                return new Bitmap(img);
        }

        public static ShotItem Make(string path, string hash, DateTime time, Bitmap src, Metrics m)
        {
            ShotItem it = new ShotItem();
            it.Path = path; it.Hash = hash; it.Time = time; it.W = src.Width; it.H = src.Height;
            double scale = Math.Min(1.0, Math.Min((double)m.InnerW / src.Width, (double)m.MaxBoxH / src.Height));
            int fw = Math.Max(1, (int)Math.Round(src.Width * scale));
            int fh = Math.Max(1, (int)Math.Round(src.Height * scale));
            Bitmap th = new Bitmap(fw, fh, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(th))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                using (ImageAttributes ia = new ImageAttributes())
                {
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(src, new Rectangle(0, 0, fw, fh), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, ia);
                }
            }
            it.Thumb = th;
            it.BoxH = Math.Max(fh, m.MinBoxH);
            return it;
        }

        // shot_20261110_153012_123_ab12cd34ef56.png
        public static bool ParseName(string path, out DateTime time, out string hash)
        {
            time = DateTime.MinValue; hash = null;
            string[] p = System.IO.Path.GetFileNameWithoutExtension(path).Split('_');
            if (p.Length < 5 || p[0] != "shot") return false;
            if (!DateTime.TryParseExact(p[1] + p[2] + p[3], "yyyyMMddHHmmssfff", CultureInfo.InvariantCulture, DateTimeStyles.None, out time)) return false;
            hash = p[4];
            return hash.Length >= 8;
        }
    }

    // ------------------------------------------------------------------ 全屏 / 游戏判断
    static class FsDetect
    {
        static readonly HashSet<string> ShellClasses = new HashSet<string>(StringComparer.Ordinal)
        {
            "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
            "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland"
        };

        public static string ClassOf(IntPtr h) { StringBuilder sb = new StringBuilder(256); W32.GetClassName(h, sb, sb.Capacity); return sb.ToString(); }
        public static string TitleOf(IntPtr h) { StringBuilder sb = new StringBuilder(256); W32.GetWindowText(h, sb, sb.Capacity); return sb.ToString(); }
        public static bool IsShell(IntPtr h) { return ShellClasses.Contains(ClassOf(h)); }

        public static bool CoversMonitor(IntPtr h, out W32.RECT r, out W32.RECT mon)
        {
            mon = new W32.RECT();
            if (!W32.GetWindowRect(h, out r)) return false;
            W32.MONITORINFO mi = new W32.MONITORINFO();
            mi.cbSize = Marshal.SizeOf(typeof(W32.MONITORINFO));
            if (!W32.GetMonitorInfo(W32.MonitorFromWindow(h, 2), ref mi)) return false;
            mon = mi.rcMonitor;
            return r.Left <= mon.Left && r.Top <= mon.Top && r.Right >= mon.Right && r.Bottom >= mon.Bottom;
        }

        // 盖满整块屏幕、又不是“带标题栏的普通最大化窗口”= 全屏（全屏游戏、无边框窗口化游戏、F11、全屏视频、放映）
        public static bool IsFullscreen(IntPtr h)
        {
            if (h == IntPtr.Zero || !W32.IsWindowVisible(h) || W32.IsIconic(h) || IsShell(h)) return false;
            W32.RECT r, mon;
            if (!CoversMonitor(h, out r, out mon)) return false;
            int style = W32.GetWindowLong(h, W32.GWL_STYLE);
            bool caption = (style & W32.WS_CAPTION) == W32.WS_CAPTION;
            if (caption && W32.IsZoomed(h)) return false;
            return true;
        }

        public static bool D3DFullscreen()
        {
            int st;
            return W32.SHQueryUserNotificationState(out st) == 0 && st == 3; // QUNS_RUNNING_D3D_FULL_SCREEN
        }

        public static string ProcName(uint pid)
        {
            try { using (Process p = Process.GetProcessById((int)pid)) return (p.ProcessName + ".exe").ToLowerInvariant(); }
            catch { return ""; }
        }
    }

    // ------------------------------------------------------------------ 图标
    static class AppIcon
    {
        public static Bitmap Draw(int s)
        {
            Bitmap b = new Bitmap(s, s, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(b))
            {
                g.Clear(Color.Transparent);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float pad = s * 0.04f;
                RectangleF bg = new RectangleF(pad, pad, s - 2 * pad, s - 2 * pad);
                using (GraphicsPath p = G.Round(bg, s * 0.22f))
                using (LinearGradientBrush br = new LinearGradientBrush(bg, Color.FromArgb(100, 160, 255), Color.FromArgb(44, 102, 222), 90f))
                    g.FillPath(br, p);
                if (s >= 24)
                {
                    RectangleF back = new RectangleF(s * 0.33f, s * 0.18f, s * 0.47f, s * 0.38f);
                    using (GraphicsPath p = G.Round(back, s * 0.06f))
                    using (SolidBrush br = new SolidBrush(Color.FromArgb(140, 255, 255, 255)))
                        g.FillPath(br, p);
                }
                RectangleF c = s >= 24 ? new RectangleF(s * 0.18f, s * 0.33f, s * 0.52f, s * 0.44f)
                                       : new RectangleF(s * 0.17f, s * 0.24f, s * 0.66f, s * 0.52f);
                using (GraphicsPath p = G.Round(c, s * 0.07f)) g.FillPath(Brushes.White, p);
                using (SolidBrush ab = new SolidBrush(Color.FromArgb(44, 102, 222)))
                {
                    PointF[] m1 = { new PointF(c.Left + c.Width * 0.12f, c.Bottom - c.Height * 0.16f),
                                    new PointF(c.Left + c.Width * 0.40f, c.Top + c.Height * 0.36f),
                                    new PointF(c.Left + c.Width * 0.66f, c.Bottom - c.Height * 0.16f) };
                    g.FillPolygon(ab, m1);
                    PointF[] m2 = { new PointF(c.Left + c.Width * 0.50f, c.Bottom - c.Height * 0.16f),
                                    new PointF(c.Left + c.Width * 0.68f, c.Top + c.Height * 0.55f),
                                    new PointF(c.Left + c.Width * 0.88f, c.Bottom - c.Height * 0.16f) };
                    g.FillPolygon(ab, m2);
                    float sd = c.Width * 0.16f;
                    g.FillEllipse(ab, c.Left + c.Width * 0.70f, c.Top + c.Height * 0.16f, sd, sd);
                }
            }
            return b;
        }

        public static byte[] BuildIco()
        {
            int[] sizes = { 16, 20, 24, 32, 40, 48, 64, 256 };
            List<byte[]> imgs = new List<byte[]>();
            foreach (int s in sizes)
                using (Bitmap b = Draw(s)) imgs.Add(s >= 256 ? Png(b) : Dib(b));
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryWriter w = new BinaryWriter(ms);
                w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int s = sizes[i];
                    w.Write((byte)(s >= 256 ? 0 : s)); w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)0); w.Write((byte)0);
                    w.Write((short)1); w.Write((short)32);
                    w.Write(imgs[i].Length); w.Write(offset);
                    offset += imgs[i].Length;
                }
                foreach (byte[] im in imgs) w.Write(im);
                w.Flush();
                return ms.ToArray();
            }
        }

        static byte[] Png(Bitmap b) { using (MemoryStream ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); return ms.ToArray(); } }

        static byte[] Dib(Bitmap b)
        {
            int w = b.Width, h = b.Height, maskStride = ((w + 31) / 32) * 4;
            byte[] px = new byte[w * h * 4];
            byte[] mask = new byte[maskStride * h];
            BitmapData d = b.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                for (int y = 0; y < h; y++)
                {
                    int dst = (h - 1 - y) * w * 4;   // 自下而上
                    Marshal.Copy(new IntPtr(d.Scan0.ToInt64() + (long)y * d.Stride), px, dst, w * 4);
                    for (int x = 0; x < w; x++)
                        if (px[dst + x * 4 + 3] < 128) mask[(h - 1 - y) * maskStride + x / 8] |= (byte)(0x80 >> (x % 8));
                }
            }
            finally { b.UnlockBits(d); }
            using (MemoryStream ms = new MemoryStream())
            {
                BinaryWriter bw = new BinaryWriter(ms);
                bw.Write(40); bw.Write(w); bw.Write(h * 2); bw.Write((short)1); bw.Write((short)32);
                bw.Write(0); bw.Write(px.Length + mask.Length); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
                bw.Write(px); bw.Write(mask);
                bw.Flush();
                return ms.ToArray();
            }
        }
    }

    // ------------------------------------------------------------------ 资源管理器同款拖拽数据
    static class ShellData
    {
        [ComImport, Guid("b63ea76d-1f85-456f-a19c-48159efa858b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItemArray
        {
            [PreserveSig]
            int BindToHandler(IntPtr pbc, [In] ref Guid bhid, [In] ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr ILCreateFromPathW(string path);
        [DllImport("shell32.dll")] static extern void ILFree(IntPtr pidl);
        [DllImport("shell32.dll")] static extern int SHCreateShellItemArrayFromIDLists(uint cidl, IntPtr[] rgpidl, out IShellItemArray arr);

        public static System.Runtime.InteropServices.ComTypes.IDataObject For(IList<string> paths)
        {
            IntPtr[] pidls = new IntPtr[paths.Count];
            try
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    pidls[i] = ILCreateFromPathW(paths[i]);
                    if (pidls[i] == IntPtr.Zero) throw new IOException("ILCreateFromPath 失败: " + paths[i]);
                }
                IShellItemArray arr;
                Marshal.ThrowExceptionForHR(SHCreateShellItemArrayFromIDLists((uint)pidls.Length, pidls, out arr));
                Guid bhid = new Guid("B8C0BD9F-ED24-455c-83E6-D5390C4FE8C4");   // BHID_DataObject
                Guid iid = new Guid("0000010e-0000-0000-C000-000000000046");    // IID_IDataObject
                object o;
                Marshal.ThrowExceptionForHR(arr.BindToHandler(IntPtr.Zero, ref bhid, ref iid, out o));
                return (System.Runtime.InteropServices.ComTypes.IDataObject)o;
            }
            finally
            {
                foreach (IntPtr pd in pidls) if (pd != IntPtr.Zero) ILFree(pd);
            }
        }
    }

    // ------------------------------------------------------------------ 拖拽时的光标（带缩略图）
    class DragCursorSet : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        struct ICONINFO { [MarshalAs(UnmanagedType.Bool)] public bool fIcon; public int xHotspot; public int yHotspot; public IntPtr hbmMask; public IntPtr hbmColor; }
        [DllImport("user32.dll")] static extern IntPtr CreateIconIndirect(ref ICONINFO ii);
        [DllImport("user32.dll")] static extern bool GetIconInfo(IntPtr h, out ICONINFO ii);
        [DllImport("user32.dll")] static extern bool DestroyCursor(IntPtr h);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr h);

        public Cursor Ok, No;
        IntPtr hOk, hNo;

        public static DragCursorSet Build(List<ShotItem> list, Metrics m)
        {
            DragCursorSet s = new DragCursorSet();
            s.hOk = Make(list, m, true);
            s.hNo = Make(list, m, false);
            s.Ok = new Cursor(s.hOk);
            s.No = new Cursor(s.hNo);
            return s;
        }

        static IntPtr Make(List<ShotItem> list, Metrics m, bool ok)
        {
            int size = m.Px(112);
            using (Bitmap bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    int ox = m.Px(13), oy = m.Px(18), step = m.Px(5);
                    int n = Math.Min(3, list.Count);
                    int availW = size - ox - m.Px(4) - (n - 1) * step, availH = size - oy - m.Px(4) - (n - 1) * step;
                    float alpha = ok ? 0.92f : 0.40f;
                    Rectangle front = Rectangle.Empty;
                    for (int k = n - 1; k >= 0; k--)
                    {
                        Bitmap th = list[k].Thumb;
                        if (th == null) continue;
                        double sc = Math.Min(1.0, Math.Min((double)availW / th.Width, (double)availH / th.Height));
                        int w = Math.Max(1, (int)(th.Width * sc)), h = Math.Max(1, (int)(th.Height * sc));
                        Rectangle r = new Rectangle(ox + k * step, oy + k * step, w, h);
                        using (SolidBrush sh = new SolidBrush(Color.FromArgb((int)(80 * alpha), 0, 0, 0)))
                            g.FillRectangle(sh, r.X + m.Px(2), r.Y + m.Px(2), w, h);
                        ColorMatrix cm = new ColorMatrix();
                        cm.Matrix33 = alpha;
                        using (ImageAttributes ia = new ImageAttributes())
                        {
                            ia.SetColorMatrix(cm);
                            g.DrawImage(th, r, 0, 0, th.Width, th.Height, GraphicsUnit.Pixel, ia);
                        }
                        using (Pen pen = new Pen(Color.FromArgb((int)(255 * alpha), 255, 255, 255), Math.Max(1, m.Px(1))))
                            g.DrawRectangle(pen, r.X, r.Y, w - 1, h - 1);
                        if (k == 0) front = r;
                    }
                    if (list.Count > 1 && front != Rectangle.Empty)
                    {
                        int d = m.Px(20);
                        Rectangle br = new Rectangle(Math.Min(front.Right - d / 2, size - d - 1), Math.Max(0, front.Y - d / 2), d, d);
                        using (SolidBrush b = new SolidBrush(ok ? Theme.Accent : Color.Gray)) g.FillEllipse(b, br);
                        using (Font f = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                        using (StringFormat sf = new StringFormat())
                        {
                            sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                            g.DrawString(list.Count.ToString(), f, Brushes.White, br, sf);
                        }
                    }
                    if (!ok)
                    {
                        int d = m.Px(14);
                        Rectangle nr = new Rectangle(m.Px(12), m.Px(2), d, d);
                        using (Pen pen = new Pen(Theme.Danger, Math.Max(2, m.Px(2))))
                        {
                            g.DrawEllipse(pen, nr);
                            g.DrawLine(pen, nr.Left + d * 0.2f, nr.Top + d * 0.2f, nr.Right - d * 0.2f, nr.Bottom - d * 0.2f);
                        }
                    }
                    // 箭头本身
                    float s = m.S;
                    PointF[] arrow = {
                        new PointF(1, 1), new PointF(1, 1 + 17 * s), new PointF(1 + 4.2f * s, 1 + 13 * s),
                        new PointF(1 + 7 * s, 1 + 19.5f * s), new PointF(1 + 9.6f * s, 1 + 18.4f * s),
                        new PointF(1 + 6.9f * s, 1 + 12.2f * s), new PointF(1 + 12 * s, 1 + 12.2f * s) };
                    g.FillPolygon(Brushes.White, arrow);
                    using (Pen pen = new Pen(Color.Black, Math.Max(1f, s * 0.9f))) g.DrawPolygon(pen, arrow);
                }
                IntPtr hIcon = bmp.GetHicon();
                ICONINFO ii;
                GetIconInfo(hIcon, out ii);
                ii.fIcon = false; ii.xHotspot = 1; ii.yHotspot = 1;
                IntPtr hCur = CreateIconIndirect(ref ii);
                DeleteObject(ii.hbmColor); DeleteObject(ii.hbmMask);
                W32.DestroyIcon(hIcon);
                return hCur;
            }
        }

        public void Dispose()
        {
            if (hOk != IntPtr.Zero) { DestroyCursor(hOk); hOk = IntPtr.Zero; }
            if (hNo != IntPtr.Zero) { DestroyCursor(hNo); hNo = IntPtr.Zero; }
        }
    }

    // ------------------------------------------------------------------ 标题栏
    class HeaderBar : Control
    {
        enum Btn { None, Menu, Clear, Collapse }

        readonly Metrics m;
        readonly Font titleFont, pillFont, iconFont, chipFont;
        readonly ToolTip tip = new ToolTip();
        readonly Bitmap chipIcon;
        public int Count, SelCount;
        public bool Collapsed, Paused, ConfirmClear, ToastWarn;
        public string Toast;
        public DateTime FlashUntil;
        public int IgnoreClicksUntil;
        public float StayFraction;      // 截图后保持展开的剩余比例，画成标题栏底部的蓝线
        public bool HoverMode;
        Btn hoverBtn = Btn.None, downBtn = Btn.None;
        bool moving, movedFar, hot;
        Point moveStartCursor, moveStartForm;
        string tipText = "";

        public event Action<Point> MenuClicked;
        public event Action ClearClicked, CollapseClicked, ChipClicked, MoveEnded, ChipHoverStart, ChipHoverEnd;

        public HeaderBar(Metrics m)
        {
            this.m = m;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            titleFont = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            pillFont = new Font("Segoe UI", 7.5f, FontStyle.Bold);
            chipFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            iconFont = new Font(PickIconFont(), 9.5f);
            chipIcon = AppIcon.Draw(m.Px(18));
            tip.ShowAlways = true;
            BackColor = Theme.Header;
        }

        static string PickIconFont()
        {
            using (InstalledFontCollection fc = new InstalledFontCollection())
                foreach (FontFamily f in fc.Families)
                    if (f.Name == "Segoe Fluent Icons") return f.Name;
            return "Segoe MDL2 Assets";
        }

        Rectangle R(Btn b)
        {
            int bw = m.Px(28), right = Width - m.Px(3), top = m.Px(3), h = Height - m.Px(6);
            switch (b)
            {
                case Btn.Collapse: return new Rectangle(right - bw, top, bw, h);
                case Btn.Clear: return new Rectangle(right - 2 * bw, top, bw, h);
                case Btn.Menu: return new Rectangle(right - 3 * bw, top, bw, h);
            }
            return Rectangle.Empty;
        }

        Btn HitBtn(Point p)
        {
            if (Collapsed) return Btn.None;
            if (R(Btn.Collapse).Contains(p)) return Btn.Collapse;
            if (R(Btn.Clear).Contains(p)) return Btn.Clear;
            if (R(Btn.Menu).Contains(p)) return Btn.Menu;
            return Btn.None;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (Collapsed) { PaintChip(g); return; }
            g.Clear(Theme.Header);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Rectangle rm = R(Btn.Menu);
            int textL = m.Px(10);
            Rectangle tr = new Rectangle(textL, 0, rm.Left - m.Px(2) - textL, Height);
            TextFormatFlags fl = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis |
                                 TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            if (ConfirmClear)
                TextRenderer.DrawText(g, "再点一下清空", titleFont, tr, Theme.Danger, fl);
            else if (!string.IsNullOrEmpty(Toast))
                TextRenderer.DrawText(g, Toast, titleFont, tr, ToastWarn ? Theme.Danger : Theme.Accent, fl);
            else if (SelCount > 0)
                TextRenderer.DrawText(g, "已选 " + SelCount + " 张 · 一起拖", titleFont, tr, Theme.Accent, fl);
            else
            {
                string t = Paused ? "截图盒 · 已暂停" : "截图盒";
                TextRenderer.DrawText(g, t, titleFont, tr, Paused ? Theme.Sub : Theme.Text, fl);
                if (Count > 0 && !Paused)
                {
                    Size ts = TextRenderer.MeasureText(g, t, titleFont, tr.Size, fl);
                    string c = Count.ToString();
                    Size cs = TextRenderer.MeasureText(g, c, pillFont, Size.Empty, TextFormatFlags.NoPadding);
                    int ph = m.Px(16), pw = Math.Max(ph, cs.Width + m.Px(10));
                    Rectangle pr = new Rectangle(textL + ts.Width + m.Px(6), (Height - ph) / 2, pw, ph);
                    using (GraphicsPath p = G.Round(pr, ph / 2f))
                    using (SolidBrush b = new SolidBrush(Theme.Pill))
                        g.FillPath(b, p);
                    TextRenderer.DrawText(g, c, pillFont, pr, Theme.Text,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                }
            }
            DrawBtn(g, Btn.Menu, "\uE712");
            DrawBtn(g, Btn.Clear, "\uE74D");
            DrawBtn(g, Btn.Collapse, "\uE70E");
            if (StayFraction > 0)
                using (SolidBrush b = new SolidBrush(Theme.Accent))
                    g.FillRectangle(b, 0, Height - m.Px(2), (int)(Width * Math.Min(1f, StayFraction)), m.Px(2));
        }

        // 收起状态：一个小块，图标 + 张数；来新截图时闪一下蓝
        void PaintChip(Graphics g)
        {
            g.Clear(hot ? Theme.HeaderHover : Theme.Header);
            double fl = (FlashUntil - DateTime.Now).TotalMilliseconds;
            if (fl > 0)
                using (SolidBrush b = new SolidBrush(Color.FromArgb((int)Math.Min(220, 220 * fl / 1400.0), Theme.Accent)))
                    g.FillRectangle(b, ClientRectangle);
            int ix = m.Px(9), iy = (Height - chipIcon.Height) / 2;
            g.DrawImage(chipIcon, new Rectangle(ix, iy, chipIcon.Width, chipIcon.Height));
            string t = Paused ? "停" : (Count > 99 ? "99+" : Count.ToString());
            int tl = ix + chipIcon.Width + m.Px(3);
            Rectangle tr = new Rectangle(tl, 0, Width - tl - m.Px(6), Height);
            TextRenderer.DrawText(g, t, Paused ? titleFont : chipFont, tr, Paused ? Theme.Sub : Theme.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        void DrawBtn(Graphics g, Btn b, string glyph)
        {
            Rectangle r = R(b);
            bool h = hoverBtn == b;
            bool danger = b == Btn.Clear && ConfirmClear;
            if (h || danger)
                using (GraphicsPath p = G.Round(r, m.Px(5)))
                using (SolidBrush br = new SolidBrush(danger ? Theme.Danger : Theme.HeaderHover))
                    g.FillPath(br, p);
            TextRenderer.DrawText(g, glyph, iconFont, r, (h || danger) ? Theme.Text : Theme.Sub,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Right) { if (MenuClicked != null) MenuClicked(PointToScreen(e.Location)); return; }
            if (e.Button != MouseButtons.Left) return;
            downBtn = HitBtn(e.Location);
            if (downBtn != Btn.None) return;
            if (!Collapsed && e.Clicks >= 2) { moving = false; if (CollapseClicked != null) CollapseClicked(); return; }
            Form f = FindForm();
            if (f == null) return;
            moving = true; movedFar = false;
            moveStartCursor = Cursor.Position;
            moveStartForm = f.Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (moving)
            {
                Point c = Cursor.Position;
                int dx = c.X - moveStartCursor.X, dy = c.Y - moveStartCursor.Y;
                if (!movedFar)
                {
                    Size ds = SystemInformation.DragSize;
                    if (Math.Abs(dx) <= ds.Width && Math.Abs(dy) <= ds.Height) return;
                    movedFar = true;
                }
                Form f = FindForm();
                if (f != null) f.Location = new Point(moveStartForm.X + dx, moveStartForm.Y + dy);
                return;
            }
            Btn hb = HitBtn(e.Location);
            string t = Collapsed ? (HoverMode ? "截图盒：鼠标停一下就展开" : "截图盒：点一下展开，按住可拖动")
                     : hb == Btn.Menu ? "菜单" : hb == Btn.Clear ? "清空全部" : hb == Btn.Collapse ? "收起" : "";
            if (t != tipText) { tipText = t; tip.SetToolTip(this, t); }
            if (hb != hoverBtn) { hoverBtn = hb; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (moving)
            {
                moving = false;
                if (movedFar) { if (MoveEnded != null) MoveEnded(); }
                else if (Collapsed && e.Button == MouseButtons.Left && ChipClicked != null) ChipClicked();
                return;
            }
            if (e.Button != MouseButtons.Left) return;
            Btn up = HitBtn(e.Location);
            Btn down = downBtn;
            downBtn = Btn.None;
            if (up == Btn.None || up != down) return;
            if (unchecked(Environment.TickCount - IgnoreClicksUntil) < 0) return;   // 刚展开，防止连点把它又收回去
            if (up == Btn.Menu && MenuClicked != null) { Rectangle r = R(Btn.Menu); MenuClicked(PointToScreen(new Point(r.Left, r.Bottom))); }
            else if (up == Btn.Clear && ClearClicked != null) ClearClicked();
            else if (up == Btn.Collapse && CollapseClicked != null) CollapseClicked();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            hot = true;
            if (Collapsed) { Invalidate(); if (ChipHoverStart != null) ChipHoverStart(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hot = false;
            hoverBtn = Btn.None;
            Invalidate();
            if (ChipHoverEnd != null) ChipHoverEnd();
        }
    }

    // ------------------------------------------------------------------ 缩略图列表
    class ShelfView : Control
    {
        readonly List<ShotItem> items;
        readonly Metrics m;
        public readonly List<ShotItem> Selected = new List<ShotItem>();
        readonly List<Rectangle> rects = new List<Rectangle>();
        readonly Font capFont, badgeFont, hintFont;
        int scroll, contentH;
        ShotItem hover; bool hoverX;
        ShotItem downItem, lastToggled; Point downPt; bool downX, suppressUp, leftDown;

        public event Action SelectionChanged;
        public event Action<ShotItem> CopyRequested;
        public event Action<ShotItem> DeleteRequested;
        public event Action<ShotItem, Point> ItemMenuRequested;
        public Func<List<ShotItem>, DragDropEffects> DragRequested;

        public ShelfView(List<ShotItem> items, Metrics m)
        {
            this.items = items; this.m = m;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            SetStyle(ControlStyles.Selectable, false);
            capFont = new Font("Microsoft YaHei UI", 7.5f);
            badgeFont = new Font("Segoe UI", 8f, FontStyle.Bold);
            hintFont = new Font("Microsoft YaHei UI", 9f);
            BackColor = Theme.Body;
        }

        public int ContentHeight { get { return contentH; } }

        public void Relayout()
        {
            rects.Clear();
            int y = m.Pad;
            foreach (ShotItem it in items)
            {
                int h = m.CardPad * 2 + it.BoxH + m.CapH;
                rects.Add(new Rectangle(m.Pad, y, m.InnerW + 2 * m.CardPad, h));
                y += h + m.Gap;
            }
            contentH = items.Count == 0 ? m.EmptyH : y - m.Gap + m.Pad;
            int before = Selected.Count;
            Selected.RemoveAll(delegate(ShotItem s) { return !items.Contains(s); });
            if (hover != null && !items.Contains(hover)) hover = null;
            ClampScroll();
            Invalidate();
            if (before != Selected.Count) RaiseSel();
        }

        void ClampScroll()
        {
            int max = Math.Max(0, contentH - Height);
            if (scroll > max) scroll = max;
            if (scroll < 0) scroll = 0;
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); ClampScroll(); }
        public void ScrollToTop() { scroll = 0; Invalidate(); }
        void RaiseSel() { if (SelectionChanged != null) SelectionChanged(); }

        Rectangle BoxOf(Rectangle card, ShotItem it) { return new Rectangle(card.X + m.CardPad, card.Y + m.CardPad, m.InnerW, it.BoxH); }
        Rectangle XRect(Rectangle box) { return new Rectangle(box.Right - m.Px(4) - m.Badge, box.Y + m.Px(4), m.Badge, m.Badge); }

        int IndexAt(Point p, out bool onX)
        {
            onX = false;
            for (int i = 0; i < rects.Count && i < items.Count; i++)
            {
                Rectangle r = rects[i];
                r.Y -= scroll;
                if (!r.Contains(p)) continue;
                Rectangle x = XRect(BoxOf(r, items[i]));
                x.Inflate(m.Px(2), m.Px(2));
                onX = x.Contains(p);
                return i;
            }
            return -1;
        }

        public void DebugHover(int index, bool onX)
        {
            hover = index >= 0 && index < items.Count ? items[index] : null;
            hoverX = onX;
            Invalidate();
        }

        static string TimeText(DateTime t, DateTime now)
        {
            return t.Date == now.Date ? t.ToString("HH:mm:ss") : t.ToString("MM-dd HH:mm");
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Body);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (items.Count == 0)
            {
                Rectangle hr = new Rectangle(m.Pad, 0, Width - 2 * m.Pad, Height);
                TextRenderer.DrawText(g, "截图后会自动出现在这里\n拖出去就能发，可点选多张", hintFont, hr, Theme.Sub,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return;
            }
            DateTime now = DateTime.Now;
            for (int i = 0; i < items.Count && i < rects.Count; i++)
            {
                ShotItem it = items[i];
                Rectangle r = rects[i];
                r.Y -= scroll;
                if (r.Bottom < 0 || r.Top > Height) continue;
                bool sel = Selected.Contains(it), hov = it == hover;

                using (GraphicsPath p = G.Round(r, m.Radius))
                using (SolidBrush b = new SolidBrush(hov ? Theme.CardHover : Theme.Card))
                    g.FillPath(b, p);

                Rectangle box = BoxOf(r, it);
                using (SolidBrush b = new SolidBrush(Theme.ThumbBg)) g.FillRectangle(b, box);
                if (it.Thumb != null)
                {
                    InterpolationMode im = g.InterpolationMode; PixelOffsetMode pm = g.PixelOffsetMode;
                    g.InterpolationMode = InterpolationMode.NearestNeighbor;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    int tx = box.X + (box.Width - it.Thumb.Width) / 2, ty = box.Y + (box.Height - it.Thumb.Height) / 2;
                    g.DrawImage(it.Thumb, new Rectangle(tx, ty, it.Thumb.Width, it.Thumb.Height));
                    g.InterpolationMode = im; g.PixelOffsetMode = pm;
                }

                Rectangle cap = new Rectangle(box.X + m.Px(1), box.Bottom, box.Width - m.Px(2), m.CapH);
                TextFormatFlags cf = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
                TextRenderer.DrawText(g, TimeText(it.Time, now), capFont, cap, Theme.Sub, cf | TextFormatFlags.Left);
                TextRenderer.DrawText(g, it.W + "×" + it.H, capFont, cap, Theme.Sub, cf | TextFormatFlags.Right);

                // 边框：选中 > 新到闪一下 > 悬停 > 普通
                double flash = (it.FlashUntil - now).TotalMilliseconds;
                Color border; float bw;
                if (sel) { border = Theme.Accent; bw = m.Px(2); }
                else if (flash > 0) { border = Color.FromArgb((int)Math.Min(255, 255 * flash / 900.0), Theme.Accent); bw = m.Px(2); }
                else if (hov) { border = Color.FromArgb(110, 116, 128); bw = Math.Max(1, m.Px(1)); }
                else { border = Theme.Line; bw = 1; }
                RectangleF rf = new RectangleF(r.X + bw / 2f, r.Y + bw / 2f, r.Width - bw, r.Height - bw);
                using (GraphicsPath p = G.Round(rf, m.Radius))
                using (Pen pen = new Pen(border, bw))
                    g.DrawPath(pen, p);

                // 左上角：选中序号 / 悬停时的空心圈
                Rectangle badge = new Rectangle(box.X + m.Px(4), box.Y + m.Px(4), m.Badge, m.Badge);
                if (sel)
                {
                    using (SolidBrush b = new SolidBrush(Theme.Accent)) g.FillEllipse(b, badge);
                    using (Pen pen = new Pen(Color.White, Math.Max(1, m.Px(1)))) g.DrawEllipse(pen, badge);
                    TextRenderer.DrawText(g, (Selected.IndexOf(it) + 1).ToString(), badgeFont, badge, Color.White,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
                }
                else if (hov)
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(110, 0, 0, 0))) g.FillEllipse(b, badge);
                    using (Pen pen = new Pen(Color.FromArgb(220, 255, 255, 255), Math.Max(1, m.Px(1.3f)))) g.DrawEllipse(pen, badge);
                }

                // 右上角：悬停时的删除 ×
                if (hov)
                {
                    Rectangle xr = XRect(box);
                    using (SolidBrush b = new SolidBrush(hoverX ? Theme.Danger : Color.FromArgb(150, 0, 0, 0))) g.FillEllipse(b, xr);
                    float q = xr.Width * 0.32f;
                    using (Pen pen = new Pen(Color.White, Math.Max(1.5f, m.S * 1.4f)))
                    {
                        g.DrawLine(pen, xr.Left + q, xr.Top + q, xr.Right - q, xr.Bottom - q);
                        g.DrawLine(pen, xr.Right - q, xr.Top + q, xr.Left + q, xr.Bottom - q);
                    }
                }
            }

            if (contentH > Height)
            {
                int track = Height - 2 * m.Px(4);
                int th = Math.Max(m.Px(24), track * Height / contentH);
                int ty = m.Px(4) + (int)((long)(track - th) * scroll / Math.Max(1, contentH - Height));
                Rectangle sr = new Rectangle(Width - m.Px(5), ty, m.Px(3), th);
                using (GraphicsPath p = G.Round(sr, sr.Width / 2f))
                using (SolidBrush b = new SolidBrush(Theme.Scroll))
                    g.FillPath(b, p);
            }
        }

        void UpdateHover(Point p)
        {
            bool onX;
            int i = IndexAt(p, out onX);
            ShotItem h = i >= 0 ? items[i] : null;
            if (h != hover || onX != hoverX) { hover = h; hoverX = onX; Invalidate(); }
            Cursor = h != null ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            bool onX;
            int i = IndexAt(e.Location, out onX);
            if (e.Button == MouseButtons.Left)
            {
                leftDown = true; downPt = e.Location; downX = onX; suppressUp = false;
                downItem = i >= 0 ? items[i] : null;
                if (e.Clicks >= 2 && downItem != null && !onX)
                {
                    // 双击 = 复制；把第一下点出来的选中撤回去
                    suppressUp = true;
                    if (lastToggled == downItem)
                    {
                        if (Selected.Contains(downItem)) Selected.Remove(downItem); else Selected.Add(downItem);
                        lastToggled = null; RaiseSel(); Invalidate();
                    }
                    if (CopyRequested != null) CopyRequested(downItem);
                }
            }
            else if (e.Button == MouseButtons.Right && i >= 0 && ItemMenuRequested != null)
                ItemMenuRequested(items[i], PointToScreen(e.Location));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (leftDown && downItem != null && !downX && (e.Button & MouseButtons.Left) != 0)
            {
                Size ds = SystemInformation.DragSize;
                if (Math.Abs(e.X - downPt.X) > ds.Width || Math.Abs(e.Y - downPt.Y) > ds.Height)
                {
                    ShotItem it = downItem;
                    leftDown = false; downItem = null;
                    bool dragSel = Selected.Contains(it);
                    List<ShotItem> list = dragSel ? new List<ShotItem>(Selected) : new List<ShotItem> { it };
                    if (DragRequested != null)
                    {
                        DragDropEffects eff = DragRequested(list);
                        if (eff != DragDropEffects.None && dragSel) { Selected.Clear(); RaiseSel(); }
                    }
                    UpdateHover(PointToClient(Cursor.Position));
                    Invalidate();
                    return;
                }
            }
            UpdateHover(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left || !leftDown) { leftDown = false; return; }
            leftDown = false;
            if (suppressUp) { suppressUp = false; return; }
            bool onX;
            int i = IndexAt(e.Location, out onX);
            ShotItem up = i >= 0 ? items[i] : null;
            if (up == null && downItem == null)
            {
                if (Selected.Count > 0) { Selected.Clear(); RaiseSel(); Invalidate(); }
                return;
            }
            if (up != downItem) return;
            if (downX && onX) { if (DeleteRequested != null) DeleteRequested(up); return; }
            if (Selected.Contains(up)) Selected.Remove(up); else Selected.Add(up);
            lastToggled = up;
            RaiseSel();
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != null) { hover = null; hoverX = false; Invalidate(); }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            scroll -= e.Delta * m.Px(90) / 120;
            ClampScroll();
            UpdateHover(e.Location);
            Invalidate();
        }
    }

    // ------------------------------------------------------------------ 主窗口
    class ShelfForm : Form
    {
        static readonly string[] SkipFormats = {
            "Rich Text Format", "Csv", "Object Descriptor", "Art::GVML ClipFormat",
            "ExcludeClipboardContentFromMonitorProcessing"
        };
        static readonly HashSet<string> NotGames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "explorer.exe", "searchhost.exe", "startmenuexperiencehost.exe", "shellexperiencehost.exe",
            "screenclippinghost.exe", "snippingtool.exe", "textinputhost.exe", "lockapp.exe"
        };

        readonly Metrics m;
        readonly Settings cfg;
        readonly string shotsDir;
        readonly bool snapshotMode;
        readonly HeaderBar header;
        readonly ShelfView view;
        readonly List<ShotItem> items = new List<ShotItem>();
        readonly HashSet<string> hashes = new HashSet<string>();
        readonly object hashLock = new object();
        readonly ContextMenuStrip mainMenu = new ContextMenuStrip();
        readonly System.Windows.Forms.Timer fsTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer clipTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer toastTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer confirmTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer flashTimer = new System.Windows.Forms.Timer();
        readonly HashSet<string> gameProcs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<uint, string> procCache = new Dictionary<uint, string>();
        readonly uint ourPid = (uint)Process.GetCurrentProcess().Id;
        NotifyIcon tray;
        bool userHidden, autoHidden, collapsed, paused, excludeCapture;
        int maxItems;
        int ignoreUntil;
        uint lastSeq;
        IntPtr lastFg = IntPtr.Zero;
        string lastForeignProc = "";
        DragCursorSet dragCursors;
        readonly System.Windows.Forms.Timer autoTimer = new System.Windows.Forms.Timer();
        Point home;                       // 收起小块的位置
        bool autoCollapse, dragging, menuOpen;
        int outsideSince, graceUntil;
        readonly System.Windows.Forms.Timer hoverTimer = new System.Windows.Forms.Timer();
        bool hoverOpen, stayAfterShot;
        int stayOpenSec;
        DateTime stayOpenUntil = DateTime.MinValue;
        W32.WinEventDelegate fgHookProc;
        IntPtr fgHook = IntPtr.Zero;

        public ShelfForm(Settings cfg, string shotsDir, Metrics m, bool snapshotMode)
        {
            this.cfg = cfg; this.shotsDir = shotsDir; this.m = m; this.snapshotMode = snapshotMode;
            Text = "截图盒";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Line;
            Padding = new Padding(1);
            Font = new Font("Microsoft YaHei UI", 9f);
            mainMenu.Font = Font;
            mainMenu.ShowImageMargin = false;
            mainMenu.ShowCheckMargin = true;

            autoCollapse = cfg.GetBool("AutoCollapse", true);
            collapsed = autoCollapse || cfg.GetBool("Collapsed", false);
            hoverOpen = cfg.Get("OpenMode", "click").ToLowerInvariant() == "hover";
            stayAfterShot = cfg.GetBool("StayOpenAfterShot", true);
            stayOpenSec = Math.Max(5, Math.Min(600, cfg.GetInt("StayOpenSeconds", 60)));
            paused = cfg.GetBool("Paused", false);
            excludeCapture = cfg.GetBool("HideFromScreenshots", true);
            maxItems = Math.Max(5, Math.Min(500, cfg.GetInt("MaxItems", 40)));
            foreach (string p in cfg.Get("GameProcs", "dnplayer.exe,MuMuPlayer.exe,MuMuNxDevice.exe").Split(','))
                if (p.Trim().Length > 0) gameProcs.Add(p.Trim().ToLowerInvariant());

            view = new ShelfView(items, m);
            view.Dock = DockStyle.Fill;
            header = new HeaderBar(m);
            header.Dock = DockStyle.Top;
            header.Height = m.HeaderH;
            header.Collapsed = collapsed;
            header.Paused = paused;
            header.HoverMode = hoverOpen;
            Controls.Add(view);
            Controls.Add(header);

            view.SelectionChanged += delegate { header.SelCount = view.Selected.Count; header.Invalidate(); };
            view.CopyRequested += CopyItem;
            view.DeleteRequested += delegate(ShotItem it) { RemoveItem(it); AfterItemsChanged(); };
            view.ItemMenuRequested += ShowItemMenu;
            view.DragRequested = DoDrag;
            view.GiveFeedback += delegate(object s, GiveFeedbackEventArgs e)
            {
                if (dragCursors == null) return;
                e.UseDefaultCursors = false;
                Cursor.Current = e.Effect != DragDropEffects.None ? dragCursors.Ok : dragCursors.No;
            };
            header.CollapseClicked += delegate { if (collapsed) Expand(0); else Collapse(); };
            header.ChipClicked += delegate { Expand(0); };
            header.ChipHoverStart += delegate { if (hoverOpen) { hoverTimer.Stop(); hoverTimer.Start(); } };
            header.ChipHoverEnd += delegate { hoverTimer.Stop(); };
            header.ClearClicked += OnClearClicked;
            header.MenuClicked += delegate(Point p) { W32.SetForegroundWindow(Handle); mainMenu.Show(p); };
            header.MoveEnded += OnMoveEnded;
            mainMenu.Opening += delegate { BuildMainMenu(); };
            mainMenu.Opened += delegate { menuOpen = true; };
            mainMenu.Closed += delegate { menuOpen = false; outsideSince = 0; };

            fsTimer.Interval = 700; fsTimer.Tick += delegate { FsTick(); };
            clipTimer.Interval = 150; clipTimer.Tick += delegate { clipTimer.Stop(); OnClipboardSettled(); };
            toastTimer.Tick += delegate { toastTimer.Stop(); header.Toast = null; header.Invalidate(); };
            confirmTimer.Interval = 3000; confirmTimer.Tick += delegate { confirmTimer.Stop(); header.ConfirmClear = false; header.Invalidate(); };
            flashTimer.Interval = 60; flashTimer.Tick += delegate
            {
                bool any = false; DateTime now = DateTime.Now;
                foreach (ShotItem it in items) if (it.FlashUntil > now) { any = true; break; }
                if (header.FlashUntil > now) any = true;
                view.Invalidate();
                header.Invalidate();
                if (!any) flashTimer.Stop();
            };
            autoTimer.Interval = 200; autoTimer.Tick += delegate { AutoCollapseTick(); };
            hoverTimer.Interval = 150; hoverTimer.Tick += delegate
            {
                hoverTimer.Stop();
                if (collapsed && hoverOpen && Visible && Control.MouseButtons == MouseButtons.None && Bounds.Contains(Cursor.Position)) Expand(0);
            };

            // 位置：上次的位置；没有就放在屏幕右侧
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            home = new Point(wa.Right - m.ChipW - m.Px(10), wa.Top + wa.Height / 5);
            if (cfg.Has("X") && cfg.Has("Y"))
            {
                Point saved = new Point(cfg.GetInt("X", home.X), cfg.GetInt("Y", home.Y));
                foreach (Screen s in Screen.AllScreens)
                    if (s.WorkingArea.Contains(new Point(saved.X + m.ChipW / 2, saved.Y + m.Px(10)))) { home = saved; break; }
            }
            Location = home;
            UpdateSize();

            if (!snapshotMode)
            {
                tray = new NotifyIcon();
                tray.Text = "截图盒";
                tray.Icon = new Icon(new MemoryStream(AppIcon.BuildIco()), SystemInformation.SmallIconSize);
                tray.ContextMenuStrip = mainMenu;
                tray.MouseClick += delegate(object s, MouseEventArgs e)
                {
                    if (e.Button != MouseButtons.Left) return;
                    userHidden = !userHidden;
                    ApplyVisibility();
                };
                tray.Visible = true;
                SystemEvents.DisplaySettingsChanged += delegate { try { BeginInvoke(new Action(ClampToScreen)); } catch { } };
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= W32.WS_EX_TOOLWINDOW | W32.WS_EX_NOACTIVATE | W32.WS_EX_TOPMOST;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void WndProc(ref Message msg)
        {
            if (msg.Msg == W32.WM_MOUSEACTIVATE) { msg.Result = new IntPtr(W32.MA_NOACTIVATE); return; }
            if (msg.Msg == W32.WM_CLIPBOARDUPDATE) { clipTimer.Stop(); clipTimer.Start(); }
            base.WndProc(ref msg);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int pref = 2; W32.DwmSetWindowAttribute(Handle, 33, ref pref, 4); } catch { }       // Win11 圆角
            try { int col = Theme.Line.R | (Theme.Line.G << 8) | (Theme.Line.B << 16); W32.DwmSetWindowAttribute(Handle, 34, ref col, 4); } catch { }
            if (snapshotMode) return;
            if (!W32.AddClipboardFormatListener(Handle)) Log.W("剪贴板监听注册失败 err=" + Marshal.GetLastWin32Error());
            lastSeq = W32.GetClipboardSequenceNumber();   // 启动前剪贴板里的东西不收
            ApplyCaptureAffinity();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (snapshotMode) return;
            LoadExistingAsync();
            fsTimer.Start();
            autoTimer.Start();
            fgHookProc = delegate { FsTick(); };
            fgHook = W32.SetWinEventHook(3, 3, IntPtr.Zero, fgHookProc, 0, 0, 0x2);   // EVENT_SYSTEM_FOREGROUND, 跳过自己
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ReassertTop();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!snapshotMode)
            {
                try { W32.RemoveClipboardFormatListener(Handle); } catch { }
                if (fgHook != IntPtr.Zero) { W32.UnhookWinEvent(fgHook); fgHook = IntPtr.Zero; }
                cfg.Set("X", home.X); cfg.Set("Y", home.Y); cfg.Save();
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
            }
            base.OnFormClosing(e);
        }

        // ---------------- 显示 / 置顶 / 全屏
        void ApplyCaptureAffinity()
        {
            if (!W32.SetWindowDisplayAffinity(Handle, excludeCapture ? W32.WDA_EXCLUDEFROMCAPTURE : W32.WDA_NONE))
                Log.W("SetWindowDisplayAffinity 失败 err=" + Marshal.GetLastWin32Error());
        }

        void ReassertTop()
        {
            if (!IsHandleCreated) return;
            W32.SetWindowPos(Handle, W32.HWND_TOPMOST, 0, 0, 0, 0, W32.SWP_NOMOVE | W32.SWP_NOSIZE | W32.SWP_NOACTIVATE | W32.SWP_NOOWNERZORDER);
        }

        void ApplyVisibility()
        {
            bool want = !userHidden && !autoHidden;
            if (want && !Visible) { Visible = true; ReassertTop(); }
            else if (!want && Visible) Visible = false;
        }

        public void ShowFromSignal()
        {
            userHidden = false;
            ApplyVisibility();
            Expand(3000);
            graceUntil = Environment.TickCount + 3000;
            ReassertTop();
            Toast("截图盒在这儿", false, 1500);
        }

        string ProcOf(uint pid)
        {
            string n;
            if (procCache.TryGetValue(pid, out n)) return n;
            if (procCache.Count > 200) procCache.Clear();
            n = FsDetect.ProcName(pid);
            procCache[pid] = n;
            return n;
        }

        void FsTick()
        {
            IntPtr fg = W32.GetForegroundWindow();
            uint pid = 0;
            if (fg != IntPtr.Zero) W32.GetWindowThreadProcessId(fg, out pid);
            bool ours = pid == ourPid;
            bool game = autoHidden;
            string why = "";
            if (!ours && fg != IntPtr.Zero)
            {
                string pn = ProcOf(pid);
                if (pn.Length > 0 && !NotGames.Contains(pn) && !FsDetect.IsShell(fg)) lastForeignProc = pn;
                if (pn.Length > 0 && gameProcs.Contains(pn)) { game = true; why = "名单里的程序 " + pn; }
                else if (FsDetect.IsFullscreen(fg)) { game = true; why = "全屏 " + pn; }
                else if (FsDetect.D3DFullscreen()) { game = true; why = "独占全屏 " + pn; }
                else game = false;
            }
            else if (fg == IntPtr.Zero) game = autoHidden;
            if (game != autoHidden)
            {
                autoHidden = game;
                Log.W(game ? "自动隐藏：" + why : "恢复显示");
                ApplyVisibility();
            }
            if (fg != lastFg)
            {
                lastFg = fg;
                if (!ours && Visible) ReassertTop();
            }
        }

        // ---------------- 尺寸 / 展开 / 收起
        static Rectangle Clamp(Rectangle r, Rectangle wa)
        {
            r.X = Math.Max(wa.Left, Math.Min(r.X, wa.Right - r.Width));
            r.Y = Math.Max(wa.Top, Math.Min(r.Y, wa.Bottom - r.Height));
            return r;
        }

        void UpdateSize()
        {
            header.Collapsed = collapsed;
            header.Count = items.Count;
            header.Invalidate();
            view.Visible = !collapsed;
            Rectangle wa = Screen.FromPoint(new Point(home.X + m.ChipW / 2, home.Y + m.Px(10))).WorkingArea;
            Rectangle chip = Clamp(new Rectangle(home, new Size(m.ChipW, m.HeaderH + 2)), wa);
            int maxH = (int)(wa.Height * 0.72) - m.HeaderH;
            Size panel = new Size(m.FormW, m.HeaderH + 2 + Math.Min(view.ContentHeight, maxH));
            if (snapshotMode) { Size = collapsed ? chip.Size : panel; return; }
            home = chip.Location;
            if (collapsed) { Bounds = chip; return; }
            // 小块在屏幕右半边就往左展开，在左半边就往右展开
            bool rightSide = home.X + m.ChipW / 2 > wa.Left + wa.Width / 2;
            int x = rightSide ? home.X + m.ChipW - m.FormW : home.X;
            Bounds = Clamp(new Rectangle(new Point(x, home.Y), panel), wa);
        }

        void ClampToScreen() { UpdateSize(); }

        void OnMoveEnded()
        {
            if (collapsed) home = Location;
            else
            {
                Rectangle wa = Screen.FromPoint(new Point(Left + Width / 2, Top + m.Px(10))).WorkingArea;
                bool rightSide = Left + Width / 2 > wa.Left + wa.Width / 2;
                home = new Point(rightSide ? Right - m.ChipW : Left, Top);
            }
            UpdateSize();
            cfg.Set("X", home.X); cfg.Set("Y", home.Y); cfg.Save();
        }

        void Expand(int graceMs)
        {
            if (!collapsed) return;
            collapsed = false;
            header.IgnoreClicksUntil = Environment.TickCount + 500;
            outsideSince = 0;
            graceUntil = Environment.TickCount + graceMs;
            view.ScrollToTop();
            UpdateSize();
            ReassertTop();
            if (!autoCollapse) { cfg.Set("Collapsed", false); cfg.Save(); }
        }

        void Collapse()
        {
            if (collapsed) return;
            collapsed = true;
            if (view.Selected.Count > 0) { view.Selected.Clear(); header.SelCount = 0; }
            header.ConfirmClear = false;
            header.Toast = null;
            stayOpenUntil = DateTime.MinValue;
            header.StayFraction = 0;
            UpdateSize();
            if (!autoCollapse) { cfg.Set("Collapsed", true); cfg.Save(); }
        }

        // 展开后鼠标离开 0.8 秒就收起；拖图、开菜单、按着鼠标时不收
        void AutoCollapseTick()
        {
            UpdateStayBar();
            if (collapsed || !autoCollapse || dragging || menuOpen || !Visible || (Control.MouseButtons & MouseButtons.Left) != 0)
            { outsideSince = 0; return; }
            if (DateTime.Now < stayOpenUntil) { outsideSince = 0; return; }   // 最近一次截图后的保持展开时间内不收
            if (unchecked(Environment.TickCount - graceUntil) < 0) return;
            Rectangle b = Bounds;
            b.Inflate(m.Px(14), m.Px(14));
            if (b.Contains(Cursor.Position)) { outsideSince = 0; return; }
            if (outsideSince == 0) { outsideSince = Environment.TickCount; return; }
            if (unchecked(Environment.TickCount - outsideSince) > 800) Collapse();
        }

        void UpdateStayBar()
        {
            float f = 0;
            if (!collapsed && autoCollapse)
            {
                double left = (stayOpenUntil - DateTime.Now).TotalSeconds;
                if (left > 0) f = (float)(left / stayOpenSec);
            }
            if (Math.Abs(f - header.StayFraction) > 0.001f) { header.StayFraction = f; header.Invalidate(); }
        }

        // 有新截图：展开并保持 stayOpenSec 秒（每来一张重新计时）
        void OnShotActivity()
        {
            if (stayAfterShot)
            {
                stayOpenUntil = DateTime.Now.AddSeconds(stayOpenSec);
                if (collapsed) Expand(0);
                UpdateStayBar();
            }
            else if (collapsed) { header.FlashUntil = DateTime.Now.AddMilliseconds(1400); flashTimer.Start(); }
        }

        void SetOpenMode(bool hover)
        {
            hoverOpen = hover;
            header.HoverMode = hover;
            cfg.Set("OpenMode", hover ? "hover" : "click");
            cfg.Save();
        }

        void AfterItemsChanged()
        {
            view.Relayout();
            UpdateSize();
        }

        void Toast(string text, bool warn, int ms)
        {
            header.Toast = text; header.ToastWarn = warn; header.Invalidate();
            toastTimer.Stop(); toastTimer.Interval = ms; toastTimer.Start();
        }

        // ---------------- 剪贴板
        void OnClipboardSettled()
        {
            uint seq = W32.GetClipboardSequenceNumber();
            if (seq == lastSeq) return;
            lastSeq = seq;
            if (unchecked(Environment.TickCount - ignoreUntil) < 0) return;   // 自己刚放进去的
            if (paused) return;
            Bitmap bmp = null;
            string from = "";
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try { bmp = ReadClipboardImage(out from); break; }
                catch (ExternalException) { Thread.Sleep(80); }
                catch (Exception ex) { Log.W("读剪贴板出错: " + ex.Message); break; }
            }
            if (bmp == null) return;
            DateTime now = DateTime.Now;
            ThreadPool.QueueUserWorkItem(delegate { SaveInBackground(bmp, now, from); });
        }

        Bitmap ReadClipboardImage(out string from)
        {
            from = "";
            IDataObject d = Clipboard.GetDataObject();
            if (d == null) return null;
            string[] fmts = d.GetFormats(false);
            bool hasPng = false;
            foreach (string f in fmts)
            {
                if (Array.IndexOf(SkipFormats, f) >= 0) return null;   // Office 复制表格/文字时顺带的图，不收
                if (f == "PNG") hasPng = true;
            }
            IntPtr owner = W32.GetClipboardOwner();
            if (owner != IntPtr.Zero) { uint pid; W32.GetWindowThreadProcessId(owner, out pid); from = ProcOf(pid); }
            if (hasPng)
            {
                try
                {
                    MemoryStream ms = d.GetData("PNG") as MemoryStream;
                    if (ms != null) using (Image img = Image.FromStream(ms)) return new Bitmap(img);
                }
                catch (Exception ex) { Log.W("PNG 格式读取失败，改用位图: " + ex.Message); }
            }
            if (Clipboard.ContainsImage())
            {
                Image img = Clipboard.GetImage();
                if (img != null) using (img) return new Bitmap(img);
            }
            return null;
        }

        void SaveInBackground(Bitmap bmp, DateTime now, string from)
        {
            try
            {
                string hash = Shots.HashOf(bmp);
                bool dup;
                lock (hashLock) dup = hashes.Contains(hash);
                if (dup) { bmp.Dispose(); BeginInvoke(new Action(delegate { BumpToTop(hash); })); return; }
                string path = Path.Combine(shotsDir, "shot_" + now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture) + "_" + hash + ".png");
                bmp.Save(path, ImageFormat.Png);
                ShotItem it = Shots.Make(path, hash, now, bmp, m);
                bmp.Dispose();
                BeginInvoke(new Action(delegate { AddNew(it, from); }));
            }
            catch (Exception ex) { Log.W("保存截图失败: " + ex); }
        }

        void AddNew(ShotItem it, string from)
        {
            bool dup;
            lock (hashLock) { dup = hashes.Contains(it.Hash); if (!dup) hashes.Add(it.Hash); }
            if (dup) { TryDelete(it.Path); BumpToTop(it.Hash); return; }
            it.FlashUntil = DateTime.Now.AddMilliseconds(900);
            items.Insert(0, it);
            while (items.Count > maxItems) RemoveItem(items[items.Count - 1]);
            AfterItemsChanged();
            view.ScrollToTop();
            flashTimer.Start();
            OnShotActivity();
            Log.W("收到截图 " + it.W + "x" + it.H + (from.Length > 0 ? " 来自 " + from : "") + " → " + Path.GetFileName(it.Path));
        }

        void BumpToTop(string hash)
        {
            ShotItem it = items.Find(delegate(ShotItem x) { return x.Hash == hash; });
            if (it == null) return;
            items.Remove(it);
            items.Insert(0, it);
            it.FlashUntil = DateTime.Now.AddMilliseconds(900);
            AfterItemsChanged();
            view.ScrollToTop();
            flashTimer.Start();
            bool wasCollapsed = collapsed;
            OnShotActivity();
            if (!wasCollapsed) Toast("盒子里已有这张", false, 1600);
        }

        void LoadExistingAsync()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<ShotItem> loaded = LoadExisting();
                try { BeginInvoke(new Action(delegate { AddLoaded(loaded); })); } catch { }
            });
        }

        public List<ShotItem> LoadExisting()
        {
            List<ShotItem> loaded = new List<ShotItem>();
            try
            {
                List<string> files = new List<string>(Directory.GetFiles(shotsDir, "shot_*.png"));
                files.Sort(StringComparer.Ordinal);
                files.Reverse();   // 文件名里带时间，新的在前
                for (int i = 0; i < files.Count; i++)
                {
                    if (i >= maxItems) { TryDelete(files[i]); continue; }
                    try
                    {
                        DateTime t; string hash;
                        using (Bitmap b = Shots.LoadFile(files[i]))
                        {
                            if (!Shots.ParseName(files[i], out t, out hash)) { t = File.GetLastWriteTime(files[i]); hash = Shots.HashOf(b); }
                            loaded.Add(Shots.Make(files[i], hash, t, b, m));
                        }
                    }
                    catch (Exception ex) { Log.W("载入失败 " + files[i] + ": " + ex.Message); }
                }
            }
            catch (Exception ex) { Log.W("读截图文件夹失败: " + ex.Message); }
            return loaded;
        }

        public void AddLoaded(List<ShotItem> loaded)
        {
            foreach (ShotItem it in loaded)
            {
                lock (hashLock) { if (hashes.Contains(it.Hash)) continue; hashes.Add(it.Hash); }
                items.Add(it);
            }
            AfterItemsChanged();
            if (!snapshotMode) Log.W("载入已有截图 " + loaded.Count + " 张");
        }

        void RemoveItem(ShotItem it)
        {
            items.Remove(it);
            view.Selected.Remove(it);
            lock (hashLock) hashes.Remove(it.Hash);
            if (it.Thumb != null) { it.Thumb.Dispose(); it.Thumb = null; }
            TryDelete(it.Path);
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception ex) { Log.W("删除失败 " + path + ": " + ex.Message); }
        }

        void OnClearClicked()
        {
            if (items.Count == 0) { Toast("盒子是空的", false, 1200); return; }
            if (!header.ConfirmClear)
            {
                header.ConfirmClear = true; header.Invalidate();
                confirmTimer.Stop(); confirmTimer.Start();
                return;
            }
            confirmTimer.Stop();
            header.ConfirmClear = false;
            int n = items.Count;
            foreach (ShotItem it in new List<ShotItem>(items)) RemoveItem(it);
            AfterItemsChanged();
            Toast("已清空 " + n + " 张", false, 1500);
            Log.W("清空 " + n + " 张");
        }

        // ---------------- 复制 / 拖出
        void CopyItem(ShotItem it)
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(it.Path);
                using (MemoryStream src = new MemoryStream(bytes))
                using (Image img = Image.FromStream(src))
                using (Bitmap bmp = new Bitmap(img))
                {
                    DataObject d = new DataObject();
                    d.SetData(DataFormats.Bitmap, true, bmp);
                    d.SetData("PNG", false, new MemoryStream(bytes));
                    ignoreUntil = Environment.TickCount + 1500;
                    Clipboard.SetDataObject(d, true, 5, 100);
                }
                Toast("已复制，可以 Ctrl+V", false, 1600);
            }
            catch (Exception ex) { Toast("复制失败", true, 1600); Log.W("复制失败: " + ex.Message); }
        }

        void CopyFiles(List<ShotItem> list)
        {
            try
            {
                List<string> paths = new List<string>();
                foreach (ShotItem x in list) if (File.Exists(x.Path)) paths.Add(x.Path);
                if (paths.Count == 0) { Toast("文件找不到了", true, 1600); return; }
                object data;
                try { data = ShellData.For(paths); }
                catch
                {
                    StringCollection sc = new StringCollection();
                    foreach (string x in paths) sc.Add(x);
                    DataObject d2 = new DataObject(); d2.SetFileDropList(sc); data = d2;
                }
                ignoreUntil = Environment.TickCount + 1500;
                Clipboard.SetDataObject(data, true, 5, 100);
                Toast("已复制 " + paths.Count + " 张，Ctrl+V", false, 1800);
                Log.W("复制 " + paths.Count + " 张（文件）");
            }
            catch (Exception ex) { Toast("复制失败", true, 1600); Log.W("复制多张失败: " + ex.Message); }
        }

        DragDropEffects DoDrag(List<ShotItem> list)
        {
            StringCollection sc = new StringCollection();
            List<ShotItem> ok = new List<ShotItem>();
            foreach (ShotItem it in list)
                if (File.Exists(it.Path)) { sc.Add(it.Path); ok.Add(it); }
            if (sc.Count == 0) { Toast("文件找不到了", true, 1600); return DragDropEffects.None; }
            object data = null;
            string kind = "shell";
            try { List<string> paths = new List<string>(); foreach (string x in sc) paths.Add(x); data = ShellData.For(paths); }
            catch (Exception ex) { Log.W("资源管理器拖拽数据创建失败，改用普通文件列表: " + ex.Message); }
            if (data == null)
            {
                DataObject d2 = new DataObject();
                d2.SetFileDropList(sc);
                data = d2;
                kind = "winforms";
            }
            DragCursorSet cur = null;
            try { cur = DragCursorSet.Build(ok, m); } catch (Exception ex) { Log.W("拖拽光标生成失败: " + ex.Message); }
            dragCursors = cur;
            dragging = true;
            DragDropEffects eff = DragDropEffects.None;
            try { eff = view.DoDragDrop(data, DragDropEffects.Copy | DragDropEffects.Link); }
            catch (Exception ex) { Log.W("拖拽出错: " + ex.Message); }
            finally
            {
                dragging = false;
                outsideSince = 0;
                dragCursors = null;
                Cursor.Current = Cursors.Default;
                if (cur != null) cur.Dispose();
            }
            string target = "";
            try
            {
                W32.POINT pt; pt.X = Cursor.Position.X; pt.Y = Cursor.Position.Y;
                IntPtr th = W32.WindowFromPoint(pt);
                uint tpid; W32.GetWindowThreadProcessId(th, out tpid);
                target = FsDetect.ProcName(tpid) + " / " + FsDetect.ClassOf(th);
            }
            catch { }
            Log.W("拖出 " + sc.Count + " 张（" + kind + "），松手在 " + target + "，结果 " + eff);
            if (eff != DragDropEffects.None) Toast("已拖出 " + sc.Count + " 张", false, 1500);
            return eff;
        }

        // ---------------- 菜单
        void BuildMainMenu()
        {
            List<ToolStripItem> old = new List<ToolStripItem>();
            foreach (ToolStripItem x in mainMenu.Items) old.Add(x);
            mainMenu.Items.Clear();
            foreach (ToolStripItem x in old) x.Dispose();

            ToolStripMenuItem mClick = new ToolStripMenuItem("点一下才展开");
            mClick.Checked = !hoverOpen;
            mClick.Click += delegate { SetOpenMode(false); };
            mainMenu.Items.Add(mClick);
            ToolStripMenuItem mHover = new ToolStripMenuItem("鼠标划过就展开");
            mHover.Checked = hoverOpen;
            mHover.Click += delegate { SetOpenMode(true); };
            mainMenu.Items.Add(mHover);

            string stayText = stayOpenSec % 60 == 0 ? (stayOpenSec / 60) + " 分钟" : stayOpenSec + " 秒";
            ToolStripMenuItem stay = new ToolStripMenuItem("截图后保持展开 " + stayText);
            stay.Checked = stayAfterShot;
            stay.Click += delegate
            {
                stayAfterShot = !stayAfterShot;
                if (!stayAfterShot) stayOpenUntil = DateTime.MinValue;
                cfg.Set("StayOpenAfterShot", stayAfterShot); cfg.Set("StayOpenSeconds", stayOpenSec); cfg.Save();
            };
            mainMenu.Items.Add(stay);

            ToolStripMenuItem ac = new ToolStripMenuItem("鼠标移开后自动收起");
            ac.Checked = autoCollapse;
            ac.Click += delegate { autoCollapse = !autoCollapse; cfg.Set("AutoCollapse", autoCollapse); cfg.Set("Collapsed", collapsed); cfg.Save(); };
            mainMenu.Items.Add(ac);

            mainMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem pause = new ToolStripMenuItem("暂停收集截图");
            pause.Checked = paused;
            pause.Click += delegate
            {
                paused = !paused; header.Paused = paused; header.Invalidate();
                cfg.Set("Paused", paused); cfg.Save();
                Toast(paused ? "已暂停收集" : "继续收集截图", false, 1500);
            };
            mainMenu.Items.Add(pause);

            string gp = lastForeignProc;
            if (gp.Length > 0)
            {
                ToolStripMenuItem gi = new ToolStripMenuItem("「" + gp + "」在前台时隐藏");
                gi.Checked = gameProcs.Contains(gp);
                gi.Click += delegate
                {
                    if (gameProcs.Contains(gp)) gameProcs.Remove(gp); else gameProcs.Add(gp);
                    cfg.Set("GameProcs", string.Join(",", new List<string>(gameProcs).ToArray())); cfg.Save();
                };
                mainMenu.Items.Add(gi);
            }

            ToolStripMenuItem cap = new ToolStripMenuItem("截图时不把盒子拍进去");
            cap.Checked = excludeCapture;
            cap.Click += delegate { excludeCapture = !excludeCapture; ApplyCaptureAffinity(); cfg.Set("HideFromScreenshots", excludeCapture); cfg.Save(); };
            mainMenu.Items.Add(cap);

            mainMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem folder = new ToolStripMenuItem("打开截图文件夹");
            folder.Click += delegate { try { Process.Start("explorer.exe", "\"" + shotsDir + "\""); } catch { } };
            mainMenu.Items.Add(folder);

            ToolStripMenuItem auto = new ToolStripMenuItem("开机自动启动");
            bool on = false;
            try { on = Autostart.IsOn(); } catch { }
            auto.Checked = on;
            auto.Click += delegate { try { Autostart.Set(!on); } catch (Exception ex) { Log.W("开机启动设置失败: " + ex.Message); } };
            mainMenu.Items.Add(auto);

            mainMenu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem hide = new ToolStripMenuItem(userHidden ? "显示盒子" : "隐藏盒子（点右下角托盘图标找回）");
            hide.Click += delegate { userHidden = !userHidden; ApplyVisibility(); };
            mainMenu.Items.Add(hide);

            ToolStripMenuItem quit = new ToolStripMenuItem("退出截图盒");
            quit.Click += delegate { Close(); };
            mainMenu.Items.Add(quit);
        }

        void ShowItemMenu(ShotItem it, Point screen)
        {
            ContextMenuStrip cm = new ContextMenuStrip();
            cm.Font = Font;
            cm.ShowImageMargin = false;
            List<ShotItem> sel = view.Selected.Contains(it) && view.Selected.Count > 1 ? new List<ShotItem>(view.Selected) : null;
            ToolStripMenuItem copy = new ToolStripMenuItem(sel != null ? "复制选中的 " + sel.Count + " 张（之后 Ctrl+V 粘贴）" : "复制（之后 Ctrl+V 粘贴）");
            copy.Click += delegate { if (sel != null) CopyFiles(sel); else CopyItem(it); };
            ToolStripMenuItem show = new ToolStripMenuItem("在文件夹中显示");
            show.Click += delegate { try { Process.Start("explorer.exe", "/select,\"" + it.Path + "\""); } catch { } };
            ToolStripMenuItem del = new ToolStripMenuItem("删除这张");
            del.Click += delegate { RemoveItem(it); AfterItemsChanged(); };
            cm.Items.Add(copy); cm.Items.Add(show); cm.Items.Add(new ToolStripSeparator()); cm.Items.Add(del);
            cm.Opened += delegate { menuOpen = true; };
            cm.Closed += delegate { menuOpen = false; outsideSince = 0; BeginInvoke(new Action(cm.Dispose)); };
            W32.SetForegroundWindow(Handle);
            cm.Show(screen);
        }

        // ---------------- 自检截图用
        public void DebugStay(float f) { header.StayFraction = f; header.Invalidate(); }

        public void DebugState(int[] select, int hover, bool hoverX, bool collapse, string toast, bool flash)
        {
            if (flash) header.FlashUntil = DateTime.Now.AddMilliseconds(900);
            if (select != null) foreach (int i in select) if (i >= 0 && i < items.Count) view.Selected.Add(items[i]);
            header.SelCount = view.Selected.Count;
            collapsed = collapse;
            if (!string.IsNullOrEmpty(toast)) { header.Toast = toast; }
            UpdateSize();
            view.DebugHover(hover, hoverX);
        }
    }

    // ------------------------------------------------------------------ 入口
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            try { W32.SetProcessDPIAware(); } catch { }
            string baseDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            Log.PathName = Path.Combine(baseDir, "log.txt");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            string a0 = args.Length > 0 ? args[0].ToLowerInvariant() : "";

            if (a0 == "--make-icon") { File.WriteAllBytes(args[1], AppIcon.BuildIco()); return 0; }
            if (a0 == "--probe") { Probe(args.Length > 1 ? args[1] : Path.Combine(baseDir, "probe.txt")); return 0; }
            if (a0 == "--install") { Install(baseDir); return 0; }
            if (a0 == "--uninstall") { try { Autostart.Set(false); } catch { } return 0; }
            if (a0 == "--snapshot") return Snapshot(args);
            if (a0 == "--test-shelldata")
            {
                // --test-shelldata out.txt file1 [file2 ...]：列出拖拽数据里有哪些格式
                StringBuilder sb = new StringBuilder();
                List<string> paths = new List<string>();
                for (int i = 2; i < args.Length; i++) paths.Add(args[i]);
                System.Runtime.InteropServices.ComTypes.IDataObject d = ShellData.For(paths);
                System.Runtime.InteropServices.ComTypes.IEnumFORMATETC en = d.EnumFormatEtc(System.Runtime.InteropServices.ComTypes.DATADIR.DATADIR_GET);
                System.Runtime.InteropServices.ComTypes.FORMATETC[] f = new System.Runtime.InteropServices.ComTypes.FORMATETC[1];
                int[] got = new int[1];
                while (en.Next(1, f, got) == 0 && got[0] == 1)
                    sb.AppendLine(DataFormats.GetFormat(f[0].cfFormat & 0xFFFF).Name + "  tymed=" + f[0].tymed);
                DataObject wrap = new DataObject(d);
                string[] files = wrap.GetData(DataFormats.FileDrop) as string[];
                sb.AppendLine("FileDrop -> " + (files == null ? "null" : string.Join(" | ", files)));
                File.WriteAllText(args[1], sb.ToString(), new UTF8Encoding(true));
                return 0;
            }

            bool created;
            using (Mutex mutex = new Mutex(true, @"Local\ClipShelf.Single.v1", out created))
            {
                if (!created)
                {
                    try { using (EventWaitHandle ev = EventWaitHandle.OpenExisting(@"Local\ClipShelf.Show.v1")) ev.Set(); } catch { }
                    return 0;
                }
                EventWaitHandle showEv = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\ClipShelf.Show.v1");
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Log.W("未处理异常: " + e.Exception); };
                AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e) { Log.W("致命异常: " + e.ExceptionObject); };

                Settings cfg = new Settings(Path.Combine(baseDir, "settings.ini"));
                string shots = Path.Combine(baseDir, "shots");
                Directory.CreateDirectory(shots);
                ShelfForm form = new ShelfForm(cfg, shots, new Metrics(Dpi()), false);
                Thread th = new Thread(delegate()
                {
                    while (true)
                    {
                        showEv.WaitOne();
                        try { form.BeginInvoke(new Action(form.ShowFromSignal)); } catch { }
                    }
                });
                th.IsBackground = true;
                th.Start();
                Log.W("启动 v1.0 dpi=" + Dpi());
                Application.Run(form);
                Log.W("退出");
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        static float Dpi()
        {
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96f;
        }

        static void Install(string baseDir)
        {
            string desk = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string lnk = Path.Combine(desk, "截图盒.lnk");
            string ico = Path.Combine(baseDir, "ClipShelf.ico");
            Shortcut.Create(lnk, Application.ExecutablePath, baseDir, (File.Exists(ico) ? ico : Application.ExecutablePath) + ",0",
                "截图盒：截图自动收进来，拖出去就能发");
            Autostart.Set(true);
            Log.W("安装：桌面快捷方式 " + lnk + " + 开机自启");
        }

        static void Probe(string outPath)
        {
            StringBuilder sb = new StringBuilder();
            IntPtr fg = W32.GetForegroundWindow();
            int st; W32.SHQueryUserNotificationState(out st);
            sb.AppendLine("foreground=" + fg.ToInt64().ToString("X") + " notificationState=" + st);
            W32.EnumWindows(delegate(IntPtr h, IntPtr l)
            {
                if (!W32.IsWindowVisible(h)) return true;
                int cloaked; W32.DwmGetWindowAttribute(h, 14, out cloaked, 4);
                if (cloaked != 0) return true;
                W32.RECT r, mon;
                bool covers = FsDetect.CoversMonitor(h, out r, out mon);
                if (r.Right - r.Left < 50 || r.Bottom - r.Top < 50) return true;
                uint pid; W32.GetWindowThreadProcessId(h, out pid);
                int style = W32.GetWindowLong(h, W32.GWL_STYLE), ex = W32.GetWindowLong(h, W32.GWL_EXSTYLE);
                string title = FsDetect.TitleOf(h);
                if (title.Length > 40) title = title.Substring(0, 40);
                sb.AppendLine(string.Format("{0}{1,-10} {2,-26} {3,-28} rect=({4},{5},{6},{7}) covers={8} caption={9} zoomed={10} topmost={11} FULLSCREEN={12} \"{13}\"",
                    h == fg ? "* " : "  ", h.ToInt64().ToString("X"), FsDetect.ProcName(pid), FsDetect.ClassOf(h),
                    r.Left, r.Top, r.Right, r.Bottom, covers, (style & W32.WS_CAPTION) == W32.WS_CAPTION, W32.IsZoomed(h),
                    (ex & W32.WS_EX_TOPMOST) != 0, FsDetect.IsFullscreen(h), title));
                return true;
            }, IntPtr.Zero);
            File.WriteAllText(outPath, sb.ToString(), new UTF8Encoding(true));
        }

        // --snapshot out.png --dir 截图目录 [--select 0,2] [--hover 1] [--hoverx] [--collapsed] [--toast 文字] [--empty]
        static int Snapshot(string[] args)
        {
            string outPng = args[1], dir = null, toast = null;
            int[] sel = null; int hover = -1; bool hoverX = false, collapsed = false, flash = false, stay = false;
            for (int i = 2; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                if (a == "--dir") dir = args[++i];
                else if (a == "--select") sel = Array.ConvertAll(args[++i].Split(','), int.Parse);
                else if (a == "--hover") hover = int.Parse(args[++i]);
                else if (a == "--hoverx") hoverX = true;
                else if (a == "--collapsed") collapsed = true;
                else if (a == "--toast") toast = args[++i];
                else if (a == "--flash") flash = true;
                else if (a == "--stay") stay = true;
            }
            Settings cfg = new Settings(Path.Combine(Path.GetTempPath(), "clipshelf_snapshot_none.ini"));
            ShelfForm f = new ShelfForm(cfg, dir ?? Path.GetTempPath(), new Metrics(Dpi()), true);
            f.Location = new Point(-32000, -32000);
            f.Show();
            if (dir != null) f.AddLoaded(f.LoadExisting());
            f.Location = new Point(-32000, -32000);
            f.DebugState(sel, hover, hoverX, collapsed, toast, flash);
            if (stay) f.DebugStay(0.6f);
            f.Location = new Point(-32000, -32000);
            Application.DoEvents();
            using (Bitmap b = new Bitmap(f.Width, f.Height))
            {
                f.DrawToBitmap(b, new Rectangle(0, 0, f.Width, f.Height));
                b.Save(outPng, ImageFormat.Png);
            }
            f.Close();
            return 0;
        }
    }
}
