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
        private readonly CloudinaryService _cloudinaryService;

        public PostController(HabitDbContext context, IWebHostEnvironment environment, IHubContext<ChatHub> hubContext, CloudinaryService cloudinaryService)
        {
            _context = context;
            _environment = environment;
            _hubContext = hubContext;
            _cloudinaryService = cloudinaryService;
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
                return RedirectToAction("Login", "Account");

            // 🔥 Posts con usuario incluido
            var posts = _context.Posts
                .Include(p => p.User)
                .Where(p => currentUser.Role == "Admin" || currentUser.Role == "SuperAdmin" ||
                       !_context.PostReports.Any(r => r.PostId == p.Id && _context.PostReports.Count(r2 => r2.PostId == p.Id) >= 5))
                .OrderByDescending(p => p.CreatedAt)
                .ToList();

            var postIds = posts.Select(p => p.Id).ToList();

            // 🔥 Comentarios en una sola query
            var commentCounts = _context.PostComments
                .Where(c => postIds.Contains(c.PostId))
                .GroupBy(c => c.PostId)
                .ToDictionary(g => g.Key, g => g.Count());

            // 🔥 Replies en una sola query
            var commentIds = _context.PostComments
                .Where(c => postIds.Contains(c.PostId))
                .Select(c => new { c.Id, c.PostId })
                .ToList();

            var replyCountsByComment = _context.CommentReplies
                .Where(r => commentIds.Select(c => c.Id).Contains(r.CommentId))
                .GroupBy(r => r.CommentId)
                .ToDictionary(g => g.Key, g => g.Count());

            // Sumar replies a comentarios
            foreach (var postId in postIds)
            {
                var commentsOfPost = commentIds.Where(c => c.PostId == postId).Select(c => c.Id).ToList();
                var replyCount = commentsOfPost.Sum(cId => replyCountsByComment.ContainsKey(cId) ? replyCountsByComment[cId] : 0);
                if (commentCounts.ContainsKey(postId))
                    commentCounts[postId] += replyCount;
                else
                    commentCounts[postId] = replyCount;
            }

            ViewBag.CommentCounts = commentCounts;

            // 🔥 Likes del usuario
            var myLikes = _context.PostLikes
                .Where(l => l.UserId == userId)
                .Select(l => l.PostId)
                .ToList();

            ViewBag.MyLikes = myLikes;

            // 🔥 Contador de likes
            var postLikes = _context.PostLikes
                .Where(l => postIds.Contains(l.PostId))
                .GroupBy(l => l.PostId)
                .ToDictionary(g => g.Key, g => g.Count());

            ViewBag.PostLikes = postLikes;

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


        [HttpGet]
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
        [RequestSizeLimit(524288000)]
        [RequestFormLimits(MultipartBodyLengthLimit = 524288000)]
        public async Task<IActionResult> Create(string description, IFormFile image)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var username = User.Identity.Name;

            string mediaPath = null;
            bool isSensitive = false;

            if (image != null && image.Length > 0)
            {
                var fileNameLower = image.FileName.ToLower();
                string[] suspiciousWords = { "porn", "sex", "xxx", "nude", "nsfw", "18+", "hentai" };
                foreach (var word in suspiciousWords)
                {
                    if (fileNameLower.Contains(word)) { isSensitive = true; break; }
                }

                try
                {
                    bool isVideo = image.ContentType.StartsWith("video");

                    if (isVideo)
                        mediaPath = await _cloudinaryService.UploadVideoAsync(image);
                    else
                        mediaPath = await _cloudinaryService.UploadImageAsync(image);
                }
                catch (Exception ex)
                {
                    TempData["Error"] = "Error al subir: " + ex.Message;
                    return RedirectToAction("Create");
                }
            }

            if (string.IsNullOrWhiteSpace(description) && mediaPath == null)
            {
                TempData["Error"] = "Debes escribir algo o subir una imagen/video.";
                return RedirectToAction("Create");
            }

            var post = new Post
            {
                UserId = userId,
                Username = username,
                Description = description,
                ImagePath = mediaPath,
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
            if (post == null) return NotFound();

            var userId = int.Parse(User.FindFirst("UserId").Value);

            // 🔥 verificar rol desde la BD, no desde la cookie
            var currentUser = _context.Users.FirstOrDefault(u => u.Id == userId);
            var isAdmin = currentUser?.Role == "Admin" || currentUser?.Role == "SuperAdmin";

            if (post.UserId != userId && !isAdmin)
                return Unauthorized();

            var reports = _context.PostReports.Where(r => r.PostId == id);
            _context.PostReports.RemoveRange(reports);

            _context.Posts.Remove(post);
            _context.SaveChanges();

            // 🔥 solo redirigir a Reports si es admin Y viene de Reports
            if (isAdmin && Request.Headers["Referer"].ToString().Contains("Reports"))
                return RedirectToAction("Reports", "Admin");

            return RedirectToAction("Index");
        }
        public IActionResult Details(int id)
        {
            var post = _context.Posts
                .Include(p => p.User)
                .FirstOrDefault(p => p.Id == id);

            if (post == null)
                return NotFound();

            return View(post);
        }

        [HttpPost]
        public async Task<IActionResult> ReplyComment(int commentId, string text, int? parentReplyId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            // 🔥 TRAER USUARIO
            var user = await _context.Users.FindAsync(userId);

            // ❌ VALIDACIÓN
            if (string.IsNullOrWhiteSpace(text))
            {
                var c = _context.PostComments.FirstOrDefault(x => x.Id == commentId);
                return RedirectToAction("Comments", new { postId = c.PostId });
            }

            // 🔥 CREAR RESPUESTA (AHORA SOPORTA SUB-RESPUESTAS)
            var reply = new CommentReply
            {
                CommentId = commentId,
                UserId = userId,
                Username = user.Username,
                ProfileImage = user.ProfileImage ?? "",
                Text = text,
                CreatedAt = DateTime.Now,
                ParentReplyId = parentReplyId // 🔥 CLAVE
            };

            _context.CommentReplies.Add(reply);
            await _context.SaveChangesAsync();

            var comment = _context.PostComments.FirstOrDefault(c => c.Id == commentId);

            return RedirectToAction("Comments", new { postId = comment.PostId });
        }

        [HttpPost]
        public async Task<IActionResult> ToggleReplyLike(int id)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var existingLike = await _context.CommentReplyLikes
                .FirstOrDefaultAsync(x => x.ReplyId == id && x.UserId == userId);

            bool liked;

            if (existingLike != null)
            {
                _context.CommentReplyLikes.Remove(existingLike);
                liked = false;
            }
            else
            {
                var like = new CommentReplyLike
                {
                    ReplyId = id,
                    UserId = userId
                };

                _context.CommentReplyLikes.Add(like);
                liked = true;
            }

            await _context.SaveChangesAsync();

            var count = await _context.CommentReplyLikes
                .CountAsync(x => x.ReplyId == id);

            return Json(new { liked, count });
        }
        [HttpGet]
        public IActionResult Comments(int postId)
        {
            var comments = _context.PostComments
                .Where(c => c.PostId == postId)
                .OrderByDescending(c => c.CreatedAt)
                .ToList();

            ViewBag.PostId = postId;

            var userId = int.Parse(User.FindFirst("UserId").Value);

            // ❤️ likes comentarios
            ViewBag.CommentLikes = _context.CommentLikes
                .GroupBy(x => x.CommentId)
                .ToDictionary(g => g.Key, g => g.Count());

            ViewBag.MyCommentLikes = _context.CommentLikes
                .Where(x => x.UserId == userId)
                .Select(x => x.CommentId)
                .ToList();

            // ❤️ likes replies
            ViewBag.ReplyLikes = _context.CommentReplyLikes
                .GroupBy(x => x.ReplyId)
                .ToDictionary(g => g.Key, g => g.Count());

            ViewBag.MyReplyLikes = _context.CommentReplyLikes
                .Where(x => x.UserId == userId)
                .Select(x => x.ReplyId)
                .ToList();

            return View(comments);
        }

        [HttpPost]
        public async Task<IActionResult> AddComment(int postId, string comment, IFormFile image)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);
            var username = User.Identity.Name;
            var user = await _context.Users.FindAsync(userId);

            string imagePath = null;

            if (string.IsNullOrWhiteSpace(comment) && (image == null || image.Length == 0))
            {
                TempData["Error"] = "Debes escribir algo o subir una imagen.";
                return RedirectToAction("Comments", new { postId = postId });
            }

            if (image != null && image.Length > 0)
                imagePath = await _cloudinaryService.UploadImageAsync(image);

            var newComment = new PostComment
            {
                PostId = postId,
                UserId = userId,
                Username = user.Username,
                ProfileImage = user.ProfileImage ?? "",
                Comment = comment,
                ImagePath = imagePath,
                CreatedAt = DateTime.Now
            };

            _context.PostComments.Add(newComment);
            await _context.SaveChangesAsync();

            var post = _context.Posts.FirstOrDefault(p => p.Id == postId);

            // 🔔 Notificación al dueño del post
            if (post != null && post.UserId != userId)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = post.UserId,
                    FromUserId = userId,
                    FromUsername = user.Username,
                    FromUserImage = user?.ProfileImage ?? "",
                    Message = user.Username + " comentó tu publicación",
                    Link = "/Post/Comments?postId=" + postId + "&highlight=" + newComment.Id,
                    IsRead = false,
                    CreatedAt = DateTime.Now
                });

                await _hubContext.Clients.Group(post.UserId.ToString())
                    .SendAsync("ReceiveNotification", userId,
                        user.Username + " comentó tu publicación",
                        user.Username, user?.ProfileImage ?? "",
                       "/Post/Comments?postId=" + postId + "&highlight=" + newComment.Id);
            }

            // 🔔 Notificaciones de menciones
            if (!string.IsNullOrEmpty(comment))
            {
                var mentionMatches = System.Text.RegularExpressions.Regex.Matches(
                    comment, @"@([A-Za-z0-9_]+(?: [A-Za-z0-9_]+)*)");

                var mentioned = new HashSet<int>();
                foreach (System.Text.RegularExpressions.Match m in mentionMatches)
                {
                    var mentionedUsername = m.Groups[1].Value;
                    var mentionedUser = _context.Users.FirstOrDefault(u => u.Username == mentionedUsername);
                    if (mentionedUser == null) continue;
                    if (mentionedUser.Id == userId) continue;
                    if (mentioned.Contains(mentionedUser.Id)) continue;
                    mentioned.Add(mentionedUser.Id);

                    _context.Notifications.Add(new Notification
                    {
                        UserId = mentionedUser.Id,
                        FromUserId = userId,
                        FromUsername = user.Username,
                        FromUserImage = user?.ProfileImage ?? "",
                        Message = user.Username + " te mencionó en un comentario",
                        Link = "/Post/Comments?postId=" + postId + "&highlight=" + newComment.Id,
                        IsRead = false,
                        CreatedAt = DateTime.Now
                    });

                    await _hubContext.Clients.Group(mentionedUser.Id.ToString())
                        .SendAsync("ReceiveNotification", userId,
                            user.Username + " te mencionó en un comentario",
                            user.Username, user?.ProfileImage ?? "",
                            "/Post/Comments?postId=" + postId);
                }
            }
            await _context.SaveChangesAsync();

            return RedirectToAction("Comments", new { postId = postId });
        }

        [HttpPost]
        public async Task<IActionResult> ToggleLike(int postId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var existingLike = await _context.PostLikes
                .FirstOrDefaultAsync(x => x.PostId == postId && x.UserId == userId);

            bool liked;

            if (existingLike != null)
            {
                _context.PostLikes.Remove(existingLike);
                liked = false;
            }
            else
            {
                var like = new PostLike
                {
                    PostId = postId,
                    UserId = userId
                };

                _context.PostLikes.Add(like);
                liked = true;
            }

            await _context.SaveChangesAsync();

            var count = await _context.PostLikes
                .CountAsync(x => x.PostId == postId);

            return Json(new { liked = liked, count = count });
        }
        [HttpPost]
        public async Task<IActionResult> ToggleCommentLike(int commentId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var existingLike = await _context.CommentLikes
                .FirstOrDefaultAsync(x => x.CommentId == commentId && x.UserId == userId);

            bool liked;

            if (existingLike != null)
            {
                _context.CommentLikes.Remove(existingLike);
                liked = false;
            }
            else
            {
                var like = new CommentLike
                {
                    CommentId = commentId,
                    UserId = userId
                };

                _context.CommentLikes.Add(like);
                liked = true;
            }

            await _context.SaveChangesAsync();

            var count = await _context.CommentLikes
                .CountAsync(x => x.CommentId == commentId);

            return Json(new { liked = liked, count = count });
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