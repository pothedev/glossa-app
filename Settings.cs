using System;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace Glossa
{
    public class Settings
    {
        // Language
        public string UserLanguage { get; set; } = "uk-UA";
        public string TargetLanguage { get; set; } = "pl-PL";

        // TTS Models
        public string InputTTSModel { get; set; } = "Google Cloud";
        public string OutputTTSModel { get; set; } = "Google Cloud";

        // Gender
        public string UserVoiceGender { get; set; } = "Female";
        public string TargetVoiceGender { get; set; } = "Male";

        // Mode
        public string UserMode { get; set; } = "Default";
        public string TargetMode { get; set; } = "Summary";

        // Translate Toggle
        public bool InputTranslateEnabled { get; set; } = true;
        public bool OutputTranslateEnabled { get; set; } = true;

        // Advanced
        public string PushToTalkKey { get; set; } = "0xA4";
        public bool SubtitlesEnabled { get; set; } = true;



        private static readonly string SettingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Glossa",
            "settings.json");

        public static Settings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                {
                    var json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
                var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}