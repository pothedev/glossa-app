using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.TextToSpeech.V1;
using NAudio.Wave;
using Glossa.src.utility;

namespace Glossa.src.output_tts_models
{
    public static class OutputTTS_Google
    {
        private static TextToSpeechClient _ttsClient;
        private static readonly object _clientLock = new();
        private const string OutputDeviceName = "Voicemeeter AUX";

        // Separate queues for text to synthesize and ready audio chunks
        private static readonly BlockingCollection<string> _textQueue = new(new ConcurrentQueue<string>(), 10);
        private static readonly BlockingCollection<AudioItem> _audioQueue = new(new ConcurrentQueue<AudioItem>(), 20);

        // Simple LRU cache for synthesized audio
        private static readonly Dictionary<string, CacheItem> _audioCache = new();
        private static readonly LinkedList<string> _cacheAccessOrder = new();
        private const int MAX_CACHE_SIZE = 100;
        private static readonly object _cacheLock = new();

        private static readonly SemaphoreSlim _synthSemaphore = new(2, 2); // Limit concurrent synthesis

        private static bool _workersStarted = false;
        private static readonly object _workersLock = new();
        private static CancellationTokenSource _workersCts;

        public static Task Speak(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return Task.CompletedTask;

            EnsureWorkersStarted();

            try
            {
                _textQueue.Add(text);
            }
            catch (InvalidOperationException)
            {
                // Queue is full, discard the oldest item and add new one
                if (_textQueue.TryTake(out _))
                {
                    _textQueue.Add(text);
                }
            }

            return Task.CompletedTask;
        }

        private static void EnsureWorkersStarted()
        {
            lock (_workersLock)
            {
                if (!_workersStarted)
                {
                    _workersCts = new CancellationTokenSource();
                    Task.Run(() => SynthesisWorker(_workersCts.Token));
                    Task.Run(() => PlaybackWorker(_workersCts.Token));
                    _workersStarted = true;
                }
            }
        }

