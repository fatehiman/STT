# Fast Speech-to-Text (STT) on Windows — Research

- **Date:** 2026-09-30
- **Goal:** find something like Android **GBoard voice typing**: words appear in any window **while** I speak, with no long wait at the end.
- **Language:** English first. Later (2026-10-24): Persian is needed too.
- **Machine:** Windows 11 Home, NVIDIA **RTX 4060 (8 GB)**.
- **Status (2026-10-24):** decision made — **Soniox** real-time API. Our own tray app is built in this repo (see `README.md` and section 6 below).

---

## 0. Starting point — why the current app is slow

### Current app: WhisperWriter
- Folder: `E:\appServices\WhisperWriter` (our fork: `github.com/fatehiman/whisper-writer`).
- Model: faster-whisper **large-v3**, GPU, float16.
- How it works: record → wait for 900 ms of silence → transcribe the whole clip → type it.
- Full doc: `E:\www\myLan\tinyApps\whisperWriter.md`.
- **Auto-start was disabled on 2026-09-30.** It was the line
  `00:01:40,E:\appServices\cmnBatchFiles\runWhsiperWrite.bat,2` in
  `E:\appServices\DelayedTaskRunnerHome\DelayedTaskRunner.txt`. It is now commented out with `#`.
  Remove the `#` to turn it on again.

### Why Whisper is always "listen, think, then type"
- Whisper is an **encoder-decoder** model trained on fixed **30-second** audio windows.
- The encoder must see the **whole** audio before the decoder writes any text.
- So Whisper is a **batch** model: it cannot give words while you are still talking.
- "Live Whisper" tools only run Whisper again and again on a growing audio buffer. That is slow, uses a lot of GPU, and earlier words can still change.

### Why GBoard is fast
- GBoard uses an on-device **RNN-T** (a "streaming transducer" model).
- The model only looks at past audio, plus a very small look-ahead.
- It gives text every audio frame, about every **40–80 ms**. So words appear while you speak, and they rarely change later.
- Models that work the same way: **Nemotron Speech Streaming, Parakeet-unified, Moonshine streaming, sherpa-onnx Zipformer**.

**Conclusion:** to get GBoard-like speed we need a **streaming model** (local or API), not a faster Whisper.

---

## 1. Ready-to-use Windows apps

Important: many "live" apps only show a live **preview** in their own small window. The final text is then pasted at the end. Only a few type words into the target window while you speak.

