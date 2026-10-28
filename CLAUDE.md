# STT — notes for AI assistants

- Windows tray app, C#, .NET Framework 4.8 (`net48`), built with the .NET SDK. Goal: a very small exe with no extra DLLs. Do not add NuGet packages unless really needed.
- Speech engine: Soniox real-time WebSocket API. `src/SonioxClient.cs` must stay free of UI code, so it can be reused in other projects.
- `src/SentencePauseFilter.cs` (also UI-free) removes "." that Soniox adds after short thinking pauses. Pauses are measured in audio time (token `start_ms`/`end_ms`, `total_audio_proc_ms`), never wall-clock time: Soniox answers 0.5–2 s late, and a wall-clock `finalize` cut sentences in the middle.
- Test with real audio: `dist\STT.exe --test file.wav [pauseSeconds]` (sends in real time, logs raw tokens). Make test speech with System.Speech (PromptBuilder.AppendBreak for pauses). Never overwrite `%APPDATA%\STT\settings.json` in tests.
- Read `README.md` for features and the file list. `research.md` has the earlier research (why streaming, which providers).
- Build: `powershell -ExecutionPolicy Bypass -File build.ps1` → `dist\STT.exe`. The script stops the running `dist\STT.exe` first; start it again after the build.
- Auto start: line `00:03:40,E:\www\STT\dist\STT.exe` in `E:\appServices\DelayedTaskRunnerHome\DelayedTaskRunner.txt` (added 2026-10-24). So the app runs from `dist\`.
- Test the API connection: `dist\STT.exe --test` → result in `%APPDATA%\STT\stt.log`.
- Soniox API key is in Bitwarden, folder `AI Providers`, item `Soniox`. Use `bwprx-client run ... -- STT.exe --save-key`, never print the key.
- Desktop app rules apply: single instance, centered windows, tray menu with Exit last, clean close on WM_CLOSE (hidden `STT host` window).
- `reference-docs/` is for the user's own files. All `.md` files stay in the root.
