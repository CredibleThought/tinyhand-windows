namespace TinyHand;

/// <summary>Frame timings, written to %TEMP%\tinyhand-profile.txt when TINYHAND_PROFILE is set.</summary>
internal static class Profile
{
    private static readonly bool On = Environment.GetEnvironmentVariable("TINYHAND_PROFILE") != null;
    private static readonly double[] Sum = new double[4];
    private static int _frames;
    private static DateTime _last = DateTime.Now;

    public static void Add(params double[] ms)
    {
        if (!On)
            return;
        for (var i = 0; i < ms.Length; i++)
            Sum[i] += ms[i];
        _frames++;
        if ((DateTime.Now - _last).TotalSeconds < 2)
            return;
        var line = $"{DateTime.Now:T} frames={_frames} arm={Sum[0] / _frames:F2} ink={Sum[1] / _frames:F2} toolbar={Sum[2] / _frames:F2} pre={Sum[3] / _frames:F2}\n";
        File.AppendAllText(Path.Combine(Path.GetTempPath(), "tinyhand-profile.txt"), line);
        Array.Clear(Sum);
        _frames = 0;
        _last = DateTime.Now;
    }
}
