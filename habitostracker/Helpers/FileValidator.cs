using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HabitTrackerApp.Helpers
{
    /// <summary>
    /// Validación de archivos subidos. Verifica magic bytes (firma del archivo)
    /// además de la extensión y el Content-Type, evitando que un atacante suba
    /// HTML, scripts u otros ejecutables disfrazados de imagen/video/audio.
    /// </summary>
    public static class FileValidator
    {
        // Tipos permitidos por categoría
        public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".gif", ".webp"
        };

        public static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp4", ".webm", ".mov", ".m4v"
        };

        public static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3", ".wav", ".ogg", ".m4a", ".webm", ".aac"
        };

        // Tamaño máximo por defecto (MB)
        public const int MaxImageMb = 10;
        public const int MaxVideoMb = 100;
        public const int MaxAudioMb = 25;

        public enum FileKind { Image, Video, Audio, ImageOrVideo, Any }

        public class ValidationResult
        {
            public bool IsValid { get; set; }
            public string Error { get; set; }
            public string DetectedExtension { get; set; }
            public bool IsImage { get; set; }
            public bool IsVideo { get; set; }
            public bool IsAudio { get; set; }
        }

        /// <summary>
        /// Valida un IFormFile. Retorna IsValid=false con Error claro si algo falla.
        /// </summary>
        public static ValidationResult Validate(IFormFile file, FileKind expected, int? maxMb = null)
        {
            var result = new ValidationResult();
            if (file == null || file.Length == 0)
            {
                result.Error = "Archivo vacío.";
                return result;
            }

            var ext = (Path.GetExtension(file.FileName) ?? "").ToLower();

            // 1. Validar extensión declarada
            bool extOk = expected switch
            {
                FileKind.Image => ImageExtensions.Contains(ext),
                FileKind.Video => VideoExtensions.Contains(ext),
                FileKind.Audio => AudioExtensions.Contains(ext),
                FileKind.ImageOrVideo => ImageExtensions.Contains(ext) || VideoExtensions.Contains(ext),
                FileKind.Any => ImageExtensions.Contains(ext) || VideoExtensions.Contains(ext) || AudioExtensions.Contains(ext),
                _ => false
            };
            if (!extOk)
            {
                result.Error = $"Extensión '{ext}' no permitida.";
                return result;
            }

            // 2. Validar tamaño
            int limit = maxMb ?? expected switch
            {
                FileKind.Image => MaxImageMb,
                FileKind.Video => MaxVideoMb,
                FileKind.Audio => MaxAudioMb,
                FileKind.ImageOrVideo => MaxVideoMb,
                _ => MaxVideoMb
            };
            if (file.Length > limit * 1024L * 1024L)
            {
                result.Error = $"El archivo excede el límite de {limit} MB.";
                return result;
            }

            // 3. Validar magic bytes (firma binaria)
            byte[] header = new byte[16];
            using (var stream = file.OpenReadStream())
            {
                int read = stream.Read(header, 0, header.Length);
                if (read < 4)
                {
                    result.Error = "Archivo demasiado pequeño para ser válido.";
                    return result;
                }
            }

            var detected = DetectKind(header);
            if (detected == null)
            {
                result.Error = "No se pudo identificar el tipo real del archivo. Posiblemente está corrupto o disfrazado.";
                return result;
            }

            result.IsImage = detected.Value.kind == FileKind.Image;
            result.IsVideo = detected.Value.kind == FileKind.Video;
            result.IsAudio = detected.Value.kind == FileKind.Audio;
            result.DetectedExtension = detected.Value.ext;

            // 4. Cruzar magic bytes con kind esperado
            bool kindMatches = expected switch
            {
                FileKind.Image => result.IsImage,
                FileKind.Video => result.IsVideo,
                FileKind.Audio => result.IsAudio,
                FileKind.ImageOrVideo => result.IsImage || result.IsVideo,
                FileKind.Any => true,
                _ => false
            };
            if (!kindMatches)
            {
                result.Error = $"El contenido real del archivo no coincide con su extensión (.{ext.TrimStart('.')}). Detectado como: {detected.Value.ext}.";
                return result;
            }

            // 5. Cruzar magic bytes con extensión declarada (rechaza .png con bytes de .exe)
            if (!ExtMatchesMagic(ext, detected.Value.ext))
            {
                result.Error = $"La extensión {ext} no coincide con el contenido real ({detected.Value.ext}).";
                return result;
            }

            result.IsValid = true;
            return result;
        }

        /// <summary>
        /// Detecta tipo real por magic bytes. Retorna null si desconocido.
        /// </summary>
        private static (FileKind kind, string ext)? DetectKind(byte[] h)
        {
            if (h.Length < 4) return null;

            // JPEG: FF D8 FF
            if (h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF)
                return (FileKind.Image, "jpg");

            // PNG: 89 50 4E 47 0D 0A 1A 0A
            if (h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47)
                return (FileKind.Image, "png");

            // GIF: 47 49 46 38
            if (h[0] == 0x47 && h[1] == 0x49 && h[2] == 0x46 && h[3] == 0x38)
                return (FileKind.Image, "gif");

            // WEBP: RIFF .... WEBP
            if (h.Length >= 12 && h[0] == 0x52 && h[1] == 0x49 && h[2] == 0x46 && h[3] == 0x46
                && h[8] == 0x57 && h[9] == 0x45 && h[10] == 0x42 && h[11] == 0x50)
                return (FileKind.Image, "webp");

            // MP4 / MOV / M4V (ftyp box at offset 4)
            if (h.Length >= 8 && h[4] == 0x66 && h[5] == 0x74 && h[6] == 0x79 && h[7] == 0x70)
                return (FileKind.Video, "mp4");

            // WEBM / Matroska: 1A 45 DF A3
            if (h[0] == 0x1A && h[1] == 0x45 && h[2] == 0xDF && h[3] == 0xA3)
                return (FileKind.Video, "webm");

            // MP3: ID3 (49 44 33) o frame sync FF Fx
            if ((h[0] == 0x49 && h[1] == 0x44 && h[2] == 0x33) ||
                (h[0] == 0xFF && (h[1] & 0xE0) == 0xE0))
                return (FileKind.Audio, "mp3");

            // OGG: 4F 67 67 53
            if (h[0] == 0x4F && h[1] == 0x67 && h[2] == 0x67 && h[3] == 0x53)
                return (FileKind.Audio, "ogg");

            // WAV: RIFF .... WAVE
            if (h.Length >= 12 && h[0] == 0x52 && h[1] == 0x49 && h[2] == 0x46 && h[3] == 0x46
                && h[8] == 0x57 && h[9] == 0x41 && h[10] == 0x56 && h[11] == 0x45)
                return (FileKind.Audio, "wav");

            return null;
        }

        private static bool ExtMatchesMagic(string ext, string detected)
        {
            ext = ext.TrimStart('.').ToLower();
            // Mapeo flexible: jpg/jpeg, mp4/mov/m4v, etc.
            if (ext == detected) return true;
            if (ext == "jpeg" && detected == "jpg") return true;
            if ((ext == "mov" || ext == "m4v" || ext == "mp4") && detected == "mp4") return true;
            if (ext == "m4a" && detected == "mp4") return true; // m4a usa contenedor mp4
            if (ext == "aac" && detected == "mp3") return true;
            if (ext == "webm" && detected == "webm") return true;
            if (ext == "webm" && detected == "ogg") return true; // contenedor variante
            return false;
        }
    }
}
