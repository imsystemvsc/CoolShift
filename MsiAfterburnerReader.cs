using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Text;

namespace ParkToggleWpf;

public static class MsiAfterburnerReader
{
    private const string MapFileName = "MAHMSharedMemory";
    private const uint Signature = 0x4D41484D; // 'M' 'A' 'H' 'M'

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct MAHM_SHARED_MEMORY_HEADER
    {
        public uint dwSignature;
        public uint dwVersion;
        public uint dwHeaderSize;
        public uint dwNumEntries;
        public uint dwEntrySize;
        public uint time;
    }

    public static double? GetGpuVoltage()
    {
        try
        {
            using var mmf = MemoryMappedFile.OpenExisting(MapFileName, MemoryMappedFileRights.Read);
            using var accessor = mmf.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);

            accessor.Read(0, out MAHM_SHARED_MEMORY_HEADER header);
            if (header.dwSignature != Signature || header.dwNumEntries == 0 || header.dwEntrySize == 0)
            {
                return null;
            }

            long offset = header.dwHeaderSize;
            for (uint i = 0; i < header.dwNumEntries; i++)
            {
                long entryOffset = offset + (i * header.dwEntrySize);
                if (entryOffset + header.dwEntrySize > accessor.Capacity)
                {
                    break;
                }

                // Read szSrcName (260 bytes string, properly truncated at first null)
                byte[] nameBytes = new byte[260];
                accessor.ReadArray(entryOffset, nameBytes, 0, 260);
                int nullIdx = Array.IndexOf(nameBytes, (byte)0);
                string srcName = nullIdx >= 0 ? Encoding.ASCII.GetString(nameBytes, 0, nullIdx) : Encoding.ASCII.GetString(nameBytes);

                // Read szSrcUnits (260 bytes string, properly truncated at first null)
                byte[] unitBytes = new byte[260];
                accessor.ReadArray(entryOffset + 260, unitBytes, 0, 260);
                int unitNullIdx = Array.IndexOf(unitBytes, (byte)0);
                string srcUnits = unitNullIdx >= 0 ? Encoding.ASCII.GetString(unitBytes, 0, unitNullIdx) : Encoding.ASCII.GetString(unitBytes);

                // Read data (float at offset 1300 / 0x514)
                float data = accessor.ReadSingle(entryOffset + 1300);

                // Read dwSrcId (uint at offset 1320 / 0x528)
                uint srcId = accessor.ReadUInt32(entryOffset + 1320);

                // Source ID 3 = GPU Voltage, Source ID 4 = GPU Voltage Aux
                bool isVoltageSource = srcId == 3 || srcId == 4 || srcId == 5 ||
                                       srcName.Contains("voltage", StringComparison.OrdinalIgnoreCase) ||
                                       srcName.Contains("VCore", StringComparison.OrdinalIgnoreCase) ||
                                       srcName.Contains("VDDC", StringComparison.OrdinalIgnoreCase) ||
                                       srcName.Contains("FBVDDC", StringComparison.OrdinalIgnoreCase);

                if (isVoltageSource && !srcName.Contains("CPU", StringComparison.OrdinalIgnoreCase))
                {
                    double val = data;
                    if (val > 10.0)
                    {
                        val /= 1000.0; // Convert mV to V
                    }
                    if (val > 0.1 && val < 3.0)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MSI Afterburner] Matched GPU Voltage sensor '{srcName}' (ID: {srcId}): {val:F3} V");
                        return val;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MSI Afterburner] Shared memory reader error: {ex.Message}");
        }

        return null;
    }
}
