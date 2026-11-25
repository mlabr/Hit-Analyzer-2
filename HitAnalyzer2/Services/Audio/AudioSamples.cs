using System;

namespace HitAnalyzer2.Services.Audio
{
    internal static class AudioSamples
    {
        internal const string Gunshot = "default_gunshot.wav";
        internal const string SessionStarted = "english/shooting_started.wav";
        internal const string SessionEnded = "english/shooting_ended.wav";
        internal const string Center = "english/center.wav";

        internal static string Score(double points) =>
            FormattableString.Invariant($"english/{points:0}_points.wav");

        internal static string Direction(int sectorCount, int sector) => sectorCount == 8
            ? FormattableString.Invariant($"english/s8d{sector}.wav")
            : FormattableString.Invariant($"english/s12d{sector:00}.wav");
    }
}
