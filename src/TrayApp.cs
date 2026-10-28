using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

namespace STT
{
    sealed class TrayApp : ApplicationContext
    {
        enum State { Idle, Starting, Listening, Stopping }

        const int HotkeyId = 1;

        readonly HostWindow host;
        readonly NotifyIcon tray;
        readonly ToolStripMenuItem toggleItem;
        readonly Icon idleIcon, busyIcon, listeningIcon, appIcon;
        readonly Typer typer = new Typer();
        readonly Timer autoStop = new Timer { Interval = 1000 };
        readonly Timer stopTimeout = new Timer { Interval = 5000 };
        readonly Settings settings = Settings.Load();
        readonly SynchronizationContext ui = new WindowsFormsSynchronizationContext();

        State state = State.Idle;
        SonioxClient client;
        MicRecorder mic;
        SentencePauseFilter filter;
        SettingsForm settingsForm;

        public TrayApp()
        {
            int small = SystemInformation.SmallIconSize.Width;
            idleIcon = Icons.Create(Icons.Idle, small);
            busyIcon = Icons.Create(Icons.Busy, small);
            listeningIcon = Icons.Create(Icons.Listening, small);
            appIcon = Icons.Create(Icons.Listening, 32);

            host = new HostWindow();
            host.Hotkey += Toggle;
            host.CloseRequested += Exit;

            toggleItem = new ToolStripMenuItem("Start listening", null, (s, e) => Toggle());
            var menu = new ContextMenuStrip();
            menu.Items.Add(toggleItem);
            menu.Items.Add("Settings...", null, (s, e) => OpenSettings());
            menu.Items.Add("Open log", null, (s, e) => OpenLog());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Exit", null, (s, e) => Exit());

            tray = new NotifyIcon { ContextMenuStrip = menu, Visible = true };
            tray.MouseDoubleClick += (s, e) => { if (e.Button == MouseButtons.Left) OpenSettings(); };

            autoStop.Tick += (s, e) => CheckAutoStop();
            stopTimeout.Tick += (s, e) => { Log.Write("stop: no 'finished' from server in time"); Cleanup(); };

            UpdateUi();
            string err = RegisterHotkey(settings.HotkeyModifiers, settings.HotkeyKey);
            Log.Write("hotkey " + HotkeyText + ": " + (err ?? "registered"));
            if (err != null) tray.ShowBalloonTip(5000, "STT", err + " Open Settings to choose another hotkey.", ToolTipIcon.Warning);
            if (string.IsNullOrEmpty(settings.GetApiKey()))
                tray.ShowBalloonTip(5000, "STT", "Please set your Soniox API key in Settings.", ToolTipIcon.Info);
            Log.Write("started");
        }

        // Runs the action on the UI thread.
        void Post(Action action) => ui.Post(_ => action(), null);

        string HotkeyText => Settings.HotkeyText(settings.HotkeyModifiers, settings.HotkeyKey);

        /// <summary>Registers the hotkey. Returns null on success or an error text.</summary>
        string RegisterHotkey(int mods, int key)
        {
            Native.UnregisterHotKey(host.Handle, HotkeyId);
            if (Native.RegisterHotKey(host.Handle, HotkeyId, mods | Native.MOD_NOREPEAT, key)) return null;
            return Settings.HotkeyText(mods, key) + " is used by another program.";
        }

        void Toggle()
        {
            switch (state)
            {
                case State.Idle: Start(); break;
                case State.Starting:
                case State.Listening: Stop(); break;
            }
        }

        void Start()
        {
            string key = settings.GetApiKey();
            if (string.IsNullOrEmpty(key))
            {
                tray.ShowBalloonTip(5000, "STT", "Please set your Soniox API key in Settings.", ToolTipIcon.Warning);
                OpenSettings();
                return;
            }

            state = State.Starting;
            UpdateUi();
            if (settings.PlaySounds) Sounds.Start();
            typer.Begin(settings.TypePartial);
            filter = new SentencePauseFilter(settings.SentencePauseMs);

            var c = new SonioxClient { FinalizeAfterPauseMs = settings.SentencePauseMs };
            client = c;
            c.Response += r => Post(() => OnResponse(c, r));
            c.Finished += () => Post(() => OnFinished(c));
            c.Failed += m => Post(() => OnFailed(c, m));

            // Start the mic first: audio is queued while the connection opens, so the first words are not lost.
            try
            {
                mic = new MicRecorder();
                mic.Data += c.SendAudio;
                mic.Start(MicRecorder.FindDevice(settings.Microphone));
            }
            catch (Exception ex)
            {
                OnFailed(c, ex.Message);
                return;
            }

            autoStop.Start();
            Log.Write("session start");
            Connect(c, key);
        }

