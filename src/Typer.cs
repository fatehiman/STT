// Types text into the focused window with SendInput (Unicode, so Persian works
// with any keyboard layout). Non-final words can be typed early and corrected
// later with Backspace.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace STT
{
    sealed class Typer
    {
        // Final text not typed yet.
        string pendingFinal = "";
        // Current non-final text from the last response.
        string nonFinal = "";
        // Non-final text that is already on screen (may be deleted and retyped).
        string shown = "";
        bool atStart = true;
        IntPtr window;
        readonly Timer retry = new Timer { Interval = 50 };

        public bool TypePartial = true;

        public Typer()
        {
            retry.Tick += (s, e) => Flush();
        }

        /// <summary>Call at the start of each dictation session.</summary>
        public void Begin(bool typePartial)
        {
            TypePartial = typePartial;
            pendingFinal = nonFinal = shown = "";
            atStart = true;
            window = Native.GetForegroundWindow();
        }

        /// <summary>finalText is added once. nonFinalText replaces the previous non-final text.</summary>
        public void Add(string finalText, string nonFinalText)
        {
            if (atStart)
            {
                finalText = finalText.TrimStart();
                if (finalText.Length > 0) atStart = false;
            }
            pendingFinal += finalText;
            nonFinal = pendingFinal.Length == 0 && atStart ? nonFinalText.TrimStart() : nonFinalText;
            Flush();
        }

        void Flush()
        {
            // Wait until the hotkey modifiers are released, or Ctrl+Backspace etc. would happen.
            if (ModifiersDown())
            {
                retry.Start();
                return;
            }
            retry.Stop();

            var fg = Native.GetForegroundWindow();
            if (fg != window)
            {
                // Focus moved: we can not fix text in the old window anymore.
                window = fg;
                shown = "";
            }

            string desired = pendingFinal + (TypePartial ? nonFinal : "");
            int p = 0;
            while (p < shown.Length && p < desired.Length && shown[p] == desired[p]) p++;
            if (p > 0 && p < shown.Length && char.IsHighSurrogate(shown[p - 1])) p--;
            int back = CodePoints(shown.Substring(p));
            string add = desired.Substring(p);
            if (back > 0 || add.Length > 0) Send(back, add);

            pendingFinal = "";
            shown = TypePartial ? nonFinal : "";
        }

        static int CodePoints(string s)
        {
            int n = 0;
            for (int i = 0; i < s.Length; i++)
                if (!char.IsLowSurrogate(s[i])) n++;
            return n;
        }

        static bool ModifiersDown()
        {
            foreach (int vk in new[] { Native.VK_SHIFT, Native.VK_CONTROL, Native.VK_MENU, Native.VK_LWIN, Native.VK_RWIN })
                if ((Native.GetAsyncKeyState(vk) & 0x8000) != 0) return true;
            return false;
        }

        static void Send(int backspaces, string text)
        {
            var inputs = new List<Native.INPUT>();
            for (int i = 0; i < backspaces; i++) AddKey(inputs, Native.VK_BACK);
            foreach (char c in text)
            {
                if (c == '\r') continue;
                if (c == '\n') { AddKey(inputs, Native.VK_RETURN); continue; }
                inputs.Add(Unicode(c, false));
                inputs.Add(Unicode(c, true));
            }
            int size = Marshal.SizeOf(typeof(Native.INPUT));
            const int chunk = 200;
            for (int i = 0; i < inputs.Count; i += chunk)
            {
                var part = inputs.GetRange(i, Math.Min(chunk, inputs.Count - i)).ToArray();
                Native.SendInput((uint)part.Length, part, size);
            }
        }

        static void AddKey(List<Native.INPUT> inputs, int vk)
        {
            foreach (bool up in new[] { false, true })
            {
                var input = new Native.INPUT { type = Native.INPUT_KEYBOARD };
                input.u.ki.wVk = (ushort)vk;
                input.u.ki.dwFlags = up ? Native.KEYEVENTF_KEYUP : 0;
                inputs.Add(input);
            }
        }

        static Native.INPUT Unicode(char c, bool up)
        {
            var input = new Native.INPUT { type = Native.INPUT_KEYBOARD };
            input.u.ki.wScan = c;
            input.u.ki.dwFlags = Native.KEYEVENTF_UNICODE | (up ? Native.KEYEVENTF_KEYUP : 0);
            return input;
        }
    }
}
