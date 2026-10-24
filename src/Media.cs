// Tray icons and short beep sounds, made in code (no resource files).
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;

namespace STT
{
    static class Icons
    {
        public static readonly Color Idle = Color.FromArgb(110, 118, 129);
        public static readonly Color Busy = Color.FromArgb(214, 140, 40);
        public static readonly Color Listening = Color.FromArgb(214, 60, 60);

        /// <summary>A round icon with a white microphone.</summary>
        public static Icon Create(Color background, int size)
        {
            using (var bmp = Draw(background, size))
                return Icon.FromHandle(bmp.GetHicon());
        }

        public static Bitmap Draw(Color background, int size)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float s = size / 16f;
                using (var bg = new SolidBrush(background))
                    g.FillEllipse(bg, 0, 0, size - 1, size - 1);
                using (var white = new SolidBrush(Color.White))
                using (var pen = new Pen(Color.White, Math.Max(1f, 1.3f * s)))
                using (var capsule = new GraphicsPath())
                {
                    // Mic head: a capsule 4 x 7 at the top center.
                    float w = 4 * s, h = 7 * s, x = 6 * s, y = 2.5f * s;
                    capsule.AddArc(x, y, w, w, 180, 180);
                    capsule.AddArc(x, y + h - w, w, w, 0, 180);
                    capsule.CloseFigure();
                    g.FillPath(white, capsule);
                    // Holder arc, stem and base.
                    g.DrawArc(pen, 4.5f * s, 4.5f * s, 7 * s, 6.5f * s, 0, 180);
                    g.DrawLine(pen, 8 * s, 11 * s, 8 * s, 13 * s);
                    g.DrawLine(pen, 6 * s, 13.2f * s, 10 * s, 13.2f * s);
                }
            }
            return bmp;
        }
    }

    static class Sounds
    {
        static readonly SoundPlayer StartPlayer = new SoundPlayer(Tone(new[] { 660, 990 }));
        static readonly SoundPlayer StopPlayer = new SoundPlayer(Tone(new[] { 990, 660 }));
        static readonly SoundPlayer ErrorPlayer = new SoundPlayer(Tone(new[] { 330, 330 }));

        public static void Start() { Play(StartPlayer); }
        public static void Stop() { Play(StopPlayer); }
        public static void Error() { Play(ErrorPlayer); }

        static void Play(SoundPlayer p)
        {
            try { p.Stream.Position = 0; p.Play(); } catch { }
        }

        /// <summary>A WAV file in memory: one 70 ms beep for each frequency.</summary>
        static Stream Tone(int[] freqs)
        {
            const int rate = 22050;
            int per = rate * 70 / 1000;
            int samples = per * freqs.Length;
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(0x46464952); w.Write(36 + samples * 2); w.Write(0x45564157); // RIFF, size, WAVE
            w.Write(0x20746d66); w.Write(16); w.Write((short)1); w.Write((short)1); // fmt, PCM, mono
            w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write(0x61746164); w.Write(samples * 2); // data
            for (int f = 0; f < freqs.Length; f++)
                for (int i = 0; i < per; i++)
                {
                    double env = Math.Min(1, Math.Min(i, per - i) / (rate * 0.008)); // soft edges
                    w.Write((short)(Math.Sin(2 * Math.PI * freqs[f] * i / rate) * 6000 * env));
                }
            ms.Position = 0;
            return ms;
        }
    }
}
