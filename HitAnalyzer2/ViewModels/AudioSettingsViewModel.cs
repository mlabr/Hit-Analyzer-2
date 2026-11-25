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
        private readonly SgaGenerator _guidance = new();
        private bool _disposed;

        [ObservableProperty]
        private bool isEnabled;

        [ObservableProperty]
        private bool isSgaEnabled;

        [ObservableProperty]
        private bool isSgaStereo;

        [ObservableProperty]
        private bool isSgaFocusOnTarget;

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
            isSgaEnabled = settings.IsSgaEnabled;
            isSgaStereo = _guidance.Stereo = settings.IsSgaStereo;
            isSgaFocusOnTarget = _guidance.FocusOnTarget = settings.IsSgaFocusOnTarget;
            selectedEffect = Effects.First(option => option.Mode == settings.Effect);
            RefreshGuidance();
        }

        internal AudioSettings GetConfiguration() => new()
        {
            IsEnabled = IsEnabled,
            Effect = SelectedEffect?.Mode ?? AudioEffectMode.HitOnly,
            IsSgaEnabled = IsSgaEnabled,
            IsSgaStereo = IsSgaStereo,
            IsSgaFocusOnTarget = IsSgaFocusOnTarget
        };

        partial void OnIsEnabledChanged(bool value)
        {
            AudioError = null;
            if (!value)
                ReleaseAudio();
            else
                RefreshGuidance();
        }

        partial void OnIsSgaEnabledChanged(bool value) => RefreshGuidance();

        partial void OnIsSgaStereoChanged(bool value) => _guidance.Stereo = value;

        partial void OnIsSgaFocusOnTargetChanged(bool value) => _guidance.FocusOnTarget = value;

        internal void UpdateGuidance(double score, double offset, double outsideDistanceInTargetRadii) =>
            _guidance.UpdateAim(score, offset, outsideDistanceInTargetRadii);

        private void RefreshGuidance()
        {
            if (IsEnabled && IsSgaEnabled)
                Play(audio => audio.StartGuidance(_guidance));
            else
                _audio?.StopGuidance();
        }

        internal void PlaySessionState(bool running) =>
            Play(audio =>
            {
                if (running)
                    audio.PlaySessionStart(AudioSamples.SessionStarted);
                else
                    audio.PlayAfterLastShot(AudioSamples.SessionEnded);
            });

        internal void PlayHit(HitPosition? hit, TargetModel? target = null)
        {
            var mode = SelectedEffect?.Mode ?? AudioEffectMode.HitOnly;
            if (mode == AudioEffectMode.HitOnly || hit is null)
            {
                Play(audio => audio.PlayShot([AudioSamples.Gunshot]));
                return;
            }

            var clips = new List<string> { AudioSamples.Gunshot };
            if (hit.Score is >= 0 and <= 10)
                clips.Add(AudioSamples.Score(Math.Round(hit.Score, MidpointRounding.AwayFromZero)));

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
                return AudioSamples.Center;

            // Image Y increases downward. Measure clockwise from the top, centered on each sector.
            double angle = Math.Atan2(x, -y);
            int nearest = (int)Math.Floor(angle * sectorCount / Math.Tau + 0.5);
            int sector = (nearest + sectorCount - 1) % sectorCount + 1;
            return AudioSamples.Direction(sectorCount, sector);
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