        private static async Task SynthesisWorker(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    foreach (var text in _textQueue.GetConsumingEnumerable(cancellationToken))
                    {
                        await _synthSemaphore.WaitAsync(cancellationToken);

                        // Fire and forget - don't await this task
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await ProcessTextSynthesis(text);
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"❌ Synthesis error: {ex.Message}");
                            }
                            finally
                            {
                                _synthSemaphore.Release();
                            }
                        }, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Synthesis worker error: {ex.Message}");
                    await Task.Delay(1000, cancellationToken); // Prevent tight loop on persistent errors
                }
            }
        }

        private static async Task ProcessTextSynthesis(string text)
        {
            // Check cache first
            byte[] audioData = GetFromCache(text);
            if (audioData != null)
            {
                _audioQueue.Add(new AudioItem { AudioData = audioData });
                return;
            }

            // Synthesize new audio
            audioData = await SynthesizeTextAsync(text);

            // Cache the result
            AddToCache(text, audioData);

            // Add to audio queue
            _audioQueue.Add(new AudioItem { AudioData = audioData });
        }

        private static byte[] GetFromCache(string text)
        {
            lock (_cacheLock)
            {
                if (_audioCache.TryGetValue(text, out var cacheItem))
                {
                    // Update access order (move to front)
                    _cacheAccessOrder.Remove(text);
                    _cacheAccessOrder.AddFirst(text);
                    cacheItem.LastAccessTime = DateTime.UtcNow;
                    return cacheItem.AudioData;
                }
                return null;
            }
        }

        private static void AddToCache(string text, byte[] audioData)
        {
            lock (_cacheLock)
            {
                // Remove oldest items if cache is full
                while (_audioCache.Count >= MAX_CACHE_SIZE && _cacheAccessOrder.Count > 0)
                {
                    var oldestKey = _cacheAccessOrder.Last.Value;
                    _audioCache.Remove(oldestKey);
                    _cacheAccessOrder.RemoveLast();
                }

                // Add new item
                _audioCache[text] = new CacheItem
                {
                    AudioData = audioData,
                    LastAccessTime = DateTime.UtcNow
                };
                _cacheAccessOrder.AddFirst(text);
            }
        }

        private static async Task<byte[]> SynthesizeTextAsync(string text)
        {
            EnsureTtsClient();

            // Load voices map from embedded resource
            var voicesJson = ReadEmbeddedText(".data.google_voices.json");
            var voices = JsonSerializer.Deserialize<Dictionary<string, GoogleLanguageItem>>(voicesJson)
                         ?? new Dictionary<string, GoogleLanguageItem>();

            string userLang = SettingsHelper.GetValue<string>("UserLanguage");
            string selectedModel = $"{userLang}-Wavenet-A"; // default fallback

            if (voices.TryGetValue(userLang, out var langItem))
            {
                var wantMale = string.Equals(
                    SettingsHelper.GetValue<string>("TargetVoiceGender"), "Male", StringComparison.OrdinalIgnoreCase);

                if (wantMale && !string.IsNullOrWhiteSpace(langItem.male?.model))
                    selectedModel = langItem.male.model;
                else if (!wantMale && !string.IsNullOrWhiteSpace(langItem.female?.model))
                    selectedModel = langItem.female.model;
            }

            // Build request
            var voice = new VoiceSelectionParams
            {
                LanguageCode = userLang,
                Name = selectedModel,
                SsmlGender = SsmlVoiceGender.Neutral
            };

            var audioConfig = new AudioConfig
            {
                AudioEncoding = AudioEncoding.Mp3,
                SpeakingRate = 1.0,
                Pitch = 0.0,
                VolumeGainDb = 0.0
            };

            var response = await _ttsClient.SynthesizeSpeechAsync(new SynthesizeSpeechRequest
            {
                Input = new SynthesisInput { Text = text },
                Voice = voice,
                AudioConfig = audioConfig
            });

            return response.AudioContent.ToByteArray();
        }

        private static async Task PlaybackWorker(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    foreach (var audioItem in _audioQueue.GetConsumingEnumerable(cancellationToken))
                    {
                        await PlayAudioAsync(audioItem.AudioData, cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Playback worker error: {ex.Message}");
                    await Task.Delay(1000, cancellationToken); // Prevent tight loop on persistent errors
                }
            }
        }

        private static async Task PlayAudioAsync(byte[] mp3Data, CancellationToken cancellationToken)
        {
            try
            {
                // Use WaveOutEvent instead of WasapiOut for better compatibility
                using var waveOut = new WaveOutEvent();
                using var mp3Stream = new MemoryStream(mp3Data);
                using var reader = new Mp3FileReader(mp3Stream);

                // Configure waveout device if Voicemeeter is available
                try
                {
                    for (int i = 0; i < WaveOut.DeviceCount; i++)
                    {
                        var capabilities = WaveOut.GetCapabilities(i);
                        if (capabilities.ProductName.Contains(OutputDeviceName, StringComparison.OrdinalIgnoreCase))
                        {
                            waveOut.DeviceNumber = i;
                            break;
                        }
                    }
                }
                catch
                {
                    // Fallback to default device
                    waveOut.DeviceNumber = -1;
                }

                var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                using (cancellationToken.Register(() => tcs.TrySetCanceled()))
                {
                    waveOut.PlaybackStopped += (s, e) =>
                    {
                        if (e.Exception != null)
                            tcs.TrySetException(e.Exception);
                        else
                            tcs.TrySetResult(true);
                    };

                    waveOut.Init(reader);
                    waveOut.Play();

                    await tcs.Task;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Audio playback error: {ex.Message}");
                // Don't rethrow to keep the playback worker alive
            }
        }

        private static void EnsureTtsClient()
        {
            if (_ttsClient != null) return;

            lock (_clientLock)
            {
                if (_ttsClient != null) return;

                using var credStream = GetEmbeddedStreamBySuffix(".google-key.json");
                if (credStream == null)
                {
                    var names = string.Join(", ", Assembly.GetExecutingAssembly().GetManifestResourceNames());
                    throw new InvalidOperationException("google-key.json not found in resources. Found: " + names);
                }

                var credential = GoogleCredential.FromStream(credStream);
                _ttsClient = new TextToSpeechClientBuilder
                {
                    Credential = credential
                }.Build();
            }
        }

        // --- Embedded resource helpers ---

        private static string ReadEmbeddedText(string resourceSuffix)
        {
            using var s = GetEmbeddedStreamBySuffix(resourceSuffix)
                          ?? throw new FileNotFoundException($"Embedded resource '*{resourceSuffix}' not found.");
            using var sr = new StreamReader(s);
            return sr.ReadToEnd();
        }

        private static Stream GetEmbeddedStreamBySuffix(string resourceSuffix)
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames()
                          .FirstOrDefault(n => n.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase));
            return name != null ? asm.GetManifestResourceStream(name) : null;
        }

        // Helper method to clear queues (useful for shutdown/reset)
        public static void ClearQueues()
        {
            while (_textQueue.TryTake(out _)) { }
            while (_audioQueue.TryTake(out _)) { }

            lock (_cacheLock)
            {
                _audioCache.Clear();
                _cacheAccessOrder.Clear();
            }
        }

        // Helper method to shutdown workers
        public static async Task ShutdownAsync()
        {
            if (_workersCts != null)
            {
                _workersCts.Cancel();
                ClearQueues();

                // Give workers time to clean up
                await Task.Delay(100);

                _workersCts.Dispose();
                _workersCts = null;
                _workersStarted = false;
            }
        }

        // Helper method to get queue status (for monitoring/debugging)
        public static (int TextQueueCount, int AudioQueueCount, int CacheCount) GetQueueStatus()
        {
            lock (_cacheLock)
            {
                return (_textQueue.Count, _audioQueue.Count, _audioCache.Count);
            }
        }

        private class AudioItem
        {
            public byte[] AudioData { get; set; }
        }

        private class CacheItem
        {
            public byte[] AudioData { get; set; }
            public DateTime LastAccessTime { get; set; }
        }
    }

    // Match your existing JSON shape for google_voices.json
    public sealed class GoogleLanguageItem
    {
        public GoogleVoice male { get; set; }
        public GoogleVoice female { get; set; }
    }

    public sealed class GoogleVoice
    {
        public string model { get; set; }
    }
}