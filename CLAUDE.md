# STT — notes for AI assistants

- Windows tray app, C#, .NET Framework 4.8 (`net48`), built with the .NET SDK. Goal: a very small exe with no extra DLLs. Do not add NuGet packages unless really needed.
- Speech engine: Soniox real-time WebSocket API. `src/SonioxClient.cs` must stay free of UI code, so it can be reused in other projects.
- Read `README.md` for features and the file list. `research.md` has the earlier research (why streaming, which providers).
- Build: `powershell -ExecutionPolicy Bypass -File build.ps1` → `dist\STT.exe`. The script stops the running `dist\STT.exe` first; start it again after the build.
- Test the API connection: `dist\STT.exe --test` → result in `%APPDATA%\STT\stt.log`.
- Soniox API key is in Bitwarden, folder `AI Providers`, item `Soniox`. Use `bwprx-client run ... -- STT.exe --save-key`, never print the key.
- Desktop app rules apply: single instance, centered windows, tray menu with Exit last, clean close on WM_CLOSE (hidden `STT host` window).
- `reference-docs/` is for the user's own files. All `.md` files stay in the root.
