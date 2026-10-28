// Soniox ends a sentence (".") when the speaker only pauses for 1-3 s. This filter decides
// with the real pause length in audio time (token start_ms / end_ms) instead:
//   gap < MinGapMs (2 s)      -> normal speech between sentences, keep the "."
//   MinGapMs <= gap < PauseMs -> the "." came from a thinking pause: remove it, lowercase the next word
//   gap >= PauseMs            -> real end of sentence, keep the "."
// A "." is held back until the next word arrives, or until the processed audio shows a long enough pause.
// No UI code: reusable together with SonioxClient.cs.
using System;
using System.Collections.Generic;
using System.Text;

namespace STT
{
    public sealed class SentencePauseFilter
    {
        /// <summary>A pause at least this long ends a sentence. 0 = filter off (Soniox decides).</summary>
        public int PauseMs;
        /// <summary>Shorter gaps are normal fluent speech; the "." is kept.</summary>
        public int MinGapMs = 2000;

        string held;            // "." waiting for the next word
        long heldAfterMs;       // end time of the word before the held "."
        long lastWordEndMs;     // last final word
        long lastHeardEndMs;    // last word, final or not

        public SentencePauseFilter(int pauseMs)
        {
            PauseMs = pauseMs;
        }

        /// <summary>Call once per response. Gives the text to type: new final text and the current non-final text.</summary>
        public void Process(SonioxResponse r, out string finalText, out string nonFinalText)
        {
            finalText = nonFinalText = "";
            if (r.Tokens == null) return;
            foreach (var t in r.Tokens)
                if (!t.IsControl && !string.IsNullOrEmpty(t.Text) && t.Text.Trim().Length > 0)
                    lastHeardEndMs = Math.Max(lastHeardEndMs, t.EndMs);
            finalText = AddFinal(r.Tokens);
            nonFinalText = NonFinal(r.Tokens);
            // Release the held "." when no word was heard for PauseMs of processed audio.
            if (held != null && lastHeardEndMs <= heldAfterMs && r.TotalAudioProcMs - heldAfterMs >= PauseMs)
                finalText += Flush();
        }

        /// <summary>Returns the held "." (for example when the session ends).</summary>
        public string Flush()
        {
            string h = held ?? "";
            held = null;
            return h;
        }

        string AddFinal(IEnumerable<SonioxToken> tokens)
        {
            var sb = new StringBuilder();
            foreach (var t in tokens)
            {
                if (!t.IsFinal || t.IsControl || string.IsNullOrEmpty(t.Text)) continue;
                // Soniox puts the "." inside the word token ("ning."), sometimes alone (".").
                string text = t.Text;
                bool endsSentence = PauseMs > 0 && EndsWithPeriod(text);
                if (endsSentence) text = text.Substring(0, text.Length - 1);
                bool hasWord = text.Trim().Length > 0;

                if (held != null && hasWord)
                {
                    long gap = t.StartMs - heldAfterMs;
                    if (gap >= MinGapMs && gap < PauseMs) text = LowerFirst(text);
                    else sb.Append(held);
                    held = null;
                }
                sb.Append(text);
                if (hasWord) lastWordEndMs = t.EndMs;
                if (endsSentence)
                {
                    held = (held ?? "") + ".";
                    heldAfterMs = lastWordEndMs;
                }
            }
            return sb.ToString();
        }

        // A "." at the end of the non-final text is hidden until it is decided.
        string NonFinal(IEnumerable<SonioxToken> tokens)
        {
            var sb = new StringBuilder();
            foreach (var t in tokens)
                if (!t.IsFinal && !t.IsControl && !string.IsNullOrEmpty(t.Text)) sb.Append(t.Text);
            string text = sb.ToString();
            return PauseMs > 0 && EndsWithPeriod(text) ? text.Substring(0, text.Length - 1) : text;
        }

        // "word." or "." but not "...". A "." inside a number ("3.5") is kept anyway: the gap to the next digit is short.
        static bool EndsWithPeriod(string text)
        {
            return text.EndsWith(".") && !text.EndsWith("..");
        }

        static string LowerFirst(string text)
        {
            string word = text.Trim();
            // "I", "I'm", "I'll" stay upper case.
            if (word == "I" || word.StartsWith("I'") || word.StartsWith("I’")) return text;
            for (int i = 0; i < text.Length; i++)
            {
                if (!char.IsLetter(text[i])) continue;
                if (!char.IsUpper(text[i])) return text;
                return text.Substring(0, i) + char.ToLowerInvariant(text[i]) + text.Substring(i + 1);
            }
            return text;
        }
    }
}
