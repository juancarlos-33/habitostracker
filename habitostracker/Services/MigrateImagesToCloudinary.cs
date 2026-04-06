using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using HabitTrackerApp.Data;
using Microsoft.EntityFrameworkCore;

public class MigrateImagesToCloudinary
{
    public static async Task Run(HabitDbContext context, CloudinaryService cloudinaryService, IWebHostEnvironment environment)
    {
        // 🔹 MIGRAR IMÁGENES DE POSTS
        var posts = await context.Posts
            .Where(p => p.ImagePath != null && p.ImagePath.StartsWith("/posts/"))
            .ToListAsync();

        foreach (var post in posts)
        {
            var localPath = Path.Combine(environment.WebRootPath, post.ImagePath.TrimStart('/'));
            if (File.Exists(localPath))
            {
                using var stream = File.OpenRead(localPath);
                var formFile = new FormFileWrapper(stream, Path.GetFileName(localPath));
                post.ImagePath = await cloudinaryService.UploadImageAsync(formFile);
                Console.WriteLine($"✅ Post {post.Id} migrado");
            }
        }

        // 🔹 MIGRAR IMÁGENES DE COMENTARIOS
        var comments = await context.PostComments
            .Where(c => c.ImagePath != null && c.ImagePath.StartsWith("/comments/"))
            .ToListAsync();

        foreach (var comment in comments)
        {
            var localPath = Path.Combine(environment.WebRootPath, comment.ImagePath.TrimStart('/'));
            if (File.Exists(localPath))
            {
                using var stream = File.OpenRead(localPath);
                var formFile = new FormFileWrapper(stream, Path.GetFileName(localPath));
                comment.ImagePath = await cloudinaryService.UploadImageAsync(formFile);
                Console.WriteLine($"✅ Comentario {comment.Id} migrado");
            }
        }

        await context.SaveChangesAsync();
        Console.WriteLine("🎉 Migración completada");
    }
}

// Helper para convertir FileStream a IFormFile
public class FormFileWrapper : IFormFile
{
    private readonly Stream _stream;
    private readonly string _fileName;

    public FormFileWrapper(Stream stream, string fileName)
    {
        _stream = stream;
        _fileName = fileName;
    }

    public string ContentType => "image/jpeg";
    public string ContentDisposition => "";
    public IHeaderDictionary Headers => new HeaderDictionary();
    public long Length => _stream.Length;
    public string Name => _fileName;
    public string FileName => _fileName;
    public void CopyTo(Stream target) => _stream.CopyTo(target);
    public async Task CopyToAsync(Stream target, CancellationToken ct = default) => await _stream.CopyToAsync(target, ct);
    public Stream OpenReadStream() => _stream;
}