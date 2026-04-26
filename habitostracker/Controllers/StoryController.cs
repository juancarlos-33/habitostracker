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
                .Where(s => friendIds.Contains(s.UserId) && s.ExpiresAt > DateTime.Now)
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
                        views = s.Views.Count,
                        viewed = s.Views.Any(v => v.ViewerId == myId)
                    }).ToList(),
                    hasUnviewed = g.Any(s => !s.Views.Any(v => v.ViewerId == myId))
                }).ToList();

            return Json(grouped);
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string type, string? textContent, string? bgColor, string? caption, IFormFile? media)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);

            var story = new Story
            {
                UserId = myId,
                Type = type,
                BgColor = bgColor ?? "#6366f1",
                Caption = caption,
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
                var folder = type == "video" ? "habitostracker/stories/videos" : "habitostracker/stories/images";
                var url = await _cloudinary.UploadImageAsync(media, folder);
                story.MediaUrl = url;
                story.Duration = type == "video" ? 30 : 7;
            }

            _context.Stories.Add(story);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Historia publicada ✅";
            return RedirectToAction("Index", "Habit");
        }

        [HttpPost]
        public async Task<IActionResult> View(int storyId)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);

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
        public async Task<IActionResult> Delete(int id)
        {
            var myId = int.Parse(User.FindFirst("UserId").Value);
            var story = await _context.Stories.FirstOrDefaultAsync(s => s.Id == id && s.UserId == myId);
            if (story == null) return NotFound();
            _context.Stories.Remove(story);
            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> ToggleHighlight(int id)
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
                    viewed = s.Views.Any(v => v.ViewerId == myId),
                    views = s.Views.Count
                })
                .ToListAsync();

            return Json(stories);
        }
    }
}