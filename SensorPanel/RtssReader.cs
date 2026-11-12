using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;

namespace SensorPanel
{
    /// <summary>
    /// Reads the frame rate of the foreground application from RivaTuner Statistics Server shared memory
    /// (the same source AIDA64 uses). Returns null when RTSS is not running.
    /// </summary>
    public sealed class RtssReader : IDisposable
    {
        const string MapName = "RTSSSharedMemoryV2";
        const uint Signature = 0x52545353; // 'RTSS'

        // Offsets inside RTSS_SHARED_MEMORY_APP_ENTRY
        const int OffTime0 = 268;
        const int OffTime1 = 272;
        const int OffFrames = 276;

        MemoryMappedFile? _map;
        MemoryMappedViewAccessor? _view;

        public double? ReadForegroundFps()
        {
            try
            {
                if (_view == null)
                {
                    _map = MemoryMappedFile.OpenExisting(MapName, MemoryMappedFileRights.Read);
                    _view = _map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
                }

                if (_view.ReadUInt32(0) != Signature)
                    return null;
                uint entrySize = _view.ReadUInt32(8);
                uint arrOffset = _view.ReadUInt32(12);
                uint arrSize = _view.ReadUInt32(16);

                GetWindowThreadProcessId(GetForegroundWindow(), out uint fgPid);

                double? best = null;
                for (uint i = 0; i < arrSize; i++)
                {
                    long entry = arrOffset + i * entrySize;
                    uint pid = _view.ReadUInt32(entry);
                    if (pid == 0)
                        continue;
                    uint t0 = _view.ReadUInt32(entry + OffTime0);
                    uint t1 = _view.ReadUInt32(entry + OffTime1);
                    uint frames = _view.ReadUInt32(entry + OffFrames);
                    if (t1 <= t0)
                        continue;
                    double fps = frames * 1000.0 / (t1 - t0);
                    if (pid == fgPid)
                        return fps;
                    best = Math.Max(best ?? 0, fps);
                }
                return best ?? 0;
            }
            catch
            {
                // RTSS closed or not installed: retry opening next time.
                Dispose();
                return null;
            }
        }

        public void Dispose()
        {
            _view?.Dispose();
            _map?.Dispose();
            _view = null;
            _map = null;
        }

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    }
}
