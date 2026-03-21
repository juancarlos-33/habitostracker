using HabitTrackerApp.Data;
using HabitTrackerApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using HabitTrackerApp.Hubs;

namespace HabitTrackerApp.Controllers
{
    [Authorize]
    public class PostController : Controller
    {
        private readonly HabitDbContext _context;

        private readonly IHubContext<ChatHub> _hubContext;

        private readonly IWebHostEnvironment _environment;

        public PostController(HabitDbContext context, IWebHostEnvironment environment, IHubContext<ChatHub> hubContext)
        {
            _context = context;
            _environment = environment;
            _hubContext = hubContext;
        }

        public IActionResult DeleteComment(int id)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var comment = _context.PostComments.FirstOrDefault(c => c.Id == id);

            if (comment == null)
                return NotFound();

            var post = _context.Posts.FirstOrDefault(p => p.Id == comment.PostId);

            // permitir eliminar si es el dueño del comentario o el dueño del post
            if (comment.UserId != userId && post.UserId != userId)
                return Unauthorized();

            _context.PostComments.Remove(comment);
            _context.SaveChanges();

            return RedirectToAction("Comments", new { postId = comment.PostId });
        }

        public IActionResult EditComment(int id)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var comment = _context.PostComments.FirstOrDefault(c => c.Id == id);

            if (comment == null)
                return NotFound();

            if (comment.UserId != userId)
                return Unauthorized();

