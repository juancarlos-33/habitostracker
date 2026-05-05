using HabitTrackerApp.Data;
using HabitTrackerApp.Models;
using HabitTrackerApp.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HabitTrackerApp.Controllers
{
    [Authorize]
    public class StoryController : Controller
    {
        private readonly HabitDbContext _context;
        private readonly CloudinaryService _cloudinary;

        public StoryController(HabitDbContext context, CloudinaryService cloudinary)
        {
            _context = context;
            _cloudinary = cloudinary;
        }

        [HttpGet]
        public async Task<IActionResult> GetFriendsStories()
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);

            var friendIds = await _context.FriendRequests
                .Where(f => (f.SenderId == myId || f.ReceiverId == myId) && f.Status == "Accepted")
                .Select(f => f.SenderId == myId ? f.ReceiverId : f.SenderId)
                .ToListAsync();

            friendIds.Add(myId);

            var stories = await _context.Stories
                .Include(s => s.User)
                .Include(s => s.Views)
                .Include(s => s.Likes)
                .Where(s => (
                    (friendIds.Contains(s.UserId) && (s.Visibility == "friends" || s.Visibility == "public")) ||
                    s.UserId == myId ||
                    s.Visibility == "public"
                ) && s.ExpiresAt > DateTime.Now)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            var grouped = stories
                .GroupBy(s => s.UserId)
                .Select(g => new {
                    user = new
                    {
                        id = g.First().User.Id,
                        username = g.First().User.Username,
                        profileImage = g.First().User.ProfileImage,
                        profilePicture = g.First().User.ProfilePicture
                    },
                    stories = g.Select(s => new {
                        s.Id,
                        s.Type,
                        s.MediaUrl,
                        s.TextContent,
                        s.BgColor,
                        s.Duration,
                        s.IsHighlight,
                        s.CreatedAt,
                        s.Caption,
                        s.Visibility,
                        views = s.Views.Count,
                        viewed = s.Views.Any(v => v.ViewerId == myId),
                        likes = s.Likes.Count,
                        liked = s.Likes.Any(l => l.UserId == myId)
                    }).ToList(),
                    hasUnviewed = g.Any(s => !s.Views.Any(v => v.ViewerId == myId))
                }).ToList();

            return Json(grouped);
        }

        [HttpGet]
        public async Task<IActionResult> GetViews(int storyId)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);
            var story = await _context.Stories.FirstOrDefaultAsync(s => s.Id == storyId && s.UserId == myId);
            if (story == null) return Forbid();

            var views = await _context.StoryViews
                .Include(v => v.Viewer)
                .Where(v => v.StoryId == storyId)
                .OrderByDescending(v => v.ViewedAt)
                .ToListAsync();

            var result = views.Select(v => new {
                username = v.Viewer.Username,
                profileImage = v.Viewer.ProfileImage ?? v.Viewer.ProfilePicture ?? "",
                viewedAt = GetTimeAgoStory(v.ViewedAt),
                liked = _context.StoryLikes.Any(l => l.StoryId == storyId && l.UserId == v.ViewerId)
            }).ToList();

            return Json(result);
        }

        private string GetTimeAgoStory(DateTime date)
        {
            var diff = (int)(DateTime.Now - date).TotalMinutes;
            if (diff < 1) return "justo ahora";
            if (diff < 60) return $"hace {diff} min";
            return $"a las {date:hh:mm tt}";
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);

            var friendIds = await _context.FriendRequests
                .Where(f => (f.SenderId == myId || f.ReceiverId == myId) && f.Status == "Accepted")
                .Select(f => f.SenderId == myId ? f.ReceiverId : f.SenderId)
                .ToListAsync();

            friendIds.Add(myId);

            var stories = await _context.Stories
                .Include(s => s.User)
                .Include(s => s.Views)
                .Where(s => friendIds.Contains(s.UserId) && s.ExpiresAt > DateTime.Now)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            var grouped = stories
                .GroupBy(s => s.UserId)
                .Select(g => new {
                    User = g.First().User,
                    Stories = g.ToList(),
                    HasUnviewed = g.Any(s => !s.Views.Any(v => v.ViewerId == myId))
                }).ToList();

            ViewBag.Grouped = grouped;
            ViewBag.MyId = myId;
            return View();
        }

        [HttpGet]
        public IActionResult Create() => View();

        [RequestSizeLimit(104857600)]
        [RequestFormLimits(MultipartBodyLengthLimit = 104857600)]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string type, string? textContent, string? bgColor, string? caption, string? visibility, double trimEnd = 30, IFormFile? media = null)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);

            var story = new Story
            {
                UserId = myId,
                Type = type,
                BgColor = bgColor ?? "#6366f1",
                Caption = caption,
                Visibility = visibility ?? "friends",
                CreatedAt = DateTime.Now,
                ExpiresAt = DateTime.Now.AddHours(24)
            };

            if (type == "text")
            {
                story.TextContent = textContent;
                story.Duration = 7;
            }
            else if (media != null && media.Length > 0)
            {
                string url;
                if (type == "video")
                {
                    url = await _cloudinary.UploadVideoAsync(media, "habitostracker/stories/videos", (int)Math.Min(trimEnd, 30));
                    story.Duration = (int)Math.Min(trimEnd, 30);
                }
                else
                {
                    url = await _cloudinary.UploadImageAsync(media, "habitostracker/stories/images");
                    story.Duration = 7;

                    var modResult = await _cloudinary.CheckImageModeration(url);
                    if (modResult == "explicit")
                    {
                        await _cloudinary.DeleteImageAsync(url);
                        TempData["Error"] = "La imagen contiene contenido explícito y no puede publicarse.";
                        return RedirectToAction("Index", "Habit");
                    }
                    story.IsSensitive = modResult == "sensitive";
                }
                story.MediaUrl = url;
            }

            _context.Stories.Add(story);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Historia publicada ✅";
            return RedirectToAction("Index", "Habit");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateFromUrl(string type, string? textContent, string? bgColor, string? caption, string? visibility, double trimEnd = 30, string? mediaUrl = null)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);

            var story = new Story
            {
                UserId = myId,
                Type = type,
                BgColor = bgColor ?? "#6366f1",
                Caption = caption,
                Visibility = visibility ?? "friends",
                MediaUrl = mediaUrl,
                CreatedAt = DateTime.Now,
                ExpiresAt = DateTime.Now.AddHours(24)
            };

            if (type == "text")
            {
                story.Duration = 7;
            }
            else if (type == "video")
            {
                story.Duration = (int)Math.Min(trimEnd, 30);

                // 🔥 moderación IA via thumbnail
                if (!string.IsNullOrEmpty(mediaUrl))
                {
                    var thumbnailUrl = System.Text.RegularExpressions.Regex.Replace(
                        mediaUrl, @"\.(mp4|webm|mov|avi)$", ".jpg",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    var modResult = await _cloudinary.CheckImageModeration(thumbnailUrl);
                    if (modResult == "explicit")
                    {
                        await _cloudinary.DeleteImageAsync(mediaUrl);
                        return BadRequest("El video contiene contenido explícito y no puede publicarse.");
                    }
                    story.IsSensitive = modResult == "sensitive";
                }
            }
            else
            {
                story.Duration = 7;

                // 🔥 moderación IA para imágenes
                if (!string.IsNullOrEmpty(mediaUrl))
                {
                    var modResult = await _cloudinary.CheckImageModeration(mediaUrl);
                    if (modResult == "explicit")
                    {
                        await _cloudinary.DeleteImageAsync(mediaUrl);
                        return BadRequest("La imagen contiene contenido explícito y no puede publicarse.");
                    }
                    story.IsSensitive = modResult == "sensitive";
                }
            }

            _context.Stories.Add(story);
            await _context.SaveChangesAsync();

            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> View([FromBody] int storyId)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);

            var story = await _context.Stories.FirstOrDefaultAsync(s => s.Id == storyId);
            if (story == null) return NotFound();
            if (story.UserId == myId) return Ok();

            var already = await _context.StoryViews
                .AnyAsync(v => v.StoryId == storyId && v.ViewerId == myId);

            if (!already)
            {
                _context.StoryViews.Add(new StoryView
                {
                    StoryId = storyId,
                    ViewerId = myId,
                    ViewedAt = DateTime.Now
                });
                await _context.SaveChangesAsync();
            }

            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> ToggleLike([FromBody] int storyId)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);

            var existing = await _context.StoryLikes
                .FirstOrDefaultAsync(l => l.StoryId == storyId && l.UserId == myId);

            if (existing != null)
            {
                _context.StoryLikes.Remove(existing);
                await _context.SaveChangesAsync();
                return Json(new { liked = false });
            }
            else
            {
                _context.StoryLikes.Add(new StoryLike
                {
                    StoryId = storyId,
                    UserId = myId,
                    CreatedAt = DateTime.Now
                });
                await _context.SaveChangesAsync();
                if (existing == null) // Solo notificar al dar like, no al quitar
                {
                    var story2 = await _context.Stories.FirstOrDefaultAsync(s => s.Id == storyId);
                    var liker = await _context.Users.FirstOrDefaultAsync(u => u.Id == myId);
                    if (story2 != null && story2.UserId != myId)
                    {
                        _context.Notifications.Add(new Notification
                        {
                            UserId = story2.UserId,
                            Message = $"{liker.Username} le dio ❤️ a tu historia",
                            CreatedAt = DateTime.Now,
                            IsRead = false
                        });
                        await _context.SaveChangesAsync();
                    }
                }
                return Json(new { liked = true });
            }
        }

        [HttpPost]
        public async Task<IActionResult> Delete([FromBody] int id)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);
            var story = await _context.Stories.FirstOrDefaultAsync(s => s.Id == id && s.UserId == myId);
            if (story == null) return NotFound();
            _context.Stories.Remove(story);
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> ToggleHighlight([FromBody] int id)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);
            var story = await _context.Stories.FirstOrDefaultAsync(s => s.Id == id && s.UserId == myId);
            if (story == null) return NotFound();
            story.IsHighlight = !story.IsHighlight;
            await _context.SaveChangesAsync();
            return Json(new { isHighlight = story.IsHighlight });
        }

        [HttpGet]
        public async Task<IActionResult> GetUserStories(int userId)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);

            var stories = await _context.Stories
                .Include(s => s.Views)
                .Include(s => s.Likes)
                .Where(s => s.UserId == userId && s.ExpiresAt > DateTime.Now)
                .OrderBy(s => s.CreatedAt)
                .Select(s => new {
                    s.Id,
                    s.Type,
                    s.MediaUrl,
                    s.TextContent,
                    s.BgColor,
                    s.Duration,
                    s.IsHighlight,
                    s.Caption,
                    s.Visibility,
                    viewed = s.Views.Any(v => v.ViewerId == myId),
                    views = s.Views.Count,
                    likes = s.Likes.Count,
                    liked = s.Likes.Any(l => l.UserId == myId)
                })
                .ToListAsync();

            return Json(stories);
        }
    }
}