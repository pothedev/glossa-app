using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Text.Json;
using System.IO;


namespace Glossa
{
    class GoogleVoiceChecker
    {

        public static bool[] Google(string languageCode)
        {
            string path = "../../../google_voices.json";
            //System.Diagnostics.Debug.WriteLine($"🔍 Checking Google voices for: {languageCode}");

            if (!File.Exists(path))
            {
                System.Diagnostics.Debug.WriteLine("❌ File not found.");
                return new bool[] { false, false };
            }

            string json = File.ReadAllText(path);
            JsonNode? root = JsonNode.Parse(json);

            if (root == null)
            {
                System.Diagnostics.Debug.WriteLine("❌ Failed to parse JSON.");
                return new bool[] { false, false };
            }

            foreach (var kvp in root.AsObject())
            {
                string key = kvp.Key;
                var value = kvp.Value?.AsObject();

                if (value == null)
                {
                    System.Diagnostics.Debug.WriteLine($"⚠️ Entry '{key}' is null or malformed.");
                    continue;
                }

                string? langCode = key;
                string? langName = value["language"]?.ToString();

                //System.Diagnostics.Debug.WriteLine($"🔎 Checking language: {langCode} ({langName})");

                if (langCode.Equals(languageCode, StringComparison.OrdinalIgnoreCase))
                {
                    bool maleAvailable = value["male"]?["available"]?.GetValue<bool>() ?? false;
                    bool femaleAvailable = value["female"]?["available"]?.GetValue<bool>() ?? false;

                    System.Diagnostics.Debug.WriteLine($"✅ Found match - Male: {maleAvailable}, Female: {femaleAvailable}");
                    return new bool[] { maleAvailable, femaleAvailable };
                }
            }

            System.Diagnostics.Debug.WriteLine("❌ No match found.");
            return new bool[] { false, false };
        }
    }
}
