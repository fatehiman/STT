# STT — voice typing for Windows

A very small Windows tray app (about 60 KB). Press a hotkey, speak, and your words are typed into the window you are using, while you speak (like GBoard voice typing).

- Speech engine: **[Soniox](https://soniox.com) real-time API** (`stt-rt-v5`), $0.12 per hour of audio.
- Languages: English + Persian by default (any of Soniox's 60+ languages can be set).
- Typing uses Unicode key events, so Persian text works with any keyboard layout.
- Needs only .NET Framework 4.8, which is part of Windows 10/11. No other DLLs.

## How to use

1. Run `STT.exe`. A grey microphone icon appears in the tray.
2. Right-click the icon → **Settings...** (or double-click the icon).
   - Set your **hotkey** (click the box, then press the keys). Default: `Ctrl+Shift+Space`.
   - Paste your **Soniox API key** (from [console.soniox.com](https://console.soniox.com)).
3. Click in any text field, press the hotkey, and speak.
4. Press the hotkey again to stop.

Tray icon colors:

| Color | Meaning |
|---|---|
| Grey | Idle |
| Orange | Connecting / finishing |
| Red | Listening |

## Settings

| Setting | Meaning |
|---|---|
| Hotkey | Starts and stops listening. Needs Ctrl, Alt, Shift or Win (or an F key alone). |
| Soniox API key | Saved encrypted with Windows DPAPI (only your Windows user can read it). If empty, the `SONIOX_API_KEY` environment variable is used. |
| Model | Soniox real-time model. Default `stt-rt-v5`. |
| Languages | Language hints, for example `en, fa`. |
| Custom words | Names or technical words that the model should know (sent as `context.terms`). |
| Microphone | Input device. Default: the Windows default device. |
| End sentence after | Pause length (seconds) that really ends a sentence. Default **6**. Soniox puts a "." after a 1–3 s thinking pause; the app removes such a "." (pause between 2 s and this value) and writes the next word in lower case. A "." after a short gap (< 2 s, normal speech) or after a pause of this length is kept. `0` = Soniox decides (its own endpoint detection, about 2 s). |
| Auto stop after | Stops after this many seconds with no speech. `0` = never. Soniox bills the whole stream time, also silence, so keep this on. |
| Type words while I speak | On: words are typed at once and fixed later with Backspace if Soniox changes them. Off: only final words are typed (a little slower, never corrected). |
| Play a short sound | Beep on start and stop. |

Files are in `%APPDATA%\STT\`: `settings.json` and `stt.log`.

## Command line

| Command | What it does |
|---|---|
| `STT.exe` | Starts the tray app. Only one copy can run. |
| `STT.exe --test` | Checks the API key and the connection (sends 2 s of silence). Result in `stt.log`. Exit code 0 = OK. |
| `STT.exe --test file.wav [pause]` | Transcribes an audio file (wav, mp3, ...) in real time and writes the tokens and the text to `stt.log`. `pause` overrides "End sentence after" (seconds). |
| `STT.exe --save-key` | Saves the `SONIOX_API_KEY` environment variable into the settings (encrypted). |

Save the key from Bitwarden without seeing it:

```powershell
bwprx-client run --query Soniox --env SONIOX_API_KEY=password -- E:\www\STT\dist\STT.exe --save-key
```

## Build

Needs the .NET SDK (8 or newer). It builds for .NET Framework 4.8.

```powershell
powershell -ExecutionPolicy Bypass -File build.ps1
```

Output: `dist\STT.exe` (+ `STT.exe.config`). The script stops a running `dist\STT.exe` first.
`tools\make-icon.ps1` makes `assets\stt.ico` (only needed if the icon is missing).

## Code

| File | What it does |
|---|---|
| `src/SonioxClient.cs` | Soniox streaming client (WebSocket). No UI code — **copy it into other projects**. Supports language hints, custom words, speaker diarization, language identification, endpoint detection, finalize, keepalive, and `FinalizeAfterPauseMs` (sends `finalize` after a pause, measured in audio time). |
| `src/SentencePauseFilter.cs` | Decides if a "." stays, from the real pause length (token `start_ms`/`end_ms`, in audio time). No UI code — reusable with `SonioxClient.cs`. |
| `src/MicRecorder.cs` | Microphone capture with winmm `waveIn` (16 kHz mono 16-bit, 100 ms chunks). |
| `src/Typer.cs` | Types text with `SendInput` (Unicode). Fixes changed non-final words with Backspace. Waits until Ctrl/Alt/Shift/Win are released. |
| `src/TrayApp.cs` | Tray icon, menu, hotkey, start/stop of a session, auto stop. |
| `src/SettingsForm.cs` | Settings window and hotkey box. |
| `src/Settings.cs` | Settings file, DPAPI key storage, log. |
| `src/Media.cs` | Tray icons and beeps, drawn/generated in code. |
| `src/Program.cs` | Single-instance check and command-line modes. |

### Using SonioxClient in another project

```csharp
var c = new SonioxClient();
c.Response += r => { foreach (var t in r.Tokens) if (t.IsFinal && !t.IsControl) Console.Write(t.Text); };
c.Finished += () => Console.WriteLine("\n[done]");
c.Failed += msg => Console.WriteLine("error: " + msg);
await c.ConnectAsync(apiKey, new SonioxConfig {
    LanguageHints = new[] { "en", "fa" },
    EnableSpeakerDiarization = true,   // token.Speaker = "1", "2", ... (good for meetings)
});
c.SendAudio(pcm16kMono);   // many times
c.EndOfAudio();            // then wait for Finished
```

## How the sentence pause works

- Soniox has no option for this. Its `max_endpoint_delay_ms` is at most 3 s, and the model adds "." after a pause even when endpoint detection is off (tested 2026-10-28).
- So when "End sentence after" is above 0, the app turns Soniox endpoint detection off and:
  1. holds back a "." that ends a word, until the next word comes;
  2. measures the gap in audio time: end of the word before the "." → start of the next word;
  3. gap 2 s … setting: removes the "." and lowercases the next word (not "I", "I'm", ...). Otherwise keeps it;
  4. if no word comes for the full pause (in processed audio), types the "." and sends `finalize`.
- Only "." is changed. "?" and "!" are kept. Lowercasing can also hit a name ("John" → "john") after a thinking pause.

## Limits

- If you click into another window while speaking, the text continues in the new window. Words already typed in the old window can not be fixed anymore.
- Some apps (for example games, or apps that run as administrator) do not accept typed input from a normal app.
- One Soniox stream can be up to 300 minutes long.
