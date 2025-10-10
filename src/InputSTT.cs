using Google.Cloud.Speech.V1;
using Google.Protobuf;
using NAudio.Wave;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Glossa.src.utility;
using Glossa.src.input_tts_models;
using System.Reflection;
using System.IO;
using Google.Apis.Auth.OAuth2;

namespace Glossa.src
{
    public static class KeyChecker
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        public static bool IsKeyDown(string vKey)
        {
            if (string.IsNullOrEmpty(vKey))
                return false;

            try
            {
                // Handle hex strings ("0xA4") and named keys ("LeftAlt")
                int keyCode = ParseKeyCode(vKey);
                return keyCode != 0 && (GetAsyncKeyState(keyCode) & 0x8000) != 0;
            }
            catch
            {
                return false;
            }
        }
        private static int ParseKeyCode(string key)
        {
            // Hex format (e.g., "0xA4")
            if (key.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                return Convert.ToInt32(key.Substring(2), 16);
            }

            // Named keys fallback (add more as needed)
            return key.ToUpperInvariant() switch
            {
                "LEFTALT" => 0xA4,
                "RIGHTALT" => 0xA5,
                "F1" => 0x70,
                "SPACE" => 0x20,
                _ => 0 // Unknown key
            };
        }
    }

    public class InputProcessor
    {
        const int SampleRate = 16000;
        private readonly SpeechClient _speechClient;
        private readonly BlockingCollection<byte[]> _audioBufferQueue = new BlockingCollection<byte[]>(100);
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task _processingTask;

        //private readonly Settings _settings;
        public InputProcessor()
        {
            using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Glossa.google-key.json");

            if (stream == null)
                throw new InvalidOperationException("google-key.json not found in resources");

            var credential = GoogleCredential.FromStream(stream);

            // the builder accepts GoogleCredential directly
            _speechClient = new SpeechClientBuilder
            {
                Credential = credential
            }.Build();
        }


        private bool ShouldProcessInput()
        {
            return SettingsHelper.GetValue<bool>("InputTranslateEnabled") &&
                   KeyChecker.IsKeyDown(SettingsHelper.GetValue<string>("PushToTalkKey"));
        }

        public async Task Start()
        {
            System.Diagnostics.Debug.WriteLine("🎤 Speak freely. Continuous listening active.\n");
            _processingTask = Task.Run(ProcessAudioBuffers);
            await StartContinuousRecognitionAsync();
        }

        public async Task StopAsync()
        {
            _cts.Cancel();
            _audioBufferQueue.CompleteAdding();
            await _processingTask;
        }

        private async Task StartContinuousRecognitionAsync()
        {
            using (var waveIn = new WaveInEvent())
            {
                waveIn.WaveFormat = new WaveFormat(SampleRate, 1);
                waveIn.BufferMilliseconds = 100;
                waveIn.DataAvailable += (s, e) =>
                {
                    var buffer = new byte[e.BytesRecorded];
                    Buffer.BlockCopy(e.Buffer, 0, buffer, 0, e.BytesRecorded);
                    _audioBufferQueue.Add(buffer);
                };

                waveIn.StartRecording();
                System.Diagnostics.Debug.WriteLine("Microphone recording started...");
                await Task.Delay(-1, _cts.Token); // Block until canceled
                waveIn.StopRecording();
            }
        }

        private async Task ProcessAudioBuffers()
        {
            while (!_audioBufferQueue.IsCompleted)
            {
                try
                {
                    if (!ShouldProcessInput())
                    {
                        if (_audioBufferQueue.TryTake(out var _, 100, _cts.Token)) continue;
                        await Task.Delay(100, _cts.Token);
                        continue;
                    }

                    System.Diagnostics.Debug.WriteLine("Key pressed - starting speech processing");
                    using (var streamingCall = _speechClient.StreamingRecognize())
                    {
                        // Buffer to hold audio for the grace period
                        var gracePeriodBuffer = new List<byte[]>();
                        var gracePeriodMs = 300; // Extend recording by 300ms after key release
                        var gracePeriodEndTime = DateTime.MaxValue;

                        var writeTask = Task.Run(async () =>
                        {
                            await streamingCall.WriteAsync(new StreamingRecognizeRequest
                            {
                                StreamingConfig = new StreamingRecognitionConfig
                                {
                                    Config = new RecognitionConfig
                                    {
                                        Encoding = RecognitionConfig.Types.AudioEncoding.Linear16,
                                        SampleRateHertz = SampleRate,
                                        LanguageCode = SettingsHelper.GetValue<string>("UserLanguage"),
                                        Model = "latest_long"
                                    },
                                    InterimResults = false,
                                }
                            });

                            while (!_cts.Token.IsCancellationRequested)
                            {
                                if (_audioBufferQueue.TryTake(out var buffer, 100, _cts.Token))
                                {
                                    // If key is pressed or we're in grace period
                                    if (ShouldProcessInput() || DateTime.UtcNow < gracePeriodEndTime)
                                    {
                                        await streamingCall.WriteAsync(new StreamingRecognizeRequest
                                        {
                                            AudioContent = ByteString.CopyFrom(buffer)
                                        });

                                        // If key was just released, start grace period
                                        if (!ShouldProcessInput() && gracePeriodEndTime == DateTime.MaxValue)
                                        {
                                            gracePeriodEndTime = DateTime.UtcNow.AddMilliseconds(gracePeriodMs);
                                            System.Diagnostics.Debug.WriteLine($"Starting {gracePeriodMs}ms grace period");
                                        }
                                    }
                                    else
                                    {
                                        break; // Grace period ended
                                    }
                                }
                            }
                            await streamingCall.WriteCompleteAsync();
                        });

                        var readTask = Task.Run(async () =>
                        {
                            try
                            {
                                await foreach (var response in streamingCall.GetResponseStream().WithCancellation(_cts.Token))
                                {
                                    foreach (var result in response.Results)
                                    {
                                        if (result.IsFinal)
                                        {
                                            await ProcessFinalResult(result);
                                        }
                                    }
                                }
                            }
                            catch (Exception ex) when (ex is OperationCanceledException || ex is TaskCanceledException)
                            {
                                // Normal shutdown
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"❌ Error reading responses: {ex.Message}");
                            }
                        });

                        await Task.WhenAll(writeTask, readTask);
                    }
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is TaskCanceledException) { }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"❌ Processing error: {ex.Message}");
                    await Task.Delay(1000);
                }
            }
        }
        private async Task ProcessFinalResult(StreamingRecognitionResult result)
        {
            try
            {
                string transcript = result.Alternatives[0].Transcript;
                System.Diagnostics.Debug.WriteLine($"✅ Final: {transcript}");

                string languageTo = SettingsHelper.GetValue<string>("TargetLanguage").Substring(0, 2);
                string languageFrom = SettingsHelper.GetValue<string>("UserLanguage").Substring(0, 2);

                string translated = await Translator.Translate(transcript, languageFrom, languageTo);
                System.Diagnostics.Debug.WriteLine($"🌍 Translated: {translated}");

                string finalText = translated;

                if (SettingsHelper.GetValue<bool>("InputTranslateEnabled") && finalText != "")
                {
                    finalText = await Summary.Summarize(translated);
                    //System.Diagnostics.Debug.WriteLine($"✅ Summarized: {finalText}");
                }

                switch (SettingsHelper.GetValue<string>("InputTTSModel"))
                {
                    case "Google Cloud":
                        await InputTTS_Google.Speak(finalText);
                        break;
                    case "ElevenLabs":
                        await InputTTS_ElevenLabs.Speak(finalText);
                        break;
                    case "Windows Default":
                        await InputTTS_Native.Speak(finalText);
                        break;
                    default:
                        await InputTTS_Native.Speak(finalText);
                        break;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ Processing error: {ex.Message}");
            }
        }
    }
}
