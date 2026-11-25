using CommunityToolkit.Mvvm.ComponentModel;
using HitAnalyzer2.Models;
using HitAnalyzer2.Models.Shooting;
using HitAnalyzer2.Services.Audio;
using System;
using System.Collections.Generic;
using System.Linq;

namespace HitAnalyzer2.ViewModels
{
    public partial class AudioSettingsViewModel : ViewModelBase, IDisposable
    {
        public sealed record EffectOption(AudioEffectMode Mode, string Label)
        {
            public override string ToString() => Label;
        }

        public IReadOnlyList<EffectOption> Effects { get; } =
        [
            new(AudioEffectMode.HitOnly, "Hit only"),
            new(AudioEffectMode.SpotterScore, "Spotter Score"),
            new(AudioEffectMode.Spotter8Directions, "Spotter 8 directions"),
            new(AudioEffectMode.Spotter12Directions, "Spotter 12 directions")
        ];

        private AudioService? _audio;
        private bool _disposed;

        [ObservableProperty]
        private bool isEnabled;

        [ObservableProperty]
        private EffectOption? selectedEffect;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasAudioError))]
        private string? audioError;

        public bool HasAudioError => AudioError is not null;

        internal AudioSettingsViewModel(AudioSettings settings)
        {
            isEnabled = settings.IsEnabled;
            selectedEffect = Effects.First(option => option.Mode == settings.Effect);
        }

        internal AudioSettings GetConfiguration() => new()
        {
            IsEnabled = IsEnabled,
            Effect = SelectedEffect?.Mode ?? AudioEffectMode.HitOnly
        };

        partial void OnIsEnabledChanged(bool value)
        {
            AudioError = null;
            if (!value)
                ReleaseAudio();
        }

        internal void PlaySessionState(bool running) =>
            Play(running ? "english/shooting_started.wav" : "english/shooting_stop.wav");

        internal void PlayHit(HitPosition? hit)
        {
            const string hitSample = "default_gunshot.wav";
            var mode = SelectedEffect?.Mode ?? AudioEffectMode.HitOnly;
            if (mode == AudioEffectMode.HitOnly || hit is null)
            {
                Play(hitSample);
                return;
            }

            var clips = new List<string> { hitSample };
            if (hit.Score is >= 0 and <= 10)
                clips.Add(FormattableString.Invariant($"english/{Math.Round(hit.Score, MidpointRounding.AwayFromZero):0}_points.wav"));

            if (mode is AudioEffectMode.Spotter8Directions or AudioEffectMode.Spotter12Directions)
            {
                int sectorCount = mode == AudioEffectMode.Spotter8Directions ? 8 : 12;
                double x = hit.X - TargetModel.Center.X;
                double y = hit.Y - TargetModel.Center.Y;
                clips.Add(GetDirectionSample(x, y, sectorCount));
            }

            Play(clips.ToArray());
        }

        private static string GetDirectionSample(double x, double y, int sectorCount)
        {
            if (x == 0 && y == 0)
                return "english/center.wav";

            // Image Y increases downward. Measure clockwise from the top, centered on each sector.
            double angle = Math.Atan2(x, -y);
            int nearest = (int)Math.Floor(angle * sectorCount / Math.Tau + 0.5);
            int sector = (nearest + sectorCount - 1) % sectorCount + 1;
            return sectorCount == 8
                ? FormattableString.Invariant($"english/s8d{sector}.wav")
                : FormattableString.Invariant($"english/s12d{sector:00}.wav");
        }

        private void Play(params string[] samples)
        {
            if (_disposed || !IsEnabled || HasAudioError)
                return;

            try
            {
                _audio ??= new AudioService();
                _audio.PlaySequence(samples);
            }
            catch (Exception ex)
            {
                AudioError = $"Audio unavailable. {ex.Message}";
                ReleaseAudio();
            }
        }

        private void ReleaseAudio()
        {
            _audio?.Dispose();
            _audio = null;
        }

        public void Dispose()
        {
            _disposed = true;
            ReleaseAudio();
        }
    }
}
