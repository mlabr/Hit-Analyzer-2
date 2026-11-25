using Avalonia.Threading;
using MiniAudioEx.Core.StandardAPI;
using System;
using System.Collections.Generic;
using System.IO;

namespace HitAnalyzer2.Services.Audio
{
    /// <summary>
    /// Owns the application's audio context. Create, use and dispose one instance on the UI thread.
    /// </summary>
    public sealed class AudioService : IDisposable
    {
        private readonly Dictionary<string, AudioClip> _clips = new(StringComparer.Ordinal);
        private readonly Dictionary<AudioSource, Queue<AudioClip>> _playbacks = new();
        private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromMilliseconds(10) };
        private bool _disposed;

        public AudioService()
        {
            Dispatcher.UIThread.VerifyAccess();
            var directory = Path.Combine(AppContext.BaseDirectory, "Data", "Audio");
            AudioContext.Initialize(48000, 2);
            try
            {
                foreach (var path in Directory.EnumerateFiles(directory, "*.wav", SearchOption.AllDirectories))
                {
                    var name = Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/');
                    _clips.Add(name, new AudioClip(File.ReadAllBytes(path)));
                }
            }
            catch
            {
                AudioContext.Deinitialize();
                throw;
            }

            _updateTimer.Tick += Update;
        }

        /// <summary>Starts a WAV named by its path relative to Data/Audio, using forward slashes.</summary>
        public void Play(string sample) => PlaySequence(sample);

        /// <summary>Plays the given WAVs in order, independently of other playbacks.</summary>
        public void PlaySequence(params string[] samples)
        {
            Dispatcher.UIThread.VerifyAccess();
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentNullException.ThrowIfNull(samples);
            if (samples.Length == 0)
                return;

            var clips = new Queue<AudioClip>(samples.Length);
            foreach (var sample in samples)
            {
                if (!_clips.TryGetValue(sample, out var clip))
                    throw new ArgumentException($"Unknown audio sample '{sample}'.", nameof(samples));

                clips.Enqueue(clip);
            }

            var source = new AudioSource(1) { Spatial = false };
            source.Play(clips.Dequeue());
            _playbacks.Add(source, clips);
            _updateTimer.Start();
        }

        private void Update(object? sender, EventArgs e)
        {
            AudioContext.Update();
            foreach (var (source, clips) in _playbacks)
            {
                if (source.IsPlaying)
                    continue;

                if (clips.TryDequeue(out var next))
                {
                    source.Play(next);
                }
                else
                {
                    source.Dispose();
                    _playbacks.Remove(source);
                }
            }

            if (_playbacks.Count == 0)
                _updateTimer.Stop();
        }

        public void Dispose()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_disposed)
                return;

            _disposed = true;
            _updateTimer.Stop();
            _updateTimer.Tick -= Update;
            AudioContext.Deinitialize();
            _playbacks.Clear();
            _clips.Clear();
        }
    }
}
