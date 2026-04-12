using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.Extensions.Configuration;

public class CloudinaryService
{
    private readonly Cloudinary _cloudinary;

    public CloudinaryService(IConfiguration configuration)
    {
        var account = new Account(
     configuration["Cloudinary:CloudName"],
     configuration["Cloudinary:ApiKey"],
     configuration["Cloudinary:ApiSecret"]
 );
        _cloudinary = new Cloudinary(account);
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

        if (result.Error != null)
            throw new Exception(result.Error.Message);

        return result.SecureUrl.ToString();
    }
    public async Task<string> UploadImageAsync(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        var uploadParams = new ImageUploadParams
        {
            File = new FileDescription(file.FileName, stream),
            Folder = "habitostracker/posts"
        };
        var result = await _cloudinary.UploadAsync(uploadParams);
        return result.SecureUrl.ToString();
    }
}