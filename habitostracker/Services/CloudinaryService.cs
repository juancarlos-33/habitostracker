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

            // nudity-2.0 detecta anime/hentai, nudity detecta real
            // gore detecta sangre/violencia
            var requestUrl = "https://api.sightengine.com/1.0/check.json" +
                             $"?url={Uri.EscapeDataString(imageUrl)}" +
                             "&models=nudity,nudity-2.0,gore" +
                             $"&api_user={apiUser}&api_secret={apiSecret}";

            var response = await http.GetAsync(requestUrl);
            if (!response.IsSuccessStatusCode) return "safe";

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // ── SANGRE / VIOLENCIA → bloquear ──
            if (root.TryGetProperty("gore", out var gore))
            {
                double goreScore = TryGetDouble(gore, "prob");
                if (goreScore > 0.5) return "explicit";
            }

            // ── DESNUDEZ REAL (nudity v1) ──
            double raw = 0, partial = 0;
            if (root.TryGetProperty("nudity", out var nudityV1))
            {
                raw = TryGetDouble(nudityV1, "raw");
                partial = TryGetDouble(nudityV1, "partial");
            }

            // ── DESNUDEZ ANIME / DIBUJADA (nudity-2.0) ──
            double sexual = 0, display = 0, erotica = 0, verySuggestive = 0, suggestive = 0;
            if (root.TryGetProperty("nudity", out var nudityV2))
            {
                sexual = TryGetDouble(nudityV2, "sexual_activity");
                display = TryGetDouble(nudityV2, "sexual_display");
                erotica = TryGetDouble(nudityV2, "erotica");
                verySuggestive = TryGetDouble(nudityV2, "very_suggestive");
                suggestive = TryGetDouble(nudityV2, "suggestive");
            }

            // partes íntimas expuestas → bloquear
            bool isExplicit = raw > 0.4 || sexual > 0.4 || display > 0.4;

            // contenido sugestivo / semidesnudo → marcar sensible
            bool isSensitive = partial > 0.35 || erotica > 0.35 || verySuggestive > 0.4 || suggestive > 0.6;

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