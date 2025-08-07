using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Speech.Synthesis;
using System.Text;
using System.Threading.Tasks;

using Glossa.src.utility;

namespace Glossa.src.input_tts_models
{
    public static class InputTTS_Native
    {
        private static readonly ConcurrentQueue<string> _speechQueue = new();
        private static readonly SemaphoreSlim _playbackLock = new(1, 1);
        private static bool _isPlaying;

        //private static Settings _settings;

        public static Task Speak(string text)
        {
            _speechQueue.Enqueue(text);
            _ = ProcessQueueAsync(); // Fire-and-forget queue processing
            return Task.CompletedTask;
        }

        private static async Task ProcessQueueAsync()
        {
            if (_isPlaying) return;

            await _playbackLock.WaitAsync();
            try
            {
                _isPlaying = true;
                while (_speechQueue.TryDequeue(out var text))
                {
                    await PlayTextAsync(text);
                }
            }
            finally
            {
                _isPlaying = false;
                _playbackLock.Release();
            }
        }

        private static async Task PlayTextAsync(string text)
        {
            // 1. Find audio output device
            using var enumerator = new MMDeviceEnumerator();
            var outputDevice = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                                    .FirstOrDefault(d => d.FriendlyName.Contains("Voicemeeter VAIO3"))
                                ?? throw new Exception("Voicemeeter VAIO3 render device not found.");

            // 2. Initialize SpeechSynthesizer
            using var synthesizer = new SpeechSynthesizer();

            // 3. Configure voice (FIXED EQUALS USAGE)
            var targetCulture = SettingsHelper.GetValue<string>("TargetLanguage");
            var installedVoice = synthesizer.GetInstalledVoices()
                .FirstOrDefault(v => string.Equals(v.VoiceInfo.Culture.Name, targetCulture, StringComparison.OrdinalIgnoreCase));

            if (installedVoice != null)
            {
                synthesizer.SelectVoice(installedVoice.VoiceInfo.Name);
            }
            else
            {
                Console.WriteLine($"Warning: {targetCulture} voice not found. Using default voice.");
            }

            // 4. Generate audio to memory stream
            using var memoryStream = new MemoryStream();
            synthesizer.SetOutputToWaveStream(memoryStream);
            synthesizer.Speak(text);
            memoryStream.Position = 0;

            // 5. Play audio with proper completion waiting
            using var waveOut = new WasapiOut(outputDevice, AudioClientShareMode.Shared, false, 200);
            using var reader = new WaveFileReader(memoryStream);

            var tcs = new TaskCompletionSource<bool>();
            waveOut.PlaybackStopped += (s, e) => tcs.TrySetResult(true);

            waveOut.Init(reader);
            waveOut.Play();
            await tcs.Task; // Wait for playback completion
        }
    }
}
