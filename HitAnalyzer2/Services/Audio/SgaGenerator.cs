using System;
using System.Threading;
using HitAnalyzer2.Models;

namespace HitAnalyzer2.Services.Audio
{
    internal enum SgaWaveform { Square, Sine, Triangle }

    internal sealed class SgaGenerator(SgaSettings settings)
    {
        private const double Volume = 0.2;
        private const double MinimumOffTargetVolumeScale = 0.4;
        private const double FullAttenuationDistanceInTargetRadii = 1.0;
        private const double FullBalanceOffset = 0.05;
        private static readonly SgaWaveform Waveform = SgaWaveform.Square;

        private sealed record Aim(double Score, Position Position, double OutsideDistanceInTargetRadii, long Time)
        {
            internal double HorizontalOffset => Position.X - TargetModel.Center.X;
        }
        private Aim? _aim;
        private double _phase;
        private double _pulsePhase;
        private double _gain;

        internal volatile bool Stereo = true;
        internal volatile bool FocusOnTarget;

        internal (double Left, double Right) GetPreview()
        {
            var aim = Volatile.Read(ref _aim);
            if (aim is null || Environment.TickCount64 - aim.Time >= 250)
                return default;

            var (left, right) = GetChannelGains(aim.HorizontalOffset);
            double quietChannelGain = settings.QuietChannelGain;
            if (quietChannelGain == 1)
                return (1, 1);

            // Display the direction above the quiet-channel floor.
            return ((left - quietChannelGain) / (1 - quietChannelGain),
                (right - quietChannelGain) / (1 - quietChannelGain));
        }

        private (double Left, double Right) GetChannelGains(double horizontalOffset)
        {
            double balance = Stereo ? Math.Clamp(horizontalOffset / FullBalanceOffset, -1, 1) : 0;
            if (FocusOnTarget)
                balance = -balance;
            return (1 - Math.Max(0, balance) * (1 - settings.QuietChannelGain),
                1 - Math.Max(0, -balance) * (1 - settings.QuietChannelGain));
        }

        internal void UpdateAim(double score, Position? position, double outsideDistanceInTargetRadii) =>
            Volatile.Write(ref _aim, double.IsFinite(score) && position is not null &&
                double.IsFinite(position.X) && double.IsFinite(position.Y) &&
                double.IsFinite(outsideDistanceInTargetRadii)
                ? new Aim(Math.Clamp(score, 0, 10), position, outsideDistanceInTargetRadii, Environment.TickCount64)
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
                ? settings.OnTargetMinimumFrequencyHz +
                    (settings.OnTargetMaximumFrequencyHz - settings.OnTargetMinimumFrequencyHz) * normalized
                : settings.OffTargetFrequencyHz;
            double period = settings.SlowPeriodSeconds + (settings.FastPeriodSeconds - settings.SlowPeriodSeconds) * normalized;
            var (left, right) = GetChannelGains(aim?.HorizontalOffset ?? 0);
            double ramp = 1.0 / (0.003 * sampleRate);

            for (int i = 0; i < output.Length; i += channels)
            {
                bool audible = valid && (normalized == 1 || _pulsePhase < settings.DutyCycle);
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
