using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace STT
{
    sealed class SettingsForm : Form
    {
        readonly Settings settings;
        readonly Func<int, int, string> tryHotkey;
        readonly HotkeyBox hotkey = new HotkeyBox();
        readonly TextBox apiKey = new TextBox { UseSystemPasswordChar = true, Width = 320 };
        readonly TextBox model = new TextBox { Width = 320 };
        readonly TextBox languages = new TextBox { Width = 320 };
        readonly TextBox terms = new TextBox { Width = 320 };
        readonly ComboBox mic = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 320 };
        readonly NumericUpDown autoStop = new NumericUpDown { Minimum = 0, Maximum = 3600, Width = 80 };
        readonly CheckBox typePartial = new CheckBox { Text = "Type words while I speak (they may be corrected)", AutoSize = true };
        readonly CheckBox sounds = new CheckBox { Text = "Play a short sound on start and stop", AutoSize = true };
        readonly Label error = new Label { ForeColor = Color.Firebrick, AutoSize = true, MaximumSize = new Size(440, 0) };

        /// <param name="tryHotkey">Registers a hotkey; returns null on success or an error text.</param>
        public SettingsForm(Settings settings, Icon icon, Func<int, int, string> tryHotkey)
        {
            this.settings = settings;
            this.tryHotkey = tryHotkey;

            Text = "STT Settings";
            Icon = icon;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            AutoScaleMode = AutoScaleMode.Font;
            Font = SystemFonts.MessageBoxFont;
            TopMost = true;
            Padding = new Padding(10);

            var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            hotkey.Width = 320;
            hotkey.Set(settings.HotkeyModifiers, settings.HotkeyKey);
            apiKey.Text = settings.GetApiKey();
            model.Text = settings.Model;
            languages.Text = settings.LanguageHints;
            terms.Text = settings.Terms;
            autoStop.Value = Math.Max(0, Math.Min(3600, settings.AutoStopSeconds));
            typePartial.Checked = settings.TypePartial;
            sounds.Checked = settings.PlaySounds;

            mic.Items.Add("(Windows default)");
            foreach (var name in MicRecorder.ListDevices()) mic.Items.Add(name);
            int idx = string.IsNullOrEmpty(settings.Microphone) ? 0 : mic.Items.IndexOf(settings.Microphone);
            mic.SelectedIndex = idx < 0 ? 0 : idx;

            var link = new LinkLabel { Text = "Get a key at console.soniox.com", AutoSize = true };
            link.LinkClicked += (s, e) => Process.Start("https://console.soniox.com");

            Row(grid, "Hotkey:", hotkey, "Click here, then press the keys. Press it again to stop.");
            Row(grid, "Soniox API key:", apiKey, null);
            Row(grid, "", link, null);
            Row(grid, "Model:", model, null);
            Row(grid, "Languages:", languages, "Language codes, for example: en, fa");
            Row(grid, "Custom words:", terms, "Names or technical words, comma separated (optional)");
            Row(grid, "Microphone:", mic, null);
            var stopRow = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty, WrapContents = false };
            stopRow.Controls.Add(autoStop);
            stopRow.Controls.Add(new Label { Text = "seconds without speech (0 = never)", AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 6, 0, 0) });
            Row(grid, "Auto stop after:", stopRow, null);
            Row(grid, "", typePartial, null);
            Row(grid, "", sounds, null);
            Row(grid, "", error, null);

            var ok = new Button { Text = "Save", DialogResult = DialogResult.None, AutoSize = true };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            ok.Click += (s, e) => SaveAndClose();
            AcceptButton = ok;
            CancelButton = cancel;
            var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill };
            buttons.Controls.Add(cancel);
            buttons.Controls.Add(ok);
            grid.Controls.Add(buttons, 0, grid.RowCount);
            grid.SetColumnSpan(buttons, 2);

            Controls.Add(grid);
        }

        static void Row(TableLayoutPanel grid, string label, Control control, string hint)
        {
            int r = grid.RowCount++;
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Padding = new Padding(0, 6, 6, 0) }, 0, r);
            if (hint == null)
            {
                grid.Controls.Add(control, 1, r);
                return;
            }
            var box = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Margin = Padding.Empty, WrapContents = false };
            box.Controls.Add(control);
            box.Controls.Add(new Label { Text = hint, AutoSize = true, ForeColor = SystemColors.GrayText, Margin = new Padding(3, 0, 3, 6) });
            grid.Controls.Add(box, 1, r);
        }

        void SaveAndClose()
        {
            if (hotkey.Key == 0)
            {
                error.Text = "Please set a hotkey.";
                return;
            }
            string err = tryHotkey(hotkey.Modifiers, hotkey.Key);
            if (err != null)
            {
                error.Text = err;
                return;
            }
            settings.HotkeyModifiers = hotkey.Modifiers;
            settings.HotkeyKey = hotkey.Key;
            settings.SetApiKey(apiKey.Text);
            settings.Model = model.Text.Trim();
            settings.LanguageHints = languages.Text.Trim();
            settings.Terms = terms.Text.Trim();
            settings.Microphone = mic.SelectedIndex <= 0 ? "" : (string)mic.SelectedItem;
            settings.AutoStopSeconds = (int)autoStop.Value;
            settings.TypePartial = typePartial.Checked;
            settings.PlaySounds = sounds.Checked;
            settings.Save();
            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }
    }

    /// <summary>A text box that records a key combination instead of text.</summary>
    sealed class HotkeyBox : TextBox
    {
        public int Modifiers { get; private set; }
        public int Key { get; private set; }

        public HotkeyBox()
        {
            ReadOnly = true;
            BackColor = SystemColors.Window;
            ShortcutsEnabled = false;
        }

        public void Set(int mods, int key)
        {
            Modifiers = mods;
            Key = key;
            Text = Settings.HotkeyText(mods, key);
        }

        protected override bool IsInputKey(Keys keyData) => true;

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Every key (also Tab, Enter, Esc) goes to OnKeyDown and can be part of the hotkey.
            return false;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.Handled = e.SuppressKeyPress = true;
            int mods = 0;
            if (e.Control) mods |= Native.MOD_CONTROL;
            if (e.Alt) mods |= Native.MOD_ALT;
            if (e.Shift) mods |= Native.MOD_SHIFT;
            if ((Native.GetKeyState(Native.VK_LWIN) & 0x8000) != 0 || (Native.GetKeyState(Native.VK_RWIN) & 0x8000) != 0)
                mods |= Native.MOD_WIN;

            var key = e.KeyCode;
            bool modifierOnly = key == Keys.ControlKey || key == Keys.ShiftKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin;
            if (modifierOnly)
            {
                Text = Settings.HotkeyText(mods, 0) + "...";
                return;
            }
            bool functionKey = key >= Keys.F1 && key <= Keys.F24;
            if (mods == 0 && !functionKey)
            {
                Text = "Use at least one of Ctrl / Alt / Shift / Win (or an F key)";
                return;
            }
            Set(mods, (int)key);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            e.Handled = true;
            if (Text.EndsWith("...") || Text.StartsWith("Use at least")) Text = Settings.HotkeyText(Modifiers, Key);
        }
    }
}
