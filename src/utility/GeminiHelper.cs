using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Glossa.src.utility
{
    internal class GeminiHelper
    {
        private const string ApiKey = "AIzaSyD9BGDWsm-tzpstCPcMkxoUaEyLDzqnBRw";
        private static readonly HttpClient httpClient = new();

        public static async Task ListAvailableModels()
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models?key={ApiKey}";

            var response = await httpClient.GetAsync(url);
            var responseString = await response.Content.ReadAsStringAsync();

            Console.WriteLine("Available Gemini Models:");
            Console.WriteLine(responseString);
        }
    }
}