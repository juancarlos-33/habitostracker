using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

public class CloudinaryService
{
    private readonly Cloudinary _cloudinary;
    private readonly IConfiguration _configuration;

    public CloudinaryService(IConfiguration configuration)
    {
        _configuration = configuration;
        var account = new Account(
            configuration["Cloudinary:CloudName"],
            configuration["Cloudinary:ApiKey"],
            configuration["Cloudinary:ApiSecret"]
        );
        _cloudinary = new Cloudinary(account);
    }

    public async Task<string> UploadImageAsync(IFormFile file, string folder = "habitostracker/posts")
    {
        using var stream = file.OpenReadStream();
        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(file.FileName, stream),
            Folder = folder,
            PublicId = Guid.NewGuid().ToString(),
            Overwrite = true
        };
        var result = await _cloudinary.UploadAsync(uploadParams);
        if (result.Error != null) throw new Exception(result.Error.Message);
        return result.SecureUrl.ToString();
    }

    public async Task<string> UploadVideoAsync(IFormFile file, string folder, int maxDuration = 30)
    {
        using var stream = file.OpenReadStream();
        _cloudinary.Api.Timeout = 180000;
        var uploadParams = new VideoUploadParams
        {
            File = new FileDescription(file.FileName, stream),
            Folder = folder,
            PublicId = Guid.NewGuid().ToString(),
            Overwrite = true
        };
        var result = await _cloudinary.UploadAsync(uploadParams);
        if (result.Error != null) throw new Exception(result.Error.Message);
        return result.SecureUrl.ToString();
    }

    public async Task<string> UploadVideoAsync(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        _cloudinary.Api.Timeout = 180000;
        var uploadParams = new VideoUploadParams
        {
            File = new FileDescription(file.FileName, stream),
            Folder = "habitostracker/videos",
            PublicId = Guid.NewGuid().ToString(),
            Overwrite = true
        };
        var result = await _cloudinary.UploadAsync(uploadParams);
        if (result.Error != null) throw new Exception(result.Error.Message);
        return result.SecureUrl.ToString();
    }

    // "safe" | "sensitive" | "explicit"
    public async Task<string> CheckImageModeration(string imageUrl)
    {
        var apiUser = _configuration["Sightengine:ApiUser"] ?? "";
        var apiSecret = _configuration["Sightengine:ApiSecret"] ?? "";

        if (string.IsNullOrWhiteSpace(apiUser) || apiUser.StartsWith("TU_"))
            return "safe";

        try
        {
            using var http = new HttpClient();
            http.Timeout = TimeSpan.FromSeconds(15);

            var requestUrl = "https://api.sightengine.com/1.0/check.json" +
                             $"?url={Uri.EscapeDataString(imageUrl)}" +
                             "&models=nudity-2.0" +
                             $"&api_user={apiUser}&api_secret={apiSecret}";

            var response = await http.GetAsync(requestUrl);
            if (!response.IsSuccessStatusCode) return "safe";

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("nudity", out var nudity)) return "safe";

            // nudity-2.0 keys
            double sexual = TryGetDouble(nudity, "sexual_activity");
            double display = TryGetDouble(nudity, "sexual_display");
            double erotica = TryGetDouble(nudity, "erotica");
            double verySuggestive = TryGetDouble(nudity, "very_suggestive");
            double suggestive = TryGetDouble(nudity, "suggestive");

            // Also try legacy v1 keys as fallback
            double raw = TryGetDouble(nudity, "raw");
            double partial = TryGetDouble(nudity, "partial");

            bool isExplicit = sexual > 0.5 || display > 0.5 || raw > 0.5;
            bool isSensitive = erotica > 0.4 || verySuggestive > 0.5 || partial > 0.5;

            if (isExplicit) return "explicit";
            if (isSensitive) return "sensitive";
            return "safe";
        }
        catch
        {
            return "safe";
        }
    }

    private static double TryGetDouble(JsonElement element, string key)
    {
        if (element.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.Number)
            return prop.GetDouble();
        return 0.0;
    }

    public async Task DeleteImageAsync(string imageUrl)
    {
        try
        {
            var uri = new Uri(imageUrl);
            var segments = uri.AbsolutePath.Split('/');
            var uploadIndex = Array.IndexOf(segments, "upload");
            if (uploadIndex < 0) return;

            var startIndex = uploadIndex + 1;
            if (startIndex < segments.Length && segments[startIndex].StartsWith("v"))
                startIndex++;

            var publicIdWithExt = string.Join("/", segments.Skip(startIndex));
            var publicId = System.IO.Path.ChangeExtension(publicIdWithExt, null);

            var deleteParams = new DeletionParams(publicId);
            await _cloudinary.DestroyAsync(deleteParams);
        }
        catch { }
    }
}
