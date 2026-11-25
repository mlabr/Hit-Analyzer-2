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

        internal int DirectionCount => SelectedEffect?.Mode switch
        {
            AudioEffectMode.Spotter8Directions => 8,
            AudioEffectMode.Spotter12Directions => 12,
            _ => 0
        };

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
            Play(audio =>
            {
                if (running)
                    audio.PlaySessionStart("english/shooting_started.wav");
                else
                    audio.PlayAfterLastShot("english/shooting_stop.wav");
            });

        internal void PlayHit(HitPosition? hit, TargetModel? target = null)
        {
            const string hitSample = "default_gunshot.wav";
            var mode = SelectedEffect?.Mode ?? AudioEffectMode.HitOnly;
            if (mode == AudioEffectMode.HitOnly || hit is null)
            {
                Play(audio => audio.PlayShot([hitSample]));
                return;
            }

            var clips = new List<string> { hitSample };
            if (hit.Score is >= 0 and <= 10)
                clips.Add(FormattableString.Invariant($"english/{Math.Round(hit.Score, MidpointRounding.AwayFromZero):0}_points.wav"));

            int sectorCount = DirectionCount;
            if (sectorCount > 0)
            {
                double x = hit.X - TargetModel.Center.X;
                double y = hit.Y - TargetModel.Center.Y;
                clips.Add(GetDirectionSample(x, y, sectorCount, target));
            }

            Play(audio => audio.PlayShot(clips.ToArray()));
        }

        private static string GetDirectionSample(double x, double y, int sectorCount, TargetModel? target)
        {
            double distance = double.Hypot(x, y);
            if (distance == 0 ||
                target?.MaxDiameter is > 0 and var diameter &&
                target.Rings.Any(ring => ring.PointValue == 10 && distance <= ring.Radius / diameter))
                return "english/center.wav";

            // Image Y increases downward. Measure clockwise from the top, centered on each sector.
            double angle = Math.Atan2(x, -y);
            int nearest = (int)Math.Floor(angle * sectorCount / Math.Tau + 0.5);
            int sector = (nearest + sectorCount - 1) % sectorCount + 1;
            return sectorCount == 8
                ? FormattableString.Invariant($"english/s8d{sector}.wav")
                : FormattableString.Invariant($"english/s12d{sector:00}.wav");
        }

        private void Play(Action<AudioService> playback)
        {
            if (_disposed || !IsEnabled || HasAudioError)
                return;

            try
            {
                _audio ??= new AudioService();
                playback(_audio);
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
