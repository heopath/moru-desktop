using System;
using System.IO;
using System.Media;
using System.Text;

namespace Moru;

// A short original PCM chime, generated once on first use. No file/network access or idle timer.
internal sealed class AlarmTone : IDisposable
{
    private MemoryStream? stream;
    private SoundPlayer? player;
    internal bool Play()
    {
        try
        {
            if (player is null)
            {
                stream = new MemoryStream(CreateWave(), writable: false);
                player = new SoundPlayer(stream);
                player.Load();
            }
            player.Play();
            DiagnosticLog.Write("Alarm chime playback requested");
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or TimeoutException)
        { Dispose(); DiagnosticLog.Write("Alarm chime playback unavailable"); return false; }
    }
    internal void Stop() => player?.Stop();
    public void Dispose() { player?.Stop(); player?.Dispose(); player = null; stream?.Dispose(); stream = null; }

    internal static byte[] CreateWave()
    {
        const int rate = 22050, samples = rate * 2;
        using var wave = new MemoryStream(44 + samples * 2);
        using var writer = new BinaryWriter(wave, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + samples * 2);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16);
        writer.Write((short)1); writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2);
        writer.Write((short)2); writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(samples * 2);
        double[] notes = { 659.25, 783.99, 987.77 };
        for (int i = 0; i < samples; i++)
        {
            double t = i / (double)rate, value = 0;
            for (int note = 0; note < notes.Length; note++)
            {
                double age = t - note * 0.42;
                if (age is < 0 or > 0.9) continue;
                double envelope = Math.Min(1, age / 0.01) * Math.Exp(-5 * age) * Math.Min(1, (0.9 - age) / 0.04);
                value += 0.24 * envelope * Math.Sin(2 * Math.PI * notes[note] * age);
            }
            writer.Write((short)(Math.Clamp(value, -1, 1) * short.MaxValue));
        }
        return wave.ToArray();
    }
}
