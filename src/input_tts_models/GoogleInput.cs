using Google.Cloud.TextToSpeech.V1;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using System.Text.Json;

using Glossa.src.utility;

namespace Glossa.src.input_tts_models
{
    public static class InputTTS_Google
    {
        private const string CredentialsPath = "../../../google-key.json";
        //private static Settings _settings;


        public static async Task Speak(string text)
        {

            // 1. Find audio output device
            using var enumerator = new MMDeviceEnumerator();
            var outputDevice = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                                    .FirstOrDefault(d => d.FriendlyName.Contains("Voicemeeter VAIO3"))
                                ?? throw new Exception("Voicemeeter VAIO3 render device not found.");

            // 2. Initialize Google TTS client
            if (!File.Exists(CredentialsPath))
            {
                throw new FileNotFoundException(
                    $"Google credentials not found at: {Path.GetFullPath(CredentialsPath)}\n" +
                    "Please ensure:\n" +
                    "1. google-key.json exists in the project's root folder\n" +
                    "2. The file is copied to output directory (set 'Copy to Output Directory' = 'Copy if newer')"
                );
            }

            var client = new TextToSpeechClientBuilder
            {
                CredentialsPath = CredentialsPath
            }.Build();


            // 3. Configure voice parameters

            string jsonPath = "../../../data/google_voices.json";
            if (!File.Exists(jsonPath)) return;

            string json = await File.ReadAllTextAsync(jsonPath);
            var languages = JsonSerializer.Deserialize<Dictionary<string, GoogleLanguageItem>>(json);

            string selectedModel = $"{SettingsHelper.GetValue<string>("TargetLanguage")}-Wavenet-A"; // default 

            if (languages.TryGetValue(SettingsHelper.GetValue<string>("TargetLanguage"), out var langItem))
            {
                string model = SettingsHelper.GetValue<string>("TargetVoiceGender") == "male"
                    ? langItem.male.model
                    : langItem.female.model;

                selectedModel = model;
            }


            string LanguageCode = SettingsHelper.GetValue<string>("TargetLanguage");
            var voice = new VoiceSelectionParams
            {
                LanguageCode = LanguageCode,
                Name = selectedModel,
                SsmlGender = SsmlVoiceGender.Male
            };

            var audioConfig = new AudioConfig
            {
                AudioEncoding = AudioEncoding.Mp3,
                SpeakingRate = 1.0, // Normal speed
                Pitch = 0.0,        // Neutral pitch
                VolumeGainDb = 0.0  // Default volume
            };

            // 4. Generate speech via gRPC
            var response = await client.SynthesizeSpeechAsync(new SynthesizeSpeechRequest
            {
                Input = new SynthesisInput { Text = text },
                Voice = voice,
                AudioConfig = audioConfig
            });

            // 5. Play audio using NAudio
            using var waveOut = new WasapiOut(outputDevice, AudioClientShareMode.Shared, false, 200);
            using var mp3Stream = new MemoryStream(response.AudioContent.ToByteArray());
            using var reader = new Mp3FileReader(mp3Stream);

            waveOut.Init(reader);
            waveOut.Play();

            // Wait until playback completes
            while (waveOut.PlaybackState == PlaybackState.Playing)
                await Task.Delay(100);
        }
    }
}
