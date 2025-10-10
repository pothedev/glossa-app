using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
using System.Diagnostics;

public static class Summary
{
    public class RequestData
    {
        public string? text { get; set; }
    }

    public static async Task<string> Summarize(string passedText)
    {
        using HttpClient client = new HttpClient();

        string url = "https://glossa-bk.onrender.com/summary";

        var requestData = new RequestData { text = passedText };
        string jsonString = JsonSerializer.Serialize(requestData);
        var content = new StringContent(jsonString, Encoding.UTF8, "application/json");

        //Console.WriteLine("Starting request...");

        // Start the stopwatch
        Stopwatch sw = Stopwatch.StartNew();

        HttpResponseMessage response = await client.PostAsync(url, content);

        // Stop the stopwatch
        sw.Stop();

        if (response.IsSuccessStatusCode)
        {
            string responseContent = await response.Content.ReadAsStringAsync();

            // 👇 Use JsonDocument or directly parse string correctly
            string? summary = null;
            try
            {
                summary = JsonSerializer.Deserialize<string>(responseContent);
            }
            catch
            {
                // In case the server returns a JSON object like {"summary": "..."}
                using var doc = JsonDocument.Parse(responseContent);
                if (doc.RootElement.TryGetProperty("summary", out var summaryProp))
                    summary = summaryProp.GetString();
            }

            if (summary != null)
            {
                double percent = 100.0 / passedText.Length * summary.Length;
                //Console.WriteLine(summary);
                System.Diagnostics.Debug.WriteLine($"✅ Summarized: {summary}");
                System.Diagnostics.Debug.WriteLine($"Time elapsed: {sw.ElapsedMilliseconds} ms, {percent:F2}% of original");
                return summary;
            }
            else
            {
                Console.WriteLine("Error: Could not parse summary.");
            }
        }
        else
        {
            Console.WriteLine($"Error: {response.StatusCode}");
            Console.WriteLine($"Time elapsed: {sw.ElapsedMilliseconds} ms");
        }
        return "";
    }
}