| App | Price | Local / cloud | Real streaming? | Notes |
|---|---|---|---|---|
| **Win+H Voice Typing** (built into Windows) | Free | Cloud (Azure Speech), no offline mode | Yes, words come in chunks, about 1–3 s behind | Stops after a few seconds of silence. Weak on technical words. [voxbooster](https://voxbooster.com/blog/voice-typing-windows-11/), [yaps](https://www.yaps.ai/blog/windows-voice-typing-keeps-stopping) |
| **Voice Access** (built into Windows) | Free | On-device after a one-time language download | Phrase by phrase (not checked) | Built for accessibility and voice commands. Offline English (US) is supported. "Fluid dictation" (small on-device language model) works only on Copilot+ PCs. [weesper](https://weesperneonflow.ai/en/blog/2026-06-30-voice-typing-vs-voice-access-windows-11-guide/) |
| **Aqua Voice** | Free for 1,000 words; Pro $8/mo; **Realtime mode needs Max, $24/mo** (billed yearly) | Cloud | **Yes, words show as you speak** | [aquavoice.com/realtime](https://aquavoice.com/realtime), [pricing](https://usevoicy.com/blog/aqua-voice-pricing) |
| **Wispr Flow** | Free 2,000 words/week; Pro $12–15/mo | Cloud | No clear streaming. It works like hold → speak → release → polished text (not checked) | [wisprflow.ai/pricing](https://wisprflow.ai/pricing) |
| **Superwhisper** | Free tier; $8.49/mo; $249.99 lifetime | Local + cloud | Batch | Windows app exists since 2026 but is less polished than the Mac app. [changelog](https://superwhisper.com/changelog?platform=windows) |
| **Voicetypr** | $39 lifetime, 3-day trial | Local (Whisper, Parakeet, Nemotron) or cloud (Soniox, Deepgram) | Live **preview** only; the final text is pasted at the end | [voicetypr.com](https://voicetypr.com/voicetyper) |
| **OpenWhispr** | Free, open source | Local (Nemotron streaming on CPU) or your own API key | Live **preview** (under 1 s behind), then pastes the final text | Streaming added in v1.7.6 (July 2026). [blog](https://openwhispr.com/blog/local-streaming-speech-to-text) |
| **Handy** | Free, open source | Local (Whisper, Parakeet, Moonshine, Nemotron streaming) | Live overlay, then paste | Streaming bugs are open now, for example Nemotron 3.5 stops giving text after the first chunk. [#2125](https://github.com/cjpais/Handy/issues/2125), [#1835](https://github.com/cjpais/Handy/issues/1835) |
| **Spokenly** | Free with local models or your own API key | Local or cloud (Deepgram, Soniox, ElevenLabs) | Says it has "real-time streaming" (did not check if it types live) | [spokenly.app/windows](https://spokenly.app/windows) |
| **Syrinx** | Free, open source | Local CUDA server (Nemotron streaming 0.6B) + Windows tray app | **Yes. Types words at the cursor as they arrive.** | **Closest to GBoard.** Needs a CUDA 12.8 server (container) and a 2.4 GB model download, so it is harder to install. [github](https://github.com/coleman2246/syrinx) |
| **Talon** | Free | Local (Conformer model) | Types each phrase after a short pause | Mainly for voice commands and coding. [talon.wiki](https://talon.wiki/Resource%20Hub/Speech%20Recognition/speech%20engines/) |
| **Dragon Professional 16** | $699 one-time | Local | Yes, true streaming | Old but still sold. Expensive. [getvoibe](https://www.getvoibe.com/resources/dragon-pricing/) |

Apps not researched this time (general knowledge only, check before you rely on them):
- **Buzz**: Whisper-based. Writes into its own window, does not type into other apps.
- **WhisperLive / WhisperLiveKit**: servers or libraries, not dictation apps.
- **nerd-dictation**: Linux only (Vosk + xdotool).
- **Google Docs voice typing**: streams, but only inside Google Docs in Chrome.
- **Speechpad**: uses Chrome's Web Speech API. Could not confirm a current tool that types into any window.

---

## 2. Streaming STT APIs

These take a **live audio stream** (usually over a WebSocket), not a WAV file, and send back partial text while you talk.

| Provider | Streaming price | Free | Latency / notes |
|---|---|---|---|
| **Deepgram** Nova-3 (English) | $0.29/h promo ($0.0048/min; normal $0.46/h). Flux English: $0.39/h | **$200 one-time credit** (about 690 h) | Low latency (no official number) |
| **AssemblyAI** Universal-Streaming English | **$0.15/h**. Universal-3.6 Pro Realtime: $0.45/h | $50 credit (about 333 h) | Billed by **open session time**, not audio time (close the socket when idle) |
| **Soniox** | **$0.12/h** (cheapest) | Free credit not confirmed | Under 200 ms, token by token |
| **Cartesia** Ink-Whisper | about $0.13/h (Scale plan) | not checked | Built for voice agents |
| **ElevenLabs** Scribe v2 Realtime | $0.39/h | — | 150 ms (independent test: 130 ms) |
| **Speechmatics** | about $0.40/h | **8 h/month free** | — |
| **Gladia** | $0.75/h | **10 h/month free** | — |
| **AWS Transcribe** streaming | about $0.60/h (third-party figure, not checked) | New accounts since July 2025: $200 general credit (older accounts had 60 min/month) | — |
| **Azure Speech** | $1.00/h | **5 h/month free, every month (F0 tier)** | Same engine as Win+H |
| **Google Cloud STT** | about $0.96/h ($0.016/min, not confirmed on the official page) | 60 min/month + $300 trial credit | — |
| **OpenAI** Realtime transcription | about $1.02/h ($0.017/min, not confirmed) | — | gpt-4o-transcribe (non-realtime) is $0.006/min |
| **Rev.ai** | Reverb from $0.20/h (not confirmed this is the streaming price) | — | — |
| **Groq** | Whisper turbo $0.04/h | — | **Batch only** (file upload). No live results. |

Sources: [deepgram.com/pricing](https://deepgram.com/pricing), [assemblyai.com/pricing](https://www.assemblyai.com/pricing), [soniox.com/pricing](https://soniox.com/pricing), [elevenlabs realtime](https://elevenlabs.io/realtime-speech-to-text), [speechmatics](https://www.speechmatics.com/pricing), [gladia](https://www.gladia.io/pricing), [Azure quotas](https://learn.microsoft.com/en-us/azure/ai-services/speech-service/speech-services-quotas-and-limits), [Google](https://cloud.google.com/speech-to-text/pricing), [AWS](https://costgoat.com/pricing/amazon-transcribe), [Cartesia](https://www.cartesia.ai/ink), [Rev](https://www.rev.ai/pricing), [Groq](https://console.groq.com/docs/model/whisper-large-v3-turbo).

**How much does dictation really cost?** One hour of dictation per day for a month ≈ 30 h:
- Soniox ≈ $3.60, AssemblyAI ≈ $4.50, Deepgram ≈ $8.70 (after the free credit is used).

**Network note:** cloud APIs need a working route to the provider from this LAN (VPN / gateway setup). Test the WebSocket connection before choosing one.

---

## 3. Local streaming models (all fit on an RTX 4060, 8 GB)

- **NVIDIA Nemotron Speech Streaming EN 0.6B** (January 2026) — **best local choice**
  - Cache-aware FastConformer-RNNT: it reuses earlier audio work instead of computing it again.
  - Chunk size vs average error rate (WER):

    | Chunk | WER |
    |---|---|
    | 80 ms | 8.43% |
    | 160 ms | 7.67% |
    | 560 ms | 7.07% |
    | 1.12 s | 6.93% |

  - Adds punctuation and capital letters itself.
  - Officially Linux + NeMo. On Windows it runs through ONNX INT8 (OpenWhispr, Handy) or NeMo-Speech.cpp.
  - Nemotron 3.5 (June 2026) is the multilingual version.
  - [HF card](https://huggingface.co/nvidia/nemotron-speech-streaming-en-0.6b)
- **Parakeet-unified-en-0.6b** (April 2026)
  - One model for both batch and streaming. Minimum delay 160 ms.
  - Beats Nemotron down to about 240 ms. No cache-aware mode yet.
  - [HF](https://huggingface.co/nvidia/parakeet-unified-en-0.6b)
- **Parakeet TDT 0.6B v2/v3**
  - Batch only, but very fast and very accurate (v3 about 6.34% WER).
  - Good for a **final correction pass** after streaming (stream for speed, then fix the sentence).
- **Moonshine v2 streaming** (February 2026)
  - English WER: Medium 6.65%, Small 7.84%, Tiny 12%.
  - Latency 50–258 ms on an Apple M3. Very light, can run on CPU.
  - [arXiv](https://arxiv.org/html/2602.12241v1)
- **Kyutai STT 1B** (English/French)
  - Fixed 0.5 s delay. Has semantic VAD (knows when you finished a sentence).
  - Rust WebSocket server available. No Windows dictation app.
  - [github](https://github.com/kyutai-labs/delayed-streams-modeling/)
- **sherpa-onnx streaming Zipformer**
  - Small, runs on CPU, ready for Windows. Gives partial results.
  - Less accurate than Nemotron. You would have to build the app yourself.
  - [docs](https://k2-fsa.github.io/sherpa/onnx/pretrained_models/online-transducer/zipformer-transducer-models.html)
- **Vosk**: old Kaldi-based toolkit, weak accuracy.
- **Whisper-based streaming** (SimulStreaming / WhisperLiveKit, whisper_streaming)
  - Workarounds that run Whisper again and again on a growing buffer.
  - whisper_streaming delay is about 3.3 s. SimulStreaming (2025, same author) is faster and better.
  - [SimulStreaming](https://github.com/ufal/SimulStreaming), [WhisperLiveKit](https://github.com/QuentinFuxa/WhisperLiveKit)
- **whisper-large-v3-turbo / Distil-Whisper**
  - Much faster than large-v3, but still batch.
  - **Quick fix for the current WhisperWriter:** change `large-v3` → `large-v3-turbo` in `config.yaml`. Several times faster, but it still waits until you stop talking.

---

## 4. Recommendations

### Best ready-to-use app
1. **Local and free:** **OpenWhispr** + Nemotron streaming. Easy to install. Live preview, final text pasted at the end.
2. **Most GBoard-like, local and free:** **Syrinx**. Types at the cursor while you talk. Harder to install (CUDA server).
3. **Cloud, paid, live typing:** **Aqua Voice Max** ($24/mo).
4. **Free, zero install:** **Win+H**. Cloud, a bit slow, stops on pauses.

### Best free / cheap API
1. **Deepgram Nova-3**: $200 credit (≈690 h), then $0.29/h.
2. **AssemblyAI**: $0.15/h, $50 credit.
3. **Soniox**: $0.12/h, cheapest paid option.
4. **Azure Speech**: 5 h/month free forever.
- These can be used without coding through **Spokenly** or **Voicetypr** (bring your own API key).

### Best local option
- **Nemotron Speech Streaming EN 0.6B** on the RTX 4060.
- Easiest: OpenWhispr or Handy (Handy's streaming is buggy now).
- Most GBoard-like: Syrinx.

### If we build our own tool (future project idea)
- Tray app + global hotkey (reuse ideas from WhisperWriter: hotkey suppression, flash cue, keyboard-layout language pick).
- Mic → 16 kHz PCM chunks (80–160 ms) → streaming engine → type new words at the cursor as they arrive.
- Engine options: Nemotron streaming (local, GPU) **or** Deepgram / AssemblyAI / Soniox WebSocket (cloud).
- Hard part: when a partial word changes, the app must delete and retype it (backspace + new text). Stable RNN-T models change words rarely, so this is small.
- Optional: run Parakeet TDT on the full sentence at the end to fix small errors.

---

## 5. Not confirmed — check before deciding

- Wispr Flow: does it stream or not?
- Win+H and Voice Access: do they show word-by-word partial text? (Sources give only lag figures.)
- Soniox and Cartesia free credits.
- Google and OpenAI Realtime prices (from third-party sites, not official pages).
- AWS streaming price.
- Spokenly: does it type live into the window, or only preview?
- Whether each cloud API is reachable from this LAN / through the VPN gateway.

---

## 6. Decision (2026-10-24): Soniox

Chosen: **Soniox real-time API** (`stt-rt-v5`).

Why:
- Cheapest streaming API: **$0.12/h**. Speaker diarization, language identification and formatting are included in this price.
- Real streaming: tokens come while you speak (non-final first, then final).
- **Persian (`fa`) is supported** in real-time, and many languages can be mixed in one stream (language hints `en, fa`).
- **Speaker diarization** (who is speaking) — useful for meeting projects later.
- Custom words (`context.terms`) for names and technical words.
- Good accuracy on the user's own voice (user's own test).

Checked on 2026-10-24 (soniox.com docs):
- WebSocket: `wss://stt-rt.soniox.com/transcribe-websocket`. API key in the `Authorization: Bearer` header (the `api_key` field in the config JSON is deprecated and refused from 2027-01-15).
- Billing counts the **whole stream time, also silence**. So the app stops the stream when idle (auto stop).
- Send a keepalive at least every 40 s when no audio is sent. One stream can be up to 300 minutes.
- No free credit for new accounts is shown on the pricing page. The key in Bitwarden connects fine but returns `402 organization_balance_exhausted` until credit is added.

Other options and why not:
- **Deepgram Nova-3**: $200 free credit, but more expensive after that ($0.29/h). Persian streaming support not checked.
- **ElevenLabs Scribe v2 Realtime**: has diarization, but $0.39/h (3x Soniox).
- **Local Nemotron streaming**: free and private, but the tested model is English only. Persian support in the multilingual Nemotron 3.5 is not confirmed. Needs the GPU.

The app: `README.md` in this folder. The reusable client is `src/SonioxClient.cs`.
