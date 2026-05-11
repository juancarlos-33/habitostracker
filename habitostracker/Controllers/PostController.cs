using HabitTrackerApp.Data;
using HabitTrackerApp.Helpers;
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
            var friendIds = _context.FriendRequests
       .Where(f => (f.SenderId == userId || f.ReceiverId == userId) && f.Status == "Accepted")
       .Select(f => f.SenderId == userId ? f.ReceiverId : f.SenderId)
       .ToList();

            var isAdmin = currentUser.Role == "Admin" || currentUser.Role == "SuperAdmin";

            var posts = _context.Posts
                .Include(p => p.User)
                .Where(p =>
                    isAdmin ||
                    p.UserId == userId ||
                    (p.Privacy == "Public" && !_context.PostReports.Any(r => r.PostId == p.Id && _context.PostReports.Count(r2 => r2.PostId == p.Id) >= 5)) ||
                    (p.Privacy == "Friends" && friendIds.Contains(p.UserId)) ||
                    (p.Privacy == "Private" && p.UserId == userId)
                )
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
            ViewBag.TotalPosts = posts.Count;
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
                // 🔒 validación real (extensión + magic bytes + tamaño)
                var v = FileValidator.Validate(image, FileValidator.FileKind.ImageOrVideo);
                if (!v.IsValid)
                {
                    TempData["Error"] = v.Error;
                    return RedirectToAction("Create");
                }
                bool isVideo = v.IsVideo;
                try
                {
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

                if (mediaPath != null)
                {
                    string thumbnailUrl = mediaPath;

                    // si es video, obtener thumbnail
                    if (isVideo)
                    {
                        thumbnailUrl = System.Text.RegularExpressions.Regex.Replace(
                            mediaPath, @"\.(mp4|webm|mov|avi)$", ".jpg",
                            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    }

                    var modResult = await _cloudinaryService.CheckImageModeration(thumbnailUrl);
                    if (modResult == "explicit")
                    {
                        if (isVideo)
                            await _cloudinaryService.DeleteImageAsync(mediaPath);
                        else
                            await _cloudinaryService.DeleteImageAsync(mediaPath);

                        TempData["Error"] = isVideo
                            ? "El video contiene contenido explícito y no puede publicarse."
                            : "La imagen contiene contenido explícito y no puede publicarse.";
                        return RedirectToAction("Create");
                    }
                    isSensitive = modResult == "sensitive";
                }
            }

            if (string.IsNullOrWhiteSpace(description) && mediaPath == null)
            {
                TempData["Error"] = "Debes escribir algo o subir una imagen/video.";
                return RedirectToAction("Create");
            }

            var privacy = Request.Form["privacy"].ToString();
            if (privacy != "Friends" && privacy != "Private") privacy = "Public";

            var post = new Post
            {
                UserId = userId,
                Username = username,
                Description = description,
                ImagePath = mediaPath,
                CreatedAt = DateTime.Now,
                IsSensitive = isSensitive,
                Privacy = privacy
            };

            _context.Posts.Add(post);
            _context.SaveChanges();

            return RedirectToAction("Index");
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
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
            var post = _context.Posts.Find(postId);
            if (post == null) return NotFound();

            ViewBag.PostId = postId;
            ViewBag.CommentsDisabled = post.CommentsDisabled;  // ✅ NUEVA LÍNEA

            var comments = _context.PostComments
                .Where(c => c.PostId == postId)
                .OrderByDescending(c => c.CreatedAt)
                .ToList();

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
            // 🔒 VERIFICAR SI COMENTARIOS ESTÁN DESHABILITADOS
            var post = await _context.Posts.FindAsync(postId);
            if (post == null) return NotFound();
            if (post.CommentsDisabled)
            {
                TempData["Error"] = "No se pueden agregar comentarios a esta publicación porque ha sido bloqueada por incumplir las normas.";
                return RedirectToAction("Comments", new { postId });
            }

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

            var postForNotif = _context.Posts.FirstOrDefault(p => p.Id == postId);

            // 🔔 Notificación al dueño del post
            if (postForNotif != null && postForNotif.UserId != userId)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = postForNotif.UserId,
                    FromUserId = userId,
                    FromUsername = user.Username,
                    FromUserImage = user?.ProfileImage ?? "",
                    Message = user.Username + " comentó tu publicación",
                    Link = "/Post/Comments?postId=" + postId + "&highlight=" + newComment.Id,
                    IsRead = false,
                    CreatedAt = DateTime.Now
                });

                await _hubContext.Clients.Group(postForNotif.UserId.ToString())
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

        // 🔹 PASO 3: Comentario automático del BOT + deshabilitar comentarios
        [HttpGet]
        public async Task<IActionResult> WarningJuanEsteban(int postId)
        {
            // 1. Verificar que la publicación existe
            var post = await _context.Posts.FindAsync(postId);
            if (post == null) return NotFound("Publicación no encontrada");

            // 2. Buscar la cuenta BOT por ID (243)
            var botUser = await _context.Users.FindAsync(243);
            if (botUser == null) return NotFound("Cuenta BOT con ID 243 no encontrada.");

            // 3. Verificar si el BOT ya comentó (para no duplicar)
            bool yaComento = await _context.PostComments.AnyAsync(c => c.PostId == postId && c.UserId == botUser.Id);
            if (!yaComento)
            {
                var comment = new PostComment
                {
                    PostId = postId,
                    UserId = botUser.Id,
                    Username = botUser.Username + " ✅",   // Le agregas el chulo visualmente
                    ProfileImage = botUser.ProfileImage ?? "",
                    Comment = @"– INCUMPLIMIENTO DE NORMAS -

Estimado usuario, esta publicación viola las políticas de la comunidad de HabitTracker.  
Por tu rol de administrador no serás sancionado de inmediato, pero esta es tu PRIMERA Y ÚLTIMA ADVERTENCIA.  

Cualquier reincidencia resultará en la suspensión de tu cuenta y tu inclusión en la lista negra** de la plataforma.  
Esta publicación ha sido marcada y los comentarios han sido deshabilitados.  

— Equipo de HabitTracker ✅",
                    CreatedAt = DateTime.Now
                };
                _context.PostComments.Add(comment);
            }

            // 4. Deshabilitar comentarios y marcar como en advertencia
            post.CommentsDisabled = true;
            post.IsUnderWarning = true;

            await _context.SaveChangesAsync();

            return Content("✅ Listo: comentario del BOT agregado y comentarios desactivados.");
        }
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Repost([FromBody] int postId)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            // verificar que el post existe
            var post = await _context.Posts.FindAsync(postId);
            if (post == null) return Json(new { success = false, error = "Post no encontrado" });

            // no repostear tu propio post
            if (post.UserId == userId)
                return Json(new { success = false, error = "No puedes repostear tu propio post" });

            // verificar si ya reposteó
            var existing = _context.Reposts.FirstOrDefault(r => r.UserId == userId && r.PostId == postId);
            if (existing != null)
            {
                // deshacer repost
                _context.Reposts.Remove(existing);
                await _context.SaveChangesAsync();
                return Json(new { success = true, reposted = false });
            }

            _context.Reposts.Add(new Repost
            {
                UserId = userId,
                PostId = postId,
                CreatedAt = DateTime.UtcNow
            });
            await _context.SaveChangesAsync();

            // notificar al dueño
            var user = await _context.Users.FindAsync(userId);
            if (post.UserId != userId)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = post.UserId,
                    FromUserId = userId,
                    FromUsername = user.Username,
                    FromUserImage = user.ProfileImage ?? "",
                    Message = $"{user.Username} reposteó tu publicación",
                    Link = $"/Post/Comments?postId={postId}",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }

            return Json(new { success = true, reposted = true });
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
        [HttpPost]
        public async Task<IActionResult> AddCommentAjax(int postId, string comment, IFormFile image)
        {
            // Verificar si comentarios están deshabilitados
            var post = await _context.Posts.FindAsync(postId);
            if (post == null) return Json(new { success = false, error = "Publicación no encontrada" });
            if (post.CommentsDisabled)
                return Json(new { success = false, error = "Los comentarios están deshabilitados." });

            var userId = int.Parse(User.FindFirst("UserId").Value);
            var user = await _context.Users.FindAsync(userId);

            string imagePath = null;
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

            // Notificar al dueño del post (si no es el mismo)
            if (post.UserId != userId)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = post.UserId,
                    FromUserId = userId,
                    FromUsername = user.Username,
                    FromUserImage = user.ProfileImage ?? "",
                    Message = $"{user.Username} comentó tu publicación",
                    Link = $"/Post/Details/{postId}#comment-{newComment.Id}",
                    IsRead = false,
                    CreatedAt = DateTime.Now
                });
                await _hubContext.Clients.Group(post.UserId.ToString())
                    .SendAsync("ReceiveNotification", userId, $"{user.Username} comentó tu publicación", user.Username, user.ProfileImage ?? "", $"/Post/Details/{postId}#comment-{newComment.Id}");
            }

            // 🔥 Notificar a todos los usuarios que están viendo esta publicación en tiempo real
            await _hubContext.Clients.Group($"post-{postId}")
                .SendAsync("NewComment", new
                {
                    newComment.Id,
                    newComment.UserId,
                    newComment.Username,
                    newComment.ProfileImage,
                    newComment.Comment,
                    newComment.ImagePath,
                    CreatedAt = DateTime.Now.ToString("dd MMM yyyy · hh:mm tt"),
                    IsMine = false,
                    LikeCount = 0,
                    IsLiked = false
                });

            // Devolver el comentario creado para agregarlo al modal del remitente
            var commentDto = new
            {
                newComment.Id,
                newComment.UserId,
                newComment.Username,
                newComment.ProfileImage,
                newComment.Comment,
                newComment.ImagePath,
                CreatedAt = newComment.CreatedAt.ToString("dd MMM yyyy · hh:mm tt"),
                IsMine = true,
                LikeCount = 0,
                IsLiked = false
            };

            return Json(new { success = true, comment = commentDto });
        }
        [HttpGet]
        [HttpGet]
        public async Task<IActionResult> GetComments(int postId)
        {
            try
            {
                var post = await _context.Posts.FindAsync(postId);
                if (post == null) return Json(new { success = false, error = "Publicación no encontrada" });

                var currentUserId = int.Parse(User.FindFirst("UserId").Value);

                var comments = await _context.PostComments
                    .Where(c => c.PostId == postId)
                    .OrderByDescending(c => c.CreatedAt)
                    .Select(c => new
                    {
                        c.Id,
                        UserId = c.UserId,
                        Username = c.Username,
                        ProfileImage = c.ProfileImage ?? "",
                        Comment = c.Comment,
                        ImagePath = c.ImagePath,
                        CreatedAt = c.CreatedAt.ToString("dd MMM yyyy · hh:mm tt"),
                        IsMine = c.UserId == currentUserId,
                        LikeCount = _context.CommentLikes.Count(l => l.CommentId == c.Id),
                        IsLiked = _context.CommentLikes.Any(l => l.CommentId == c.Id && l.UserId == currentUserId)
                    })
                    .ToListAsync();

                return Json(new { success = true, comments, commentsDisabled = post.CommentsDisabled });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }
    }
}