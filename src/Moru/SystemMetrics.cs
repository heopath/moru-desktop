using System;
using System.Runtime.InteropServices;

namespace Moru;

internal sealed class SystemMetrics
{
    private CpuTimes? previous;
    public bool SupportsCpu => Native.GetActiveProcessorGroupCount() <= 1;
    public void Reset() => previous = null;

    public Metrics Read()
    {
        double? cpu = null;
        if (SupportsCpu && Native.GetSystemTimes(out ulong idle, out ulong kernel, out ulong user))
        {
            var current = new CpuTimes(idle, kernel, user);
            if (previous.HasValue) cpu = CpuMath.Usage(previous.Value, current);
            previous = current;
        }
        else previous = null;

        var memory = new Native.MemoryStatus { Length = (uint)Marshal.SizeOf<Native.MemoryStatus>() };
        return Native.GlobalMemoryStatusEx(ref memory)
            ? new Metrics(cpu, memory.TotalPhysical, memory.AvailablePhysical)
            : new Metrics(cpu, 0, 0);
    }

    private static class Native
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
        [DllImport("kernel32.dll")]
        internal static extern ushort GetActiveProcessorGroupCount();
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);

        [StructLayout(LayoutKind.Sequential)]
        internal struct MemoryStatus
        {
            public uint Length, Load;
            public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile;
            public ulong TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
        }
    }
}
