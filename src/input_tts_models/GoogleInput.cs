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
using System.Reflection;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Speech.V1;

namespace Glossa.src.input_tts_models
{
    public static class InputTTS_Google
    {
        

        //private const string CredentialsPath = "../../../google-key.json";
        //private static Settings _settings;


        public static async Task Speak(string text)
        {

            using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Glossa.google-key.json");

            if (stream == null)
                throw new InvalidOperationException("google-key.json not found in resources");

            var credential = GoogleCredential.FromStream(stream);

            var client = new TextToSpeechClientBuilder
            {
                Credential = credential
            }.Build();



            using var enumerator = new MMDeviceEnumerator();
            var outputDevice = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                                    .FirstOrDefault(d => d.FriendlyName.Contains("Voicemeeter VAIO3"))
                                ?? throw new Exception("Voicemeeter VAIO3 render device not found.");


            
            string jsonPath = Path.Combine(Path.GetTempPath(), "google_voices.json");
            using (var resourceStream = Assembly.GetExecutingAssembly()
                       .GetManifestResourceStream("Glossa.data.google_voices.json")) // Namespace + file name
            using (var fileStream = File.Create(jsonPath))
            {
                resourceStream.CopyTo(fileStream);
            }

            //string jsonPath = "../../../data/google_voices.json";
            if (!File.Exists(jsonPath)) return;

            string json = await File.ReadAllTextAsync(jsonPath);
            var languages = JsonSerializer.Deserialize<Dictionary<string, GoogleLanguageItem>>(json);

            string selectedModel = $"{SettingsHelper.GetValue<string>("TargetLanguage")}-Wavenet-A"; // default 

            if (languages.TryGetValue(SettingsHelper.GetValue<string>("TargetLanguage"), out var langItem))
            {
                System.Diagnostics.Debug.WriteLine("hi");
                if (SettingsHelper.GetValue<string>("UserVoiceGender") == "Male")
                {
                    selectedModel = langItem.male.model;
                    System.Diagnostics.Debug.WriteLine($"male: {langItem.male.model}");
                }
                else
                {
                    selectedModel = langItem.female.model;
                    System.Diagnostics.Debug.WriteLine($"female: {langItem.female.model}");
                }
            }

            System.Diagnostics.Debug.WriteLine($"selected: { selectedModel}");
            string LanguageCode = SettingsHelper.GetValue<string>("TargetLanguage");
            var voice = new VoiceSelectionParams
            {
                LanguageCode = LanguageCode,
                Name = selectedModel,
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
