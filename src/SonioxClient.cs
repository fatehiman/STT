// Soniox real-time (streaming) speech-to-text client.
// No UI code here, so this file can be copied into other projects.
// API docs: https://soniox.com/docs/stt/api-reference/websocket-api
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace STT
{
    public sealed class SonioxConfig
    {
        public string Model = "stt-rt-v5";
        // "pcm_s16le" for raw microphone audio, or "auto" for files (wav, mp3, ...).
        public string AudioFormat = "pcm_s16le";
        public int SampleRate = 16000;
        public int NumChannels = 1;
        public string[] LanguageHints = { "en", "fa" };
        public bool EnableSpeakerDiarization;
        public bool EnableLanguageIdentification;
        public bool EnableEndpointDetection = true;
        // Words the model should know (names, technical terms). Sent as context.terms.
        public string[] Terms;
        // Free text that describes the topic. Sent as context.general.
        public string ContextText;

        public string ToJson()
        {
            var sb = new StringBuilder("{");
            Prop(sb, "model", Model);
            Prop(sb, "audio_format", AudioFormat);
            if (AudioFormat != "auto")
            {
                sb.Append(",\"sample_rate\":").Append(SampleRate);
                sb.Append(",\"num_channels\":").Append(NumChannels);
            }
            if (LanguageHints != null && LanguageHints.Length > 0)
                sb.Append(",\"language_hints\":").Append(Array(LanguageHints));
            sb.Append(",\"enable_speaker_diarization\":").Append(EnableSpeakerDiarization ? "true" : "false");
            sb.Append(",\"enable_language_identification\":").Append(EnableLanguageIdentification ? "true" : "false");
            sb.Append(",\"enable_endpoint_detection\":").Append(EnableEndpointDetection ? "true" : "false");
            bool hasTerms = Terms != null && Terms.Length > 0;
            if (hasTerms || !string.IsNullOrEmpty(ContextText))
            {
                sb.Append(",\"context\":{");
                if (hasTerms) sb.Append("\"terms\":").Append(Array(Terms));
                if (!string.IsNullOrEmpty(ContextText))
                    sb.Append(hasTerms ? "," : "").Append("\"general\":[{\"key\":\"topic\",\"value\":").Append(Str(ContextText)).Append("}]");
                sb.Append('}');
            }
            return sb.Append('}').ToString();
        }

        static void Prop(StringBuilder sb, string name, string value)
        {
            if (sb.Length > 1) sb.Append(',');
            sb.Append('"').Append(name).Append("\":").Append(Str(value));
        }

        static string Array(IEnumerable<string> items)
        {
            var parts = new List<string>();
            foreach (var s in items) parts.Add(Str(s));
            return "[" + string.Join(",", parts) + "]";
        }

        internal static string Str(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.Append('"').ToString();
        }
    }

    [DataContract]
    public sealed class SonioxToken
    {
        [DataMember(Name = "text")] public string Text;
        [DataMember(Name = "start_ms")] public long StartMs;
        [DataMember(Name = "end_ms")] public long EndMs;
        [DataMember(Name = "confidence")] public double Confidence;
        [DataMember(Name = "is_final")] public bool IsFinal;
        [DataMember(Name = "speaker")] public string Speaker;
        [DataMember(Name = "language")] public string Language;

        // "<end>" (endpoint detected) and "<fin>" (manual finalize done) are control tokens, not words.
        public bool IsControl => Text == "<end>" || Text == "<fin>";
    }

    [DataContract]
    public sealed class SonioxResponse
    {
        [DataMember(Name = "tokens")] public SonioxToken[] Tokens;
        [DataMember(Name = "final_audio_proc_ms")] public long FinalAudioProcMs;
        [DataMember(Name = "total_audio_proc_ms")] public long TotalAudioProcMs;
        [DataMember(Name = "finished")] public bool Finished;
        [DataMember(Name = "error_code")] public int ErrorCode;
        [DataMember(Name = "error_type")] public string ErrorType;
        [DataMember(Name = "error_message")] public string ErrorMessage;
    }

    /// <summary>
    /// One streaming session. Usage: ConnectAsync, then SendAudio many times, then EndOfAudio.
    /// Events are raised on a thread-pool thread. Exactly one of Finished / Failed is raised.
    /// Billing note: Soniox bills the whole stream time (also silence), so end the session when idle.
    /// </summary>
    public sealed class SonioxClient : IDisposable
    {
        public const string Endpoint = "wss://stt-rt.soniox.com/transcribe-websocket";

        static readonly DataContractJsonSerializer Parser = new DataContractJsonSerializer(typeof(SonioxResponse));
        static readonly byte[] KeepAlive = Encoding.UTF8.GetBytes("{\"type\":\"keepalive\"}");
        static readonly byte[] FinalizeMsg = Encoding.UTF8.GetBytes("{\"type\":\"finalize\"}");

        readonly ClientWebSocket ws = new ClientWebSocket();
        readonly BlockingCollection<Outgoing> queue = new BlockingCollection<Outgoing>();
        readonly CancellationTokenSource cts = new CancellationTokenSource();
        int done; // 1 after Finished or Failed was raised

        public event Action<SonioxResponse> Response;
        public event Action Finished;
        public event Action<string> Failed;

        sealed class Outgoing
        {
            public byte[] Data;
            public bool Text;
            public bool End;
        }

        /// <summary>Opens the WebSocket and sends the config. Audio sent before this ends is queued.</summary>
        public async Task ConnectAsync(string apiKey, SonioxConfig config)
        {
            ws.Options.SetRequestHeader("Authorization", "Bearer " + apiKey);
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            await ws.ConnectAsync(new Uri(Endpoint), cts.Token).ConfigureAwait(false);
            var cfg = Encoding.UTF8.GetBytes(config.ToJson());
            await ws.SendAsync(new ArraySegment<byte>(cfg), WebSocketMessageType.Text, true, cts.Token).ConfigureAwait(false);
            _ = Task.Run(SendLoop);
            _ = Task.Run(ReceiveLoop);
        }

        /// <summary>Queues raw audio (in the format given in the config).</summary>
        public void SendAudio(byte[] data)
        {
            Enqueue(new Outgoing { Data = data });
        }

        /// <summary>Asks the server to make all pending tokens final now (returns a "&lt;fin&gt;" token).</summary>
        public void FinalizeNow()
        {
            Enqueue(new Outgoing { Data = FinalizeMsg, Text = true });
        }

        /// <summary>No more audio. The server sends the last tokens, then a "finished" response.</summary>
        public void EndOfAudio()
        {
            Enqueue(new Outgoing { Data = new byte[0], Text = true, End = true });
        }

        void Enqueue(Outgoing item)
        {
            try { queue.Add(item); }
            catch (InvalidOperationException) { } // already ended
        }

        async Task SendLoop()
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    // Soniox closes the connection after 40 s with no message, so send keepalives when idle.
                    if (!queue.TryTake(out var item, 10000, cts.Token))
                        item = new Outgoing { Data = KeepAlive, Text = true };
                    var type = item.Text ? WebSocketMessageType.Text : WebSocketMessageType.Binary;
                    await ws.SendAsync(new ArraySegment<byte>(item.Data), type, true, cts.Token).ConfigureAwait(false);
                    if (item.End)
                    {
                        queue.CompleteAdding();
                        return;
                    }
                }
            }
            catch (Exception ex) when (!cts.IsCancellationRequested)
            {
                Fail("send failed: " + ex.Message);
            }
            catch { }
        }

        async Task ReceiveLoop()
        {
            var buffer = new byte[16384];
            var message = new MemoryStream();
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    message.SetLength(0);
                    WebSocketReceiveResult r;
                    do
                    {
                        r = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token).ConfigureAwait(false);
                        if (r.MessageType == WebSocketMessageType.Close)
                        {
                            Fail("connection closed by server (" + ws.CloseStatus + " " + ws.CloseStatusDescription + ")");
                            return;
                        }
                        message.Write(buffer, 0, r.Count);
                    } while (!r.EndOfMessage);

                    message.Position = 0;
                    var resp = (SonioxResponse)Parser.ReadObject(message);
                    if (resp.ErrorCode != 0 || !string.IsNullOrEmpty(resp.ErrorType))
                    {
                        Fail(resp.ErrorCode + " " + resp.ErrorType + ": " + resp.ErrorMessage);
                        return;
                    }
                    Response?.Invoke(resp);
                    if (resp.Finished)
                    {
                        if (Interlocked.Exchange(ref done, 1) == 0) Finished?.Invoke();
                        return;
                    }
                }
            }
            catch (Exception ex) when (!cts.IsCancellationRequested)
            {
                Fail("receive failed: " + ex.Message);
            }
            catch { }
        }

        void Fail(string message)
        {
            if (Interlocked.Exchange(ref done, 1) == 0) Failed?.Invoke(message);
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref done, 1);
            cts.Cancel();
            try { queue.CompleteAdding(); } catch { }
            try { ws.Abort(); } catch { }
            ws.Dispose();
        }
    }
}