            return View(comment);
        }

        public IActionResult DeleteReply(int id)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var reply = _context.CommentReplies.FirstOrDefault(r => r.Id == id);

            if (reply == null)
                return NotFound();

            if (reply.UserId != userId)
                return Unauthorized();

            _context.CommentReplies.Remove(reply);
            _context.SaveChanges();

            return RedirectToAction("Index");
        }

        public IActionResult EditReply(int id)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var reply = _context.CommentReplies.FirstOrDefault(r => r.Id == id);

            if (reply == null)
                return NotFound();

            if (reply.UserId != userId)
                return Unauthorized();

            return View(reply);
        }

        [HttpPost]
        public IActionResult UpdateReply(CommentReply updatedReply)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var reply = _context.CommentReplies.FirstOrDefault(r => r.Id == updatedReply.Id);

            if (reply == null)
                return NotFound();

            if (reply.UserId != userId)
                return Unauthorized();

            reply.Text = updatedReply.Text;

            _context.SaveChanges();

            return RedirectToAction("Index");
        }

        [HttpPost]
        public IActionResult UpdateComment(PostComment updatedComment)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var comment = _context.PostComments.FirstOrDefault(c => c.Id == updatedComment.Id);

            if (comment == null)
                return NotFound();

            if (comment.UserId != userId)
                return Unauthorized();

            comment.Comment = updatedComment.Comment;

            _context.SaveChanges();

            return RedirectToAction("Comments", new { postId = comment.PostId });
        }

        public IActionResult Index()
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var currentUser = _context.Users.FirstOrDefault(u => u.Id == userId);

            if (currentUser == null)
            {
                return RedirectToAction("Login", "Account");
            }

            var posts = _context.Posts
                .Include(p => p.User)
                .Where(p => currentUser.Role == "Admin"
                         || currentUser.Role == "SuperAdmin"
                         || _context.PostReports.Count(r => r.PostId == p.Id) < 5)
                .OrderByDescending(p => p.CreatedAt)
                .ToList();

            return View(posts);
        }

        [HttpGet]
        public IActionResult Report(int postId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var report = new PostReport
            {
                PostId = postId,
                ReportedByUserId = userId,
                Reason = "Contenido inapropiado",
                CreatedAt = DateTime.Now
            };

            _context.PostReports.Add(report);
            _context.SaveChanges();

            TempData["Message"] = "Publicación reportada correctamente.";

            return RedirectToAction("Index");
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        public IActionResult LikePost(int postId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var alreadyLiked = _context.PostLikes
                .FirstOrDefault(l => l.PostId == postId && l.UserId == userId);

            if (alreadyLiked == null)
            {
                var like = new PostLike
                {
                    PostId = postId,
                    UserId = userId
                };

                _context.PostLikes.Add(like);
                _context.SaveChanges();
            }

            return Ok();
        }

        [HttpPost]
        public IActionResult SavePost([FromBody] int postId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var alreadySaved = _context.SavedPosts
                .FirstOrDefault(s => s.PostId == postId && s.UserId == userId);

            if (alreadySaved == null)
            {
                var saved = new SavedPost
                {
                    PostId = postId,
                    UserId = userId,
                    CreatedAt = DateTime.Now
                };

                _context.SavedPosts.Add(saved);
                _context.SaveChanges();
            }

            return Ok();
        }

        [HttpPost]
        public async Task<IActionResult> Create(string description, IFormFile image)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var username = User.Identity.Name;

            string imagePath = null;

            bool isSensitive = false;

            if (image != null && image.Length > 0)
            {
                // detección automática de contenido sospechoso
                var fileNameLower = image.FileName.ToLower();

                string[] suspiciousWords =
                {
            "porn",
            "sex",
            "xxx",
            "nude",
            "nsfw",
            "18+",
            "hentai"
        };

                foreach (var word in suspiciousWords)
                {
                    if (fileNameLower.Contains(word))
                    {
                        isSensitive = true;
                        break;
                    }
                }

                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(image.FileName);

                var folderPath = Path.Combine(_environment.WebRootPath, "posts");

                // 🔹 seguridad: crear carpeta si no existe
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                var path = Path.Combine(folderPath, fileName);

                using (var stream = new FileStream(path, FileMode.Create))
                {
                    await image.CopyToAsync(stream);
                }

                imagePath = "/posts/" + fileName;
            }

            var post = new Post
            {
                UserId = userId,
                Username = username,
                Description = description,
                ImagePath = imagePath,
                CreatedAt = DateTime.Now,
                IsSensitive = isSensitive
            };

            _context.Posts.Add(post);
            _context.SaveChanges();

            return RedirectToAction("Index");
        }
        public IActionResult Delete(int id)
        {
            var post = _context.Posts.FirstOrDefault(p => p.Id == id);

            if (post == null)
                return NotFound();

            var userId = int.Parse(User.FindFirst("UserId").Value);

            if (post.UserId != userId)
                return Unauthorized();

            _context.Posts.Remove(post);
            _context.SaveChanges();

            return RedirectToAction("Index");
        }

        [HttpPost]
        public IActionResult ReplyComment(int commentId, string text)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var username = User.Identity.Name;

            var reply = new CommentReply
            {
                CommentId = commentId,
                UserId = userId,
                Username = username,
                Text = text,
                CreatedAt = DateTime.Now
            };

            _context.CommentReplies.Add(reply);
            _context.SaveChanges();

            return RedirectToAction("Index");
        }

        public IActionResult Comments(int postId)
        {
            var comments = _context.PostComments
                .Where(c => c.PostId == postId)
                .OrderByDescending(c => c.CreatedAt)
                .ToList();

            ViewBag.PostId = postId;

            return View(comments);
        }
        [HttpPost]
        public async Task<IActionResult> AddComment(int postId, string comment, IFormFile image)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var username = User.Identity.Name;

            string imagePath = null;

            // 📷 GUARDAR IMAGEN SI EXISTE
            if (image != null && image.Length > 0)
            {
                var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot/comments");

                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(image.FileName);
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await image.CopyToAsync(stream);
                }

                imagePath = "/comments/" + fileName;
            }

            var newComment = new PostComment
            {
                PostId = postId,
                UserId = userId,
                Username = username,
                Comment = comment,
                ImagePath = imagePath,
                CreatedAt = DateTime.Now
            };

            _context.PostComments.Add(newComment);

            var post = _context.Posts.FirstOrDefault(p => p.Id == postId);

            if (post != null && post.UserId != userId)
            {
                var user = _context.Users.FirstOrDefault(u => u.Id == userId);

                var notification = new Notification
                {
                    UserId = post.UserId,
                    FromUserId = userId,
                    FromUsername = username,
                    FromUserImage = user?.ProfileImage ?? "",
                    Message = username + " comentó tu publicación",
                    Link = "/Post/Comments?postId=" + postId, // 🔥 CLAVE PARA REDIRECCIÓN
                    IsRead = false,
                    CreatedAt = DateTime.Now
                };

                _context.Notifications.Add(notification);

                // 🔔 TIEMPO REAL (CON DATOS COMPLETOS)
                await _hubContext.Clients.Group(post.UserId.ToString())
      .SendAsync(
          "ReceiveNotification",
          userId,
          username + " comentó tu publicación",
          username,
          user?.ProfileImage ?? "",
          "/Post/Comments?postId=" + postId
      );
            }

            await _context.SaveChangesAsync();

            return RedirectToAction("Comments", new { postId = postId });
        }

        public IActionResult SavedPosts()
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var posts = _context.SavedPosts
                .Where(s => s.UserId == userId)
                .Include(s => s.Post)
                .ThenInclude(p => p.User)
                .Select(s => s.Post)
                .OrderByDescending(p => p.CreatedAt)
                .ToList();

            return View(posts);
        }
    }
}