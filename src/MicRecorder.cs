// Microphone capture with the old winmm waveIn API: 16 kHz, mono, 16-bit PCM.
// No extra DLLs needed. Data is raised every 100 ms on a background thread.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace STT
{
    public sealed class MicRecorder : IDisposable
    {
        public const int SampleRate = 16000;
        const int BufferCount = 4;
        const int BufferBytes = SampleRate * 2 / 10; // 100 ms
        const uint WaveMapper = unchecked((uint)-1);
        const int CALLBACK_EVENT = 0x50000;
        const int WHDR_DONE = 1;

        public event Action<byte[]> Data;

        IntPtr handle;
        readonly IntPtr[] headers = new IntPtr[BufferCount];
        readonly EventWaitHandle signal = new EventWaitHandle(false, EventResetMode.AutoReset);
        Thread thread;
        volatile bool running;

        static readonly int HeaderSize = Marshal.SizeOf(typeof(WAVEHDR));
        static readonly int OffBytesRecorded = (int)Marshal.OffsetOf(typeof(WAVEHDR), "dwBytesRecorded");
        static readonly int OffFlags = (int)Marshal.OffsetOf(typeof(WAVEHDR), "dwFlags");

        /// <summary>Names of the input devices. Index = device id. Names are cut to 31 characters by Windows.</summary>
        public static List<string> ListDevices()
        {
            var list = new List<string>();
            int n = waveInGetNumDevs();
            for (int i = 0; i < n; i++)
            {
                var caps = new WAVEINCAPS();
                list.Add(waveInGetDevCapsW((UIntPtr)i, ref caps, Marshal.SizeOf(caps)) == 0 ? caps.szPname : "Device " + i);
            }
            return list;
        }

        /// <summary>Device id for a name from ListDevices, or -1 (Windows default) if not found.</summary>
        public static int FindDevice(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            return ListDevices().IndexOf(name);
        }

        public void Start(int deviceId)
        {
            var fmt = new WAVEFORMATEX
            {
                wFormatTag = 1,
                nChannels = 1,
                nSamplesPerSec = SampleRate,
                nAvgBytesPerSec = SampleRate * 2,
                nBlockAlign = 2,
                wBitsPerSample = 16,
            };
            uint dev = deviceId < 0 ? WaveMapper : (uint)deviceId;
            int err = waveInOpen(out handle, dev, ref fmt, signal.SafeWaitHandle.DangerousGetHandle(), IntPtr.Zero, CALLBACK_EVENT);
            if (err != 0) throw new InvalidOperationException("Cannot open the microphone (waveInOpen error " + err + ").");

            for (int i = 0; i < BufferCount; i++)
            {
                var hdr = new WAVEHDR { lpData = Marshal.AllocHGlobal(BufferBytes), dwBufferLength = BufferBytes };
                headers[i] = Marshal.AllocHGlobal(HeaderSize);
                Marshal.StructureToPtr(hdr, headers[i], false);
                waveInPrepareHeader(handle, headers[i], HeaderSize);
                waveInAddBuffer(handle, headers[i], HeaderSize);
            }

            running = true;
            thread = new Thread(Loop) { IsBackground = true, Priority = ThreadPriority.AboveNormal, Name = "mic" };
            thread.Start();
            err = waveInStart(handle);
            if (err != 0)
            {
                Stop();
                throw new InvalidOperationException("Cannot start the microphone (waveInStart error " + err + ").");
            }
        }

        void Loop()
        {
            int next = 0;
            while (running)
            {
                signal.WaitOne(200);
                // The driver fills the buffers in order.
                while (running && (Marshal.ReadInt32(headers[next], OffFlags) & WHDR_DONE) != 0)
                {
                    Emit(headers[next]);
                    Requeue(headers[next]);
                    next = (next + 1) % BufferCount;
                }
            }
        }

        void Emit(IntPtr hdr)
        {
            int n = Marshal.ReadInt32(hdr, OffBytesRecorded);
            if (n <= 0) return;
            var data = new byte[n];
            Marshal.Copy(Marshal.ReadIntPtr(hdr), data, 0, n); // lpData is the first field
            Data?.Invoke(data);
        }

        void Requeue(IntPtr hdr)
        {
            waveInUnprepareHeader(handle, hdr, HeaderSize);
            Marshal.WriteInt32(hdr, OffBytesRecorded, 0);
            Marshal.WriteInt32(hdr, OffFlags, 0);
            waveInPrepareHeader(handle, hdr, HeaderSize);
            waveInAddBuffer(handle, hdr, HeaderSize);
        }

        public void Stop()
        {
            if (handle == IntPtr.Zero) return;
            running = false;
            signal.Set();
            thread?.Join(2000);
            waveInReset(handle); // marks all buffers done
            for (int i = 0; i < BufferCount; i++)
            {
                if (headers[i] == IntPtr.Zero) continue;
                waveInUnprepareHeader(handle, headers[i], HeaderSize);
                Marshal.FreeHGlobal(Marshal.ReadIntPtr(headers[i]));
                Marshal.FreeHGlobal(headers[i]);
                headers[i] = IntPtr.Zero;
            }
            waveInClose(handle);
            handle = IntPtr.Zero;
        }

        public void Dispose()
        {
            Stop();
            signal.Dispose();
        }

        [StructLayout(LayoutKind.Sequential, Pack = 2)]
        struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public int nSamplesPerSec;
            public int nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct WAVEHDR
        {
            public IntPtr lpData;
            public int dwBufferLength;
            public int dwBytesRecorded;
            public IntPtr dwUser;
            public int dwFlags;
            public int dwLoops;
            public IntPtr lpNext;
            public IntPtr reserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct WAVEINCAPS
        {
            public ushort wMid;
            public ushort wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public uint dwFormats;
            public ushort wChannels;
            public ushort wReserved1;
        }

        [DllImport("winmm.dll")] static extern int waveInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode)] static extern int waveInGetDevCapsW(UIntPtr id, ref WAVEINCAPS caps, int size);
        [DllImport("winmm.dll")] static extern int waveInOpen(out IntPtr h, uint id, ref WAVEFORMATEX fmt, IntPtr callback, IntPtr instance, int flags);
        [DllImport("winmm.dll")] static extern int waveInPrepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInUnprepareHeader(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInAddBuffer(IntPtr h, IntPtr hdr, int size);
        [DllImport("winmm.dll")] static extern int waveInStart(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInReset(IntPtr h);
        [DllImport("winmm.dll")] static extern int waveInClose(IntPtr h);
    }
}
