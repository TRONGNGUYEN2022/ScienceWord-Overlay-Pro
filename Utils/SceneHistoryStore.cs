using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScienceWordOverlay.Models;

namespace ScienceWordOverlay.Utils
{
    public class SavedSceneEntry
    {
        public int Id { get; set; }
        public string Timestamp { get; set; } = "";
        public string TargetApp { get; set; } = "";
        public string Label { get; set; } = "";
        public double BoxW { get; set; }
        public double BoxH { get; set; }
        public double OrigLeft { get; set; }
        public double OrigTop { get; set; }
        public SceneState Scene { get; set; } = new();
    }

    public static class SceneHistoryStore
    {
        private const ushort Magic = 0xA55A;
        private const int MaxEntries = 25;
        private static readonly string StoreDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ScienceWordOverlayPro");
        private static readonly string StorePath = Path.Combine(StoreDir, "scene_history_v74.json");

        private static List<SavedSceneEntry>? _cache;

        private static List<SavedSceneEntry> LoadAll()
        {
            if (_cache != null) return _cache;
            try
            {
                if (File.Exists(StorePath))
                {
                    string json = File.ReadAllText(StorePath);
                    _cache = JsonSerializer.Deserialize<List<SavedSceneEntry>>(json) ?? new List<SavedSceneEntry>();
                    return _cache;
                }
            }
            catch { }
            _cache = new List<SavedSceneEntry>();
            return _cache;
        }

        private static void SaveAll(List<SavedSceneEntry> list)
        {
            try
            {
                Directory.CreateDirectory(StoreDir);
                File.WriteAllText(StorePath, JsonSerializer.Serialize(list));
            }
            catch { }
        }

        public static List<SavedSceneEntry> GetRecent(int max = 12)
        {
            var list = LoadAll();
            return list.OrderByDescending(x => x.Id).Take(max).ToList();
        }

        public static SavedSceneEntry? GetById(int id)
        {
            return LoadAll().FirstOrDefault(x => x.Id == id);
        }

        public static SavedSceneEntry SaveScene(SceneState scene, Rect screenBoxDip, string targetApp)
        {
            var list = LoadAll();
            int nextId = list.Count > 0 ? list.Max(x => x.Id) + 1 : 1;
            if (nextId > 65000) nextId = 1;

            // Chuẩn hóa tọa độ tương đối theo góc trên-trái của screenBoxDip
            var rel = JsonSerializer.Deserialize<SceneState>(JsonSerializer.Serialize(scene)) ?? new SceneState();
            foreach (var p in rel.Points) { p.X -= screenBoxDip.Left; p.Y -= screenBoxDip.Top; }
            foreach (var st in rel.Strokes)
                for (int i = 0; i < st.Points.Count; i++)
                {
                    st.Points[i][0] -= screenBoxDip.Left;
                    st.Points[i][1] -= screenBoxDip.Top;
                }
            foreach (var t in rel.Texts) { t.X -= screenBoxDip.Left; t.Y -= screenBoxDip.Top; }
            foreach (var mf in rel.MathFormulas) { mf.X -= screenBoxDip.Left; mf.Y -= screenBoxDip.Top; }

            var ptNames = rel.Points.Where(p => !string.IsNullOrWhiteSpace(p.Label)).Select(p => p.Label).Take(6).ToList();
            string desc = ptNames.Count > 0
                ? $"Hình ({string.Join(",", ptNames)})"
                : (rel.MathFormulas.Count > 0 ? $"Công thức ({rel.MathFormulas.Count})" : (rel.Strokes.Count > 0 ? $"Nét bút ({rel.Strokes.Count} nét)" : "Hình vẽ"));

            var entry = new SavedSceneEntry
            {
                Id = nextId,
                Timestamp = DateTime.Now.ToString("HH:mm:ss"),
                TargetApp = string.IsNullOrWhiteSpace(targetApp) ? "App" : targetApp,
                Label = $"#{nextId} {desc} [{DateTime.Now:HH:mm}]",
                BoxW = screenBoxDip.Width,
                BoxH = screenBoxDip.Height,
                OrigLeft = screenBoxDip.Left,
                OrigTop = screenBoxDip.Top,
                Scene = rel
            };

            list.RemoveAll(x => x.Id == nextId);
            list.Add(entry);
            while (list.Count > MaxEntries) list.RemoveAt(0);
            SaveAll(list);
            return entry;
        }

