using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace STT
{
    [DataContract]
    sealed class Settings
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "STT");
        static readonly string FilePath = Path.Combine(Dir, "settings.json");

        [DataMember] public int HotkeyModifiers;
        [DataMember] public int HotkeyKey;
        // The API key, encrypted with Windows DPAPI (only this Windows user can read it).
        [DataMember] public string ApiKeyProtected;
        [DataMember] public string Model;
        [DataMember] public string LanguageHints;
        [DataMember] public string Terms;
        [DataMember] public string Microphone;
        [DataMember] public bool TypePartial;
        [DataMember] public int AutoStopSeconds;
        // A pause this long ends the sentence (the app sends "finalize"). 0 = Soniox decides (endpoint detection).
        [DataMember] public double SentencePauseSeconds;
        [DataMember] public bool PlaySounds;

        public Settings() { SetDefaults(); }

        [OnDeserializing]
        void OnDeserializing(StreamingContext c) { SetDefaults(); }

        void SetDefaults()
        {
            HotkeyModifiers = Native.MOD_CONTROL | Native.MOD_SHIFT;
            HotkeyKey = (int)Keys.Space;
            ApiKeyProtected = "";
            Model = "stt-rt-v5";
            LanguageHints = "en, fa";
            Terms = "";
            Microphone = "";
            TypePartial = true;
            AutoStopSeconds = 30;
            SentencePauseSeconds = 6;
            PlaySounds = true;
        }

        static readonly DataContractJsonSerializer Serializer = new DataContractJsonSerializer(typeof(Settings));

        public static Settings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    using (var f = File.OpenRead(FilePath))
                        return (Settings)Serializer.ReadObject(f);
            }
            catch (Exception ex)
            {
                Log.Write("settings load failed: " + ex.Message);
            }
            return new Settings();
        }

        public void Save()
        {
            Directory.CreateDirectory(Dir);
            using (var f = File.Create(FilePath))
                Serializer.WriteObject(f, this);
        }

        /// <summary>Saved key, or the SONIOX_API_KEY environment variable.</summary>
        public string GetApiKey()
        {
            if (!string.IsNullOrEmpty(ApiKeyProtected))
            {
                try
                {
                    var data = ProtectedData.Unprotect(Convert.FromBase64String(ApiKeyProtected), null, DataProtectionScope.CurrentUser);
                    return Encoding.UTF8.GetString(data);
                }
                catch (Exception ex)
                {
                    Log.Write("api key decrypt failed: " + ex.Message);
                }
            }
            return Environment.GetEnvironmentVariable("SONIOX_API_KEY") ?? "";
        }

        public void SetApiKey(string key)
        {
            key = (key ?? "").Trim();
            ApiKeyProtected = key.Length == 0 ? "" :
                Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser));
        }

        public SonioxConfig ToSonioxConfig()
        {
            return new SonioxConfig
            {
                Model = string.IsNullOrWhiteSpace(Model) ? "stt-rt-v5" : Model.Trim(),
                LanguageHints = SplitList(LanguageHints),
                Terms = SplitList(Terms),
                EnableEndpointDetection = SentencePauseSeconds <= 0,
            };
        }

        public int SentencePauseMs => SentencePauseSeconds <= 0 ? 0 : (int)(SentencePauseSeconds * 1000);

        static string[] SplitList(string s)
        {
            return (s ?? "").Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        }

        public static string HotkeyText(int mods, int key)
        {
            if (key == 0 && mods == 0) return "(none)";
            var sb = new StringBuilder();
            if ((mods & Native.MOD_CONTROL) != 0) sb.Append("Ctrl+");
            if ((mods & Native.MOD_ALT) != 0) sb.Append("Alt+");
            if ((mods & Native.MOD_SHIFT) != 0) sb.Append("Shift+");
            if ((mods & Native.MOD_WIN) != 0) sb.Append("Win+");
            return key == 0 ? sb.ToString() : sb.Append(KeyName((Keys)key)).ToString();
        }

        static string KeyName(Keys k)
        {
            if (k >= Keys.D0 && k <= Keys.D9) return ((char)('0' + (k - Keys.D0))).ToString();
            switch (k)
            {
                case Keys.Oemtilde: return "`";
                case Keys.OemMinus: return "-";
                case Keys.Oemplus: return "=";
                case Keys.OemOpenBrackets: return "[";
                case Keys.Oem6: return "]";
                case Keys.Oem5: return "\\";
                case Keys.Oem1: return ";";
                case Keys.Oem7: return "'";
                case Keys.Oemcomma: return ",";
                case Keys.OemPeriod: return ".";
                case Keys.OemQuestion: return "/";
                default: return k.ToString();
            }
        }
    }

    static class Log
    {
        static readonly string FilePath = Path.Combine(Settings.Dir, "stt.log");
        static readonly object Lock = new object();

        public static void Write(string message)
        {
            lock (Lock)
            {
                try
                {
                    Directory.CreateDirectory(Settings.Dir);
                    var info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > 1024 * 1024) File.Delete(FilePath);
                    File.AppendAllText(FilePath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine);
                }
                catch { }
            }
        }
    }
}
