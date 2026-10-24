using System.Media;
using System.Text;

namespace GTAWChatParser;

internal static class NotificationPing
{
    private static readonly SoundPlayer Player = new(CreateWav()) { LoadTimeout = 2000 };

    public static void Play()
    {
        try
        {
            Player.Play();
        }
        catch (Exception exception)
        {
            AppLog.Error("Notification sound failed.", exception);
        }
    }

    private static MemoryStream CreateWav()
    {
        const int sampleRate = 22050;
        const double seconds = 0.18;
        var count = (int)(sampleRate * seconds);
        var samples = new short[count];
        for (var i = 0; i < count; i++)
        {
            var time = i / (double)sampleRate;
            var attack = Math.Min(1d, time / 0.006);
            var decay = Math.Exp(-time * 22);
            var tone = Math.Sin(2 * Math.PI * 784 * time) * 0.78
                + Math.Sin(2 * Math.PI * 1176 * time) * 0.22;
            var level = tone * attack * decay * 0.28;
            samples[i] = (short)Math.Clamp(level * short.MaxValue, short.MinValue, short.MaxValue);
        }

        var dataLength = samples.Length * sizeof(short);
        var stream = new MemoryStream(44 + dataLength);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);
        foreach (var sample in samples)
            writer.Write(sample);

        stream.Position = 0;
        return stream;
    }
}