        async void Connect(SonioxClient c, string key)
        {
            try
            {
                await c.ConnectAsync(key, settings.ToSonioxConfig());
                if (client == c && state == State.Starting)
                {
                    state = State.Listening;
                    UpdateUi();
                }
            }
            catch (Exception ex)
            {
                var msg = ex.InnerException != null ? ex.Message + " " + ex.InnerException.Message : ex.Message;
                OnFailed(c, "cannot connect to Soniox: " + msg);
            }
        }

        void Stop()
        {
            if (client == null) return;
            state = State.Stopping;
            UpdateUi();
            if (settings.PlaySounds) Sounds.Stop();
            autoStop.Stop();
            mic?.Dispose();
            mic = null;
            client.EndOfAudio();
            stopTimeout.Start();
        }

        void OnResponse(SonioxClient c, SonioxResponse r)
        {
            if (c != client || r.Tokens == null) return;
            filter.Process(r, out string fin, out string nonFinal);
            typer.Add(fin, nonFinal);
        }

        void OnFinished(SonioxClient c)
        {
            if (c != client) return;
            typer.Add(filter.Flush(), ""); // also removes non-final words that were never confirmed
            Log.Write("session finished");
            Cleanup();
        }

        void OnFailed(SonioxClient c, string message)
        {
            if (c != client) return;
            Log.Write("error: " + message);
            if (settings.PlaySounds) Sounds.Error();
            tray.ShowBalloonTip(8000, "STT error", message, ToolTipIcon.Error);
            Cleanup();
        }

        void CheckAutoStop()
        {
            if (state == State.Listening && settings.AutoStopSeconds > 0 &&
                (DateTime.UtcNow - client.LastSpeechUtc).TotalSeconds >= settings.AutoStopSeconds)
            {
                Log.Write("auto stop: no speech for " + settings.AutoStopSeconds + " s");
                Stop();
            }
        }

        void Cleanup()
        {
            autoStop.Stop();
            stopTimeout.Stop();
            mic?.Dispose();
            mic = null;
            client?.Dispose();
            client = null;
            state = State.Idle;
            UpdateUi();
        }

        void UpdateUi()
        {
            string status;
            switch (state)
            {
                case State.Starting: tray.Icon = busyIcon; status = "connecting..."; break;
                case State.Listening: tray.Icon = listeningIcon; status = "listening"; break;
                case State.Stopping: tray.Icon = busyIcon; status = "finishing..."; break;
                default: tray.Icon = idleIcon; status = "idle"; break;
            }
            string tip = "STT - " + status + " (" + HotkeyText + ")";
            tray.Text = tip.Length > 63 ? tip.Substring(0, 63) : tip;
            toggleItem.Text = (state == State.Idle ? "Start listening" : "Stop listening") + "\t" + HotkeyText;
            toggleItem.Enabled = state != State.Stopping;
        }

        void OpenSettings()
        {
            if (settingsForm != null)
            {
                settingsForm.Activate();
                return;
            }
            // Free the hotkey while the window is open, so it can be pressed in the hotkey box.
            Native.UnregisterHotKey(host.Handle, HotkeyId);
            settingsForm = new SettingsForm(settings, appIcon, (m, k) =>
            {
                string err = RegisterHotkey(m, k);
                Native.UnregisterHotKey(host.Handle, HotkeyId);
                return err;
            });
            settingsForm.FormClosed += (s, e) =>
            {
                settingsForm = null;
                RegisterHotkey(settings.HotkeyModifiers, settings.HotkeyKey);
                UpdateUi();
            };
            settingsForm.Show();
        }

        void OpenLog()
        {
            Log.Write("open log");
            try { System.Diagnostics.Process.Start(System.IO.Path.Combine(Settings.Dir, "stt.log")); } catch { }
        }

        void Exit()
        {
            Log.Write("exit");
            Cleanup();
            settingsForm?.Close();
            Native.UnregisterHotKey(host.Handle, HotkeyId);
            tray.Visible = false;
            tray.Dispose();
            host.DestroyHandle();
            ExitThread();
        }

        /// <summary>
        /// Hidden window: receives WM_HOTKEY, and WM_CLOSE (so the app closes cleanly when asked).
        /// </summary>
        sealed class HostWindow : NativeWindow
        {
            public event Action Hotkey;
            public event Action CloseRequested;

            public HostWindow()
            {
                CreateHandle(new CreateParams { Caption = "STT host" });
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == Native.WM_HOTKEY) { Hotkey?.Invoke(); return; }
                if (m.Msg == Native.WM_CLOSE) { CloseRequested?.Invoke(); return; }
                base.WndProc(ref m);
            }
        }
    }
}
