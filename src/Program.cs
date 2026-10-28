using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace STT
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--test")
                return Test(args.Length > 1 ? args[1] : null, args.Length > 2 ? args[2] : null);
            if (args.Length > 0 && args[0] == "--save-key")
                return SaveKeyFromEnv();

            using (new Mutex(true, @"Local\STTSingleInstance", out bool created))
            {
                if (!created)
                {
                    Native.MessageBoxW(IntPtr.Zero, "STT is already running.", "STT", Native.MB_ICONERROR | Native.MB_TOPMOST);
                    return 1;
                }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
                return 0;
            }
        }

        /// <summary>
        /// STT.exe --save-key: saves the SONIOX_API_KEY environment variable into the settings (encrypted).
        /// Example: bwprx-client run --query Soniox --env SONIOX_API_KEY=password -- STT.exe --save-key
        /// </summary>
        static int SaveKeyFromEnv()
        {
            string key = Environment.GetEnvironmentVariable("SONIOX_API_KEY") ?? "";
            if (key.Trim().Length == 0)
            {
                Log.Write("save-key: SONIOX_API_KEY is empty");
                return 2;
            }
            var settings = Settings.Load();
            settings.SetApiKey(key);
            settings.Save();
            Log.Write("save-key: API key saved");
            return 0;
        }

        /// <summary>
        /// STT.exe --test [file [pauseSeconds]]: checks the API key and the connection to Soniox.
        /// Without a file it sends 2 s of silence. With a file (wav, mp3, ...) it transcribes the file.
        /// The result goes to the log (%APPDATA%\STT\stt.log). Exit code 0 = OK.
        /// </summary>
        static int Test(string file, string pauseSeconds)
        {
            var settings = Settings.Load();
            if (pauseSeconds != null)
                settings.SentencePauseSeconds = double.Parse(pauseSeconds, System.Globalization.CultureInfo.InvariantCulture);
            string key = settings.GetApiKey();
            if (key.Length == 0)
            {
                Log.Write("test: no API key");
                return 2;
            }

            var config = settings.ToSonioxConfig();
            byte[] audio;
            if (file != null)
            {
                config.AudioFormat = "auto";
                audio = File.ReadAllBytes(file);
            }
            else
            {
                audio = new byte[MicRecorder.SampleRate * 2 * 2];
            }

            string result = null;
            var text = new StringBuilder();
            var done = new ManualResetEventSlim();
            using (var c = new SonioxClient { FinalizeAfterPauseMs = settings.SentencePauseMs })
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                var filter = new SentencePauseFilter(settings.SentencePauseMs);
                Action<string> add = part =>
                {
                    if (part.Length == 0) return;
                    text.Append(part);
                    Log.Write("test: " + clock.Elapsed.TotalSeconds.ToString("0.0") + " s final: " + part);
                };
                c.Response += r =>
                {
                    if (r.Tokens == null) return;
                    var raw = new StringBuilder();
                    foreach (var t in r.Tokens)
                        if (t.IsFinal) raw.Append("[" + t.Text + "|" + t.StartMs + "-" + t.EndMs + "]");
                    if (raw.Length > 0) Log.Write("test: tokens " + raw);
                    filter.Process(r, out string fin, out _);
                    add(fin);
                };
                c.Finished += () => { result = "OK"; done.Set(); };
                c.Failed += m => { result = "FAILED: " + m; done.Set(); };
                try
                {
                    c.ConnectAsync(key, config).GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    Log.Write("test: FAILED to connect: " + ex.Message + (ex.InnerException != null ? " " + ex.InnerException.Message : ""));
                    return 1;
                }
                // Send in real time (16 kHz mono 16-bit = 32000 bytes/s), so pauses are real pauses.
                const int chunk = 3200;
                for (int i = 0; i < audio.Length && !done.IsSet; i += chunk)
                {
                    var part = new byte[Math.Min(chunk, audio.Length - i)];
                    Buffer.BlockCopy(audio, i, part, 0, part.Length);
                    c.SendAudio(part);
                    Thread.Sleep(100);
                }
                c.EndOfAudio();
                if (!done.Wait(30000)) result = "FAILED: no answer in 30 s";
                add(filter.Flush());
            }
            Log.Write("test: " + result + " | text: " + text);
            return result == "OK" ? 0 : 1;
        }
    }
}
