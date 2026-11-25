using System;
using System.Threading;

namespace HitAnalyzer2.Services.Audio
{
    internal enum SgaWaveform { Square, Sine, Triangle }

    internal sealed class SgaGenerator
    {
        private const double OffTargetFrequencyHz = 200;
        private const double OnTargetMinimumFrequencyHz = 400;
        private const double OnTargetMaximumFrequencyHz = 1200;
        private const double SlowPeriodSeconds = 0.40;
        private const double FastPeriodSeconds = 0.10;
        private const double Volume = 0.2;
        private const double MinimumOffTargetVolumeScale = 0.4;
        private const double FullAttenuationDistanceInTargetRadii = 1.0;
        private const double QuietChannelGain = 0.15;
        private const double FullBalanceOffset = 0.05;
        private const double DutyCycle = 0.4;
        private static readonly SgaWaveform Waveform = SgaWaveform.Square;

        private sealed record Aim(double Score, double HorizontalOffset, double OutsideDistanceInTargetRadii, long Time);
        private Aim? _aim;
        private double _phase;
        private double _pulsePhase;
        private double _gain;

        internal volatile bool Stereo = true;
        internal volatile bool FocusOnTarget;

        internal void UpdateAim(double score, double horizontalOffset, double outsideDistanceInTargetRadii) =>
            Volatile.Write(ref _aim, double.IsFinite(score) && double.IsFinite(horizontalOffset) &&
                double.IsFinite(outsideDistanceInTargetRadii)
                ? new Aim(Math.Clamp(score, 0, 10), horizontalOffset, outsideDistanceInTargetRadii, Environment.TickCount64)
                : null);

        internal void Render(Span<float> output, int channels, int sampleRate)
        {
            var aim = Volatile.Read(ref _aim);
            bool valid = aim is not null && Environment.TickCount64 - aim.Time < 250;
            double normalized = (aim?.Score ?? 0) / 10;
            double attenuation = Math.Clamp((aim?.OutsideDistanceInTargetRadii ?? 0) /
                FullAttenuationDistanceInTargetRadii, 0, 1);
            double volume = Volume * (1 - (1 - MinimumOffTargetVolumeScale) * attenuation);
            double frequency = normalized > 0
                ? OnTargetMinimumFrequencyHz + (OnTargetMaximumFrequencyHz - OnTargetMinimumFrequencyHz) * normalized
                : OffTargetFrequencyHz;
            double period = SlowPeriodSeconds + (FastPeriodSeconds - SlowPeriodSeconds) * normalized;
            double balance = Stereo
                ? Math.Clamp((aim?.HorizontalOffset ?? 0) / FullBalanceOffset, -1, 1)
                : 0;
            if (FocusOnTarget)
                balance = -balance;
            double left = 1 - Math.Max(0, balance) * (1 - QuietChannelGain);
            double right = 1 - Math.Max(0, -balance) * (1 - QuietChannelGain);
            double ramp = 1.0 / (0.003 * sampleRate);

            for (int i = 0; i < output.Length; i += channels)
            {
                bool audible = valid && (normalized == 1 || _pulsePhase < DutyCycle);
                _gain = audible ? Math.Min(1, _gain + ramp) : Math.Max(0, _gain - ramp);
                double wave = Waveform switch
                {
                    SgaWaveform.Sine => Math.Sin(Math.Tau * _phase),
                    SgaWaveform.Triangle => 1 - 4 * Math.Abs(_phase - 0.5),
                    _ => _phase < 0.5 ? 1 : -1
                };

                float sample = (float)(wave * _gain * volume);
                output[i] = channels == 1 ? sample : (float)(sample * left);
                for (int channel = 1; channel < channels; channel++)
                    output[i + channel] = (float)(sample * right);

                _phase += frequency / sampleRate;
                _phase -= Math.Floor(_phase);
                _pulsePhase = valid && normalized < 1 ? _pulsePhase + 1 / (period * sampleRate) : 0;
                _pulsePhase -= Math.Floor(_pulsePhase);
            }
        }
    }
}
