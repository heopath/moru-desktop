using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace Moru;

internal sealed record AudioTarget(string Id, string Name)
{
    public override string ToString() => Name;
}
internal sealed record AudioSnapshot(IReadOnlyList<AudioTarget> Targets, double? Volume, bool Muted, string? Error = null);

// Core Audio only. No audio capture, media decoding, process scanning, or persistent polling.
// COM objects are created and released on the calling thread on every bounded refresh/write.
internal static class AudioControl
{
    private static readonly AudioTarget Master = new("master", "전체 소리");
    internal static AudioSnapshot Read(string target = "master") => Access(target, null, null);
    internal static bool SetVolume(string target, double percent) => Access(target, Math.Clamp(percent, 0, 100) / 100, null).Error is null;
    internal static bool SetMute(string target, bool muted) => Access(target, null, muted).Error is null;

    private static AudioSnapshot Access(string target, double? setVolume, bool? setMute)
    {
        object? enumerator = null, endpoint = null, master = null, manager = null, sessions = null;
        var targets = new List<AudioTarget> { Master };
        double? volume = null; bool muted = false, found = false;
        try
        {
            enumerator = new DeviceEnumerator();
            ((IDeviceEnumerator)enumerator).GetDefaultAudioEndpoint(0, 1, out var device); endpoint = device;
            if (target == "master")
            {
                Guid iid = typeof(IEndpointVolume).GUID;
                device.Activate(ref iid, 23, IntPtr.Zero, out master);
                var control = (IEndpointVolume)master;
                Guid context = Guid.Empty;
                if (setVolume.HasValue) control.SetMasterVolumeLevelScalar((float)setVolume.Value, ref context);
                if (setMute.HasValue) control.SetMute(setMute.Value, ref context);
                control.GetMasterVolumeLevelScalar(out float level); control.GetMute(out muted);
                volume = level * 100d; found = true;
                if (setVolume.HasValue || setMute.HasValue) return new(targets, volume, muted);
            }
            Guid managerId = typeof(ISessionManager).GUID;
            device.Activate(ref managerId, 23, IntPtr.Zero, out manager);
            ((ISessionManager)manager).GetSessionEnumerator(out var list); sessions = list;
            list.GetCount(out int count);
            for (int i = 0; i < Math.Min(count, 128); i++)
            {
                object? session = null;
                try
                {
                    list.GetSession(i, out session);
                    var control = (ISessionControl)session;
                    control.GetState(out int state);
                    if (state != 1) continue;
                    control.GetProcessId(out uint pid);
                    string id = "pid:" + pid;
                    if (targets.All(t => t.Id != id)) targets.Add(new(id, ProcessName(pid)));
                    if (target != id) continue;
                    var simple = (ISimpleVolume)session;
                    Guid context = Guid.Empty;
                    if (setVolume.HasValue) simple.SetMasterVolume((float)setVolume.Value, ref context);
                    if (setMute.HasValue) simple.SetMute(setMute.Value, ref context);
                    simple.GetMasterVolume(out float level); simple.GetMute(out bool isMuted);
                    volume ??= level * 100d;
                    muted = found ? muted && isMuted : isMuted;
                    found = true;
                }
                catch (Exception ex) when (ex is COMException or InvalidCastException) { }
                finally { Release(session); }
            }
            return new(targets, volume, muted, found ? null : "선택한 앱의 재생이 끝났어요. 대상을 다시 선택해 주세요.");
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException)
        {
            return new(targets, null, false, "출력 장치를 확인해 주세요.");
        }
        finally { Release(sessions); Release(manager); Release(master); Release(endpoint); Release(enumerator); }
    }
    private static string ProcessName(uint id)
    {
        if (id == 0) return "시스템 소리";
        try
        {
            using var process = Process.GetProcessById((int)id);
            return process.ProcessName switch { "chrome" => "Chrome", "msedge" => "Edge", "Spotify" => "Spotify", var name => name };
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return $"앱 {id}"; }
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] private class DeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDeviceEnumerator
    {
        void EnumAudioEndpoints(int flow, uint states, [MarshalAs(UnmanagedType.Interface)] out object devices);
        void GetDefaultAudioEndpoint(int flow, int role, out IDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDevice { void Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result); }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEndpointVolume
    {
        void RegisterControlChangeNotify(IntPtr callback); void UnregisterControlChangeNotify(IntPtr callback);
        void GetChannelCount(out uint count); void SetMasterVolumeLevel(float level, ref Guid context);
        void SetMasterVolumeLevelScalar(float level, ref Guid context); void GetMasterVolumeLevel(out float level);
        void GetMasterVolumeLevelScalar(out float level); void SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context); void GetChannelVolumeLevel(uint channel, out float level);
        void GetChannelVolumeLevelScalar(uint channel, out float level); void SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessionManager
    {
        void GetAudioSessionControl(IntPtr session, uint flags, out IntPtr control);
        void GetSimpleAudioVolume(IntPtr session, uint flags, out IntPtr volume);
        void GetSessionEnumerator(out ISessionEnumerator enumerator);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessionEnumerator
    {
        void GetCount(out int count); void GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISessionControl
    {
        void GetState(out int state); void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path); void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        void GetGroupingParam(out Guid group); void SetGroupingParam(ref Guid group, ref Guid context);
        void RegisterAudioSessionNotification(IntPtr callback); void UnregisterAudioSessionNotification(IntPtr callback);
        void GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id); void GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetProcessId(out uint id);
    }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISimpleVolume
    {
        void SetMasterVolume(float level, ref Guid context); void GetMasterVolume(out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool muted, ref Guid context); void GetMute([MarshalAs(UnmanagedType.Bool)] out bool muted);
    }
}