        public static SceneState MaterializeSceneAt(SavedSceneEntry entry, Point targetTopLeftDip, double scaleX = 1.0, double scaleY = 1.0)
        {
            var sc = JsonSerializer.Deserialize<SceneState>(JsonSerializer.Serialize(entry.Scene)) ?? new SceneState();
            foreach (var p in sc.Points)
            {
                p.X = targetTopLeftDip.X + p.X * scaleX;
                p.Y = targetTopLeftDip.Y + p.Y * scaleY;
            }
            foreach (var st in sc.Strokes)
                for (int i = 0; i < st.Points.Count; i++)
                {
                    st.Points[i][0] = targetTopLeftDip.X + st.Points[i][0] * scaleX;
                    st.Points[i][1] = targetTopLeftDip.Y + st.Points[i][1] * scaleY;
                }
            foreach (var t in sc.Texts)
            {
                t.X = targetTopLeftDip.X + t.X * scaleX;
                t.Y = targetTopLeftDip.Y + t.Y * scaleY;
            }
            foreach (var mf in sc.MathFormulas)
            {
                mf.X = targetTopLeftDip.X + mf.X * scaleX;
                mf.Y = targetTopLeftDip.Y + mf.Y * scaleY;
            }
            return sc;
        }

        // Nhúng mã 40-bit (16-bit Magic + 16-bit SceneId + 8-bit Checksum) vô hình vào kênh Blue hàng đầu tiên (y = 0,1)
        // Dùng độ lệch màu rất nhỏ (255 vs 252) nên mắt thường không thấy, sống sót tốt qua Clipboard DIB của ScienceWord/Paint
        public static BitmapSource EmbedStegoId(BitmapSource src, int sceneId)
        {
            if (src.PixelWidth < 44 || src.PixelHeight < 4) return src;
            var wb = new WriteableBitmap(new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0));
            int w = wb.PixelWidth, h = wb.PixelHeight;
            int stride = w * 4;
            byte[] buf = new byte[h * stride];
            wb.CopyPixels(buf, stride, 0);

            ushort id16 = (ushort)(sceneId & 0xFFFF);
            byte chk = (byte)(((Magic >> 8) ^ (Magic & 0xFF) ^ (id16 >> 8) ^ (id16 & 0xFF)) & 0xFF);
            ulong bits = ((ulong)Magic << 24) | ((ulong)id16 << 8) | chk;

            for (int i = 0; i < 40; i++)
            {
                bool bit = ((bits >> (39 - i)) & 1UL) == 1UL;
                for (int row = 0; row < 2; row++)
                {
                    int idx = row * stride + (i + 1) * 4;
                    // Giữ alpha = 255 ở dải stego 2px trên nền trắng; đổi kênh Blue chẵn/lẻ dạng 255 (0) và 251 (1)
                    if (buf[idx + 3] == 255)
                    {
                        byte b = buf[idx];
                        if (bit) buf[idx] = (byte)(b >= 4 ? (b & 0xFC) : 0); // chia hết cho 4 (bit 1 -> 252)
                        else buf[idx] = (byte)Math.Min(255, (b & 0xFC) + 3); // dư 3 mod 4 (bit 0 -> 255)
                    }
                }
            }

            wb.WritePixels(new Int32Rect(0, 0, w, h), buf, stride, 0);
            wb.Freeze();
            return wb;
        }

        public static bool TryDecodeStegoId(BitmapSource src, out int sceneId)
        {
            sceneId = 0;
            if (src == null || src.PixelWidth < 44 || src.PixelHeight < 4) return false;
            try
            {
                var conv = new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
                int w = conv.PixelWidth, h = Math.Min(8, conv.PixelHeight);
                int stride = w * 4;
                byte[] buf = new byte[h * stride];
                conv.CopyPixels(new Int32Rect(0, 0, w, h), buf, stride, 0);

                // Quét nhẹ offset dòng 0..4 và cột 0..3 phòng trường hợp ScienceWord thêm/cắt viền 1-2px
                for (int row = 0; row < Math.Min(5, h); row++)
                {
                    for (int colOff = 0; colOff <= 3; colOff++)
                    {
                        if (colOff + 40 >= w) continue;
                        ulong bits = 0;
                        for (int i = 0; i < 40; i++)
                        {
                            int idx = row * stride + (colOff + i) * 4;
                            byte b = buf[idx];
                            bool bit = (b & 0x03) <= 1; // 252 -> 0 (<=1 -> bit 1), 255 -> 3 (>1 -> bit 0)
                            bits = (bits << 1) | (bit ? 1UL : 0UL);
                        }
                        ushort magic = (ushort)((bits >> 24) & 0xFFFF);
                        ushort id16 = (ushort)((bits >> 8) & 0xFFFF);
                        byte chk = (byte)(bits & 0xFF);
                        byte expChk = (byte)(((magic >> 8) ^ (magic & 0xFF) ^ (id16 >> 8) ^ (id16 & 0xFF)) & 0xFF);

                        if (magic == Magic && chk == expChk && id16 > 0)
                        {
                            sceneId = id16;
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }
    }
}
