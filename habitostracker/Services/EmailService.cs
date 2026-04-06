using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace HabitTrackerApp.Services
{
    public class EmailService
    {
        private readonly IConfiguration _configuration;

        public EmailService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public async Task SendEmailAsync(string toEmail, string subject, string body)
        {
            var apiKey = _configuration["Resend:ApiKey"] ?? _configuration["Resend__ApiKey"];

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

            var payload = new
            {
                from = "HabitTracker <onboarding@resend.dev>",
                to = new[] { toEmail },
                subject = subject,
                html = body
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await client.PostAsync("https://api.resend.com/emails", content);
            Console.WriteLine($"📧 Resend status: {response.StatusCode}");
            var responseBody = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"📧 Resend response: {responseBody}");
        }
    }
}