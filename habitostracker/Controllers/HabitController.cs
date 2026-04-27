using HabitTrackerApp.Data;
using HabitTrackerApp.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using System.Linq;
using Microsoft.EntityFrameworkCore;

namespace HabitTrackerApp.Controllers
{
    [Authorize]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public class HabitController : Controller
    {
        private readonly HabitDbContext _context;

        public HabitController(HabitDbContext context)
        {
            _context = context;
        }

        private int GetUserId()
        {
            var claim = User.FindFirst("UserId");
            if (claim == null) return -1;
            return int.Parse(claim.Value);
        }

        // 📌 DASHBOARD
        public IActionResult Index()
        {
            var userId = GetUserId();

            var user = _context.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null) return RedirectToAction("Login", "Account");

            // 🔥 Si es Google y el perfil está incompleto → forzar CompleteProfile
            if (user.IsGoogleAccount)
            {
                bool perfilIncompleto = string.IsNullOrEmpty(user.Gender)
                    || user.Gender == "No especificado"
                    || string.IsNullOrEmpty(user.Bio)
                    || user.Bio == "Registrado con Google";

                if (perfilIncompleto)
                    return RedirectToAction("CompleteProfile", "Account");
            }

            var habits = _context.Habits
                .Where(h => h.UserId == userId)
                .Include(h => h.Comments)
                .ThenInclude(c => c.User)
                .ToList();

            return View(habits);
        }

        // 📅 HISTORIAL
        public IActionResult History(DateTime? date)
        {
            var userId = GetUserId();

            var userHabitIds = _context.Habits
                .Where(h => h.UserId == userId)
                .Select(h => h.Id)
                .ToList();

            var historyQuery = _context.HabitHistories
                .Where(h => userHabitIds.Contains(h.HabitId));

            if (date.HasValue)
                historyQuery = historyQuery.Where(h => h.Date.Date == date.Value.Date);

            var history = historyQuery
                .OrderByDescending(h => h.Date)
                .ToList();

            ViewBag.SelectedDate = date;
            return View(history);
        }

        // 🏆 LOGROS
        public IActionResult Achievements()
        {
            var userId = GetUserId();

            var userHabitIds = _context.Habits
                .Where(h => h.UserId == userId)
                .Select(h => h.Id)
                .ToList();

            var achievements = _context.Achievements
                .Where(a => userHabitIds.Contains(a.HabitId))
                .OrderByDescending(a => a.DateUnlocked)
                .ToList();

            return View(achievements);
        }

        // ➕ CREAR
        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Create(Habit habit)
        {
            if (string.IsNullOrWhiteSpace(habit.Name))
            {
                TempData["Error"] = "⚠️ El nombre del hábito es obligatorio.";
                return RedirectToAction("Create");
            }

            var userId = GetUserId();
            var user = _context.Users.FirstOrDefault(u => u.Id == userId);

            if (user != null && !user.IsPremium)
            {
                var count = _context.Habits.Count(h => h.UserId == userId);
                if (count >= 5)
                {
                    TempData["Error"] = "🚫 Límite alcanzado (máx 5 hábitos). Hazte premium 😈";
                    return RedirectToAction("Index");
                }
            }

            habit.UserId = userId;
            habit.CreatedDate = DateTime.Now;
            habit.Completed = false;
            habit.StreakDays = 0;
            habit.MaxStreak = 0;

            _context.Habits.Add(habit);
            _context.SaveChanges();

            TempData["Success"] = "✅ Hábito creado correctamente";
            return RedirectToAction("Index");
        }

        // ✏ EDITAR
        public IActionResult Edit(int id)
        {
            var userId = GetUserId();
            var habit = _context.Habits.FirstOrDefault(h => h.Id == id && h.UserId == userId);
            if (habit == null) return NotFound();
            return View(habit);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(Habit habit)
        {
            var userId = GetUserId();
            var habitInDb = _context.Habits.FirstOrDefault(h => h.Id == habit.Id && h.UserId == userId);
            if (habitInDb == null) return NotFound();

            habitInDb.Name = habit.Name;
            _context.SaveChanges();
            return RedirectToAction("Index");
        }

        // 🗑 ELIMINAR
        public IActionResult Delete(int id)
        {
            var userId = GetUserId();
            var habit = _context.Habits.FirstOrDefault(h => h.Id == id && h.UserId == userId);

            if (habit != null)
            {
                _context.Habits.Remove(habit);
                _context.SaveChanges();
            }

            return RedirectToAction("Index");
        }

        // ✅ COMPLETE
        public IActionResult Complete(int id)
        {
            var userId = GetUserId();
            var habit = _context.Habits.FirstOrDefault(h => h.Id == id && h.UserId == userId);
            if (habit == null) return NotFound();

            var today = DateTime.Today;

            if (habit.LastCheckDate == null)
                habit.StreakDays = 1;
            else if (habit.LastCheckDate.Value.Date == today.AddDays(-1))
                habit.StreakDays += 1;
            else if (habit.LastCheckDate.Value.Date != today)
                habit.StreakDays = 1;

            if (habit.StreakDays > habit.MaxStreak)
                habit.MaxStreak = habit.StreakDays;

            habit.Completed = true;
            habit.LastCheckDate = today;

            _context.HabitHistories.Add(new HabitHistory
            {
                HabitId = habit.Id,
                HabitName = habit.Name,
                Date = today,
                Completed = true
            });

            CreateAchievementIfNeeded(habit);
            _context.SaveChanges();

            return RedirectToAction("Index");
        }

        // ❌ FAIL
        public IActionResult Fail(int id)
        {
            var userId = GetUserId();
            var habit = _context.Habits.FirstOrDefault(h => h.Id == id && h.UserId == userId);
            if (habit == null) return NotFound();

            habit.StreakDays = 0;
            habit.Completed = false;
            habit.LastCheckDate = DateTime.Today;

            _context.HabitHistories.Add(new HabitHistory
            {
                HabitId = habit.Id,
                HabitName = habit.Name,
                Date = DateTime.Today,
                Completed = false
            });

            _context.SaveChanges();
            return RedirectToAction("Index");
        }

        [HttpPost]
        public IActionResult AddComment(int habitId, string content)
        {
            var userId = int.Parse(User.FindFirst("UserId").Value);

            var comment = new HabitComment
            {
                HabitId = habitId,
                UserId = userId,
                Content = content
            };

            _context.HabitComments.Add(comment);
            _context.SaveChanges();

            var habit = _context.Habits.FirstOrDefault(h => h.Id == habitId);
            var currentUser = _context.Users.FirstOrDefault(u => u.Id == userId);

            if (habit != null && currentUser != null && habit.UserId != userId)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = habit.UserId,
                    FromUserId = userId,
                    Message = "💬 comentó tu hábito",
                    CreatedAt = DateTime.Now,
                    IsRead = false,
                    FromUsername = currentUser.Username,
                    FromUserImage = currentUser.ProfileImage ?? ""
                });
            }

            _context.SaveChanges();
            return RedirectToAction("Index");
        }

        private void CreateAchievementIfNeeded(Habit habit)
        {
            string achievementTitle = null;

            if (habit.StreakDays == 7)
                achievementTitle = "🔥 7 días seguidos";
            else if (habit.StreakDays == 30)
                achievementTitle = "💪 30 días seguidos";
            else if (habit.StreakDays == 100)
                achievementTitle = "🧠 100 días seguidos";

            if (achievementTitle == null) return;

            bool exists = _context.Achievements
                .Any(a => a.HabitId == habit.Id && a.Title == achievementTitle);

            if (!exists)
            {
                _context.Achievements.Add(new Achievement
                {
                    HabitId = habit.Id,
                    HabitName = habit.Name,
                    Title = achievementTitle,
                    DateUnlocked = DateTime.Now
                });
            }
        }

        // 📊 DETALLE DEL HÁBITO CON GRÁFICA INDIVIDUAL
        public IActionResult Detail(int id)
        {
            var userId = GetUserId();
            var habit = _context.Habits
                .FirstOrDefault(h => h.Id == id && h.UserId == userId);
            if (habit == null) return RedirectToAction("Index");

            var last7Days = Enumerable.Range(0, 7)
                .Select(i => DateTime.Today.AddDays(-i))
                .Reverse()
                .ToList();

            var histories = _context.HabitHistories
                .Where(h => h.HabitId == id && h.Date >= DateTime.Today.AddDays(-6))
                .ToList();

            var dailyData = last7Days.Select(date => new
            {
                date = date.ToString("dd/MM"),
                completed = histories.Count(h => h.Date.Date == date.Date && h.Completed),
                failed = histories.Count(h => h.Date.Date == date.Date && !h.Completed)
            }).ToList();

            // % completado últimos 7 días
            int totalLast7 = histories.Count;
            int completedLast7 = histories.Count(h => h.Completed);
            int completionRate7 = totalLast7 > 0 ? (int)((completedLast7 * 100.0) / totalLast7) : 0;

            // historial completo para el calendario mini
            var allHistory = _context.HabitHistories
                .Where(h => h.HabitId == id)
                .OrderByDescending(h => h.Date)
                .Take(30)
                .ToList();

            ViewBag.Labels = System.Text.Json.JsonSerializer.Serialize(dailyData.Select(d => d.date));
            ViewBag.Completed = System.Text.Json.JsonSerializer.Serialize(dailyData.Select(d => d.completed));
            ViewBag.Failed = System.Text.Json.JsonSerializer.Serialize(dailyData.Select(d => d.failed));
            ViewBag.CompletionRate7 = completionRate7;
            ViewBag.AllHistory = allHistory;

            // progresos compartidos de este hábito
            var progresses = _context.HabitProgresses
                .Where(p => p.HabitId == id)
                .Include(p => p.User)
                .Include(p => p.Reactions).ThenInclude(r => r.User)
                .Include(p => p.Comments).ThenInclude(c => c.User)
                .OrderByDescending(p => p.SharedAt)
                .ToList();

            ViewBag.Progresses = progresses;
            ViewBag.CurrentUserId = userId;

            return View(habit);
        }

        // 🚀 COMPARTIR PROGRESO
        [HttpPost]
        public async Task<IActionResult> ShareProgress(int habitId, string message)
        {
            if (User.IsInRole("Guest")) return Json(new { success = false, error = "Invitados no pueden compartir en la comunidad." });

            var userId = GetUserId();
            var habit = _context.Habits.FirstOrDefault(h => h.Id == habitId && h.UserId == userId);
            if (habit == null) return Json(new { success = false });

            if (string.IsNullOrWhiteSpace(message) || message.Length > 300)
                return Json(new { success = false, error = "El mensaje debe tener entre 1 y 300 caracteres." });

            // calcular % últimos 7 días
            var histories = _context.HabitHistories
                .Where(h => h.HabitId == habitId && h.Date >= DateTime.Today.AddDays(-6))
                .ToList();
            int total = histories.Count;
            int completed = histories.Count(h => h.Completed);
            int rate = total > 0 ? (int)((completed * 100.0) / total) : 0;

            var progress = new HabitProgress
            {
                UserId = userId,
                HabitId = habitId,
                Message = message.Trim(),
                StreakDays = habit.StreakDays,
                CompletionRate = rate,
                SharedAt = DateTime.UtcNow
            };
            _context.HabitProgresses.Add(progress);
            await _context.SaveChangesAsync();

            var user = _context.Users.FirstOrDefault(u => u.Id == userId);
            return Json(new
            {
                success = true,
                id = progress.Id,
                username = user?.Username,
                avatar = user?.ProfileImage ?? user?.ProfilePicture ?? "",
                message = progress.Message,
                streak = progress.StreakDays,
                rate = progress.CompletionRate,
                time = "ahora"
            });
        }

        // 🔥 REACCIONAR A PROGRESO
        [HttpPost]
        public async Task<IActionResult> ReactProgress([FromBody] ReactProgressDto dto)
        {
            if (User.IsInRole("Guest")) return Json(new { success = false, message = "Invitados no pueden reaccionar." });
            var userId = GetUserId();
            var progress = _context.HabitProgresses.FirstOrDefault(p => p.Id == dto.ProgressId);
            if (progress == null) return Json(new { success = false });

            var existing = _context.HabitProgressReactions
                .FirstOrDefault(r => r.HabitProgressId == dto.ProgressId && r.UserId == userId && r.Emoji == dto.Emoji);

            if (existing != null)
            {
                _context.HabitProgressReactions.Remove(existing);
            }
            else
            {
                var old = _context.HabitProgressReactions
                    .FirstOrDefault(r => r.HabitProgressId == dto.ProgressId && r.UserId == userId);
                if (old != null) _context.HabitProgressReactions.Remove(old);

                _context.HabitProgressReactions.Add(new HabitProgressReaction
                {
                    HabitProgressId = dto.ProgressId,
                    UserId = userId,
                    Emoji = dto.Emoji
                });

                if (progress.UserId != userId)
                {
                    var me = _context.Users.FirstOrDefault(u => u.Id == userId);
                    _context.Notifications.Add(new Notification
                    {
                        UserId = progress.UserId,
                        FromUserId = userId,
                        Message = $"{dto.Emoji} {me?.Username} reaccionó a tu progreso",
                        CreatedAt = DateTime.UtcNow,
                        IsRead = false,
                        FromUsername = me?.Username ?? "",
                        FromUserImage = me?.ProfileImage ?? ""
                    });
                }
            }

            await _context.SaveChangesAsync();

            var counts = _context.HabitProgressReactions
                .Where(r => r.HabitProgressId == dto.ProgressId)
                .GroupBy(r => r.Emoji)
                .Select(g => new { emoji = g.Key, count = g.Count() })
                .ToList();

            var myReaction = _context.HabitProgressReactions
                .FirstOrDefault(r => r.HabitProgressId == dto.ProgressId && r.UserId == userId)?.Emoji;

            return Json(new { success = true, counts, myReaction });
        }
        // 📤 EXPORTAR HISTORIAL PDF (solo Premium)
        public IActionResult ExportHistory()
        {
            var userId = GetUserId();
            var user = _context.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null) return RedirectToAction("Index");
            if (!user.IsPremium) return RedirectToAction("Pay", "User");

            var habitIds = _context.Habits
                .Where(h => h.UserId == userId)
                .Select(h => h.Id)
                .ToList();

            var history = _context.HabitHistories
                .Where(h => habitIds.Contains(h.HabitId))
                .OrderByDescending(h => h.Date)
                .Take(90)
                .ToList();

            var habits = _context.Habits
                .Where(h => h.UserId == userId)
                .ToList();

            int totalCompleted = history.Count(h => h.Completed);
            int totalFailed = history.Count(h => !h.Completed);
            int overallRate = (totalCompleted + totalFailed) > 0
                ? (int)((totalCompleted * 100.0) / (totalCompleted + totalFailed)) : 0;

            var html = $@"
<!DOCTYPE html>
<html>
<head>
<meta charset='UTF-8'>
<style>
    body {{ font-family: Arial, sans-serif; color: #1e293b; padding: 40px; }}
    .header {{ text-align: center; margin-bottom: 32px; border-bottom: 2px solid #6366f1; padding-bottom: 20px; }}
    .header h1 {{ font-size: 28px; color: #6366f1; margin: 0 0 4px; }}
    .header p {{ color: #64748b; font-size: 13px; margin: 0; }}
    .stats {{ display: flex; gap: 20px; margin-bottom: 28px; }}
    .stat {{ flex: 1; background: #f8fafc; border-radius: 12px; padding: 16px; text-align: center; border: 1px solid #e2e8f0; }}
    .stat-val {{ font-size: 28px; font-weight: 800; color: #111827; }}
    .stat-val.green {{ color: #16a34a; }}
    .stat-val.red {{ color: #dc2626; }}
    .stat-val.blue {{ color: #2563eb; }}
    .stat-label {{ font-size: 11px; color: #9ca3af; text-transform: uppercase; font-weight: 600; letter-spacing: 0.5px; margin-top: 4px; }}
    table {{ width: 100%; border-collapse: collapse; margin-top: 10px; }}
    th {{ background: #f1f5f9; padding: 10px 14px; font-size: 11px; text-transform: uppercase; letter-spacing: 0.5px; color: #64748b; text-align: left; border-bottom: 2px solid #e2e8f0; }}
    td {{ padding: 10px 14px; border-bottom: 1px solid #f3f4f6; font-size: 13px; }}
    .ok {{ color: #16a34a; font-weight: 700; }}
    .fail {{ color: #dc2626; font-weight: 700; }}
    .section-title {{ font-size: 16px; font-weight: 800; color: #111827; margin: 24px 0 12px; }}
    .footer {{ text-align: center; margin-top: 32px; font-size: 11px; color: #9ca3af; border-top: 1px solid #e2e8f0; padding-top: 16px; }}
</style>
</head>
<body>
<div class='header'>
    <h1>🚀 HabitTracker</h1>
    <p>Reporte de historial de {user.Username} · Generado el {DateTime.Now:dd MMM yyyy}</p>
</div>

<div class='stats'>
    <div class='stat'><div class='stat-val green'>{totalCompleted}</div><div class='stat-label'>Completados</div></div>
    <div class='stat'><div class='stat-val red'>{totalFailed}</div><div class='stat-label'>Fallados</div></div>
    <div class='stat'><div class='stat-val blue'>{overallRate}%</div><div class='stat-label'>Tasa de éxito</div></div>
    <div class='stat'><div class='stat-val'>{habits.Count}</div><div class='stat-label'>Hábitos</div></div>
</div>

<div class='section-title'>📊 Resumen de hábitos</div>
<table>
    <tr><th>Hábito</th><th>Racha actual</th><th>Mejor racha</th></tr>
    {string.Join("", habits.Select(h => $"<tr><td>{h.Name}</td><td>{h.StreakDays} 🔥</td><td>{h.MaxStreak} 🏆</td></tr>"))}
</table>

<div class='section-title'>📅 Historial de los últimos 90 días</div>
<table>
    <tr><th>Fecha</th><th>Hábito</th><th>Resultado</th></tr>
    {string.Join("", history.Select(h => $"<tr><td>{h.Date:dd MMM yyyy}</td><td>{h.HabitName}</td><td class='{(h.Completed ? "ok" : "fail")}'>{(h.Completed ? "✔ Completado" : "✖ Fallado")}</td></tr>"))}
</table>

<div class='footer'>HabitTracker Pro · habitostracker-production-4cf5.up.railway.app</div>
</body>
</html>";

            var bytes = System.Text.Encoding.UTF8.GetBytes(html);
            return File(bytes, "text/html", $"historial-{user.Username}-{DateTime.Now:yyyyMMdd}.html");
        }

        // 📊 ESTADÍSTICAS AVANZADAS (solo Premium)
        public IActionResult Statistics()
        {
            var userId = GetUserId();
            var user = _context.Users.FirstOrDefault(u => u.Id == userId);
            if (user == null) return RedirectToAction("Index");
            if (!user.IsPremium) return RedirectToAction("Pay", "User");

            var habitIds = _context.Habits
                .Where(h => h.UserId == userId)
                .Select(h => h.Id)
                .ToList();

            var habits = _context.Habits
                .Where(h => h.UserId == userId)
                .ToList();

            var last90 = DateTime.Today.AddDays(-89);
            var history = _context.HabitHistories
                .Where(h => habitIds.Contains(h.HabitId) && h.Date >= last90)
                .ToList();

            // % completado por día de semana
            var byDayOfWeek = Enumerable.Range(0, 7).Select(d => {
                var day = (DayOfWeek)d;
                var total = history.Count(h => h.Date.DayOfWeek == day);
                var completed = history.Count(h => h.Date.DayOfWeek == day && h.Completed);
                return new { day = day.ToString(), rate = total > 0 ? (int)((completed * 100.0) / total) : 0 };
            }).ToList();

            // completados por mes (últimos 3 meses)
            var byMonth = Enumerable.Range(0, 3).Select(i => {
                var date = DateTime.Today.AddMonths(-i);
                var start = new DateTime(date.Year, date.Month, 1);
                var end = start.AddMonths(1).AddDays(-1);
                var total = history.Count(h => h.Date >= start && h.Date <= end);
                var completed = history.Count(h => h.Date >= start && h.Date <= end && h.Completed);
                return new
                {
                    month = start.ToString("MMM yyyy"),
                    completed,
                    failed = total - completed,
                    rate = total > 0 ? (int)((completed * 100.0) / total) : 0
                };
            }).OrderBy(m => m.month).ToList();

            // mejor hábito (más racha)
            var bestHabit = habits.OrderByDescending(h => h.MaxStreak).FirstOrDefault();

            // racha actual total
            int totalStreak = habits.Sum(h => h.StreakDays);

            // días completados vs fallados últimos 90 días
            int totalCompleted = history.Count(h => h.Completed);
            int totalFailed = history.Count(h => !h.Completed);
            int totalDays = history.Select(h => h.Date.Date).Distinct().Count();
            int overallRate = (totalCompleted + totalFailed) > 0
                ? (int)((totalCompleted * 100.0) / (totalCompleted + totalFailed)) : 0;

            // tendencia últimas 4 semanas
            var weeklyTrend = Enumerable.Range(0, 4).Select(w => {
                var start = DateTime.Today.AddDays(-(w + 1) * 7);
                var end = DateTime.Today.AddDays(-w * 7);
                var total = history.Count(h => h.Date >= start && h.Date < end);
                var completed = history.Count(h => h.Date >= start && h.Date < end && h.Completed);
                return new
                {
                    week = $"Sem {4 - w}",
                    rate = total > 0 ? (int)((completed * 100.0) / total) : 0
                };
            }).OrderBy(w => w.week).ToList();

            ViewBag.ByDayOfWeek = System.Text.Json.JsonSerializer.Serialize(byDayOfWeek);
            ViewBag.ByMonth = System.Text.Json.JsonSerializer.Serialize(byMonth);
            ViewBag.WeeklyTrend = System.Text.Json.JsonSerializer.Serialize(weeklyTrend);
            ViewBag.BestHabit = bestHabit;
            ViewBag.TotalStreak = totalStreak;
            ViewBag.TotalCompleted = totalCompleted;
            ViewBag.TotalFailed = totalFailed;
            ViewBag.OverallRate = overallRate;
            ViewBag.TotalDays = totalDays;

            return View(habits);
        }

        // 💬 COMENTAR PROGRESO
        [HttpPost]
        public async Task<IActionResult> CommentProgress([FromBody] CommentProgressDto dto)
        {
            if (User.IsInRole("Guest")) return Json(new { success = false, message = "Invitados no pueden comentar." });
            var userId = GetUserId();
            if (string.IsNullOrWhiteSpace(dto.Content) || dto.Content.Length > 200)
                return Json(new { success = false });

            var progress = _context.HabitProgresses.FirstOrDefault(p => p.Id == dto.ProgressId);
            if (progress == null) return Json(new { success = false });

            var comment = new HabitProgressComment
            {
                HabitProgressId = dto.ProgressId,
                UserId = userId,
                Content = dto.Content.Trim(),
                CreatedAt = DateTime.UtcNow
            };
            _context.HabitProgressComments.Add(comment);

            // notificar
            if (progress.UserId != userId)
            {
                var me = _context.Users.FirstOrDefault(u => u.Id == userId);
                _context.Notifications.Add(new Notification
                {
                    UserId = progress.UserId,
                    FromUserId = userId,
                    Message = $"💬 {me?.Username} comentó tu progreso",
                    CreatedAt = DateTime.UtcNow,
                    IsRead = false,
                    FromUsername = me?.Username ?? "",
                    FromUserImage = me?.ProfileImage ?? ""
                });
            }

            await _context.SaveChangesAsync();

            var user = _context.Users.FirstOrDefault(u => u.Id == userId);
            return Json(new
            {
                success = true,
                id = comment.Id,
                username = user?.Username,
                avatar = user?.ProfileImage ?? user?.ProfilePicture ?? "",
                content = comment.Content,
                time = "ahora"
            });
        }

        // 🌍 COMUNIDAD — todos los progresos
        public IActionResult Community()
        {
            var userId = GetUserId();

            var progresses = _context.HabitProgresses
                .Include(p => p.User)
                .Include(p => p.Habit)
                .Include(p => p.Reactions).ThenInclude(r => r.User)
                .Include(p => p.Comments).ThenInclude(c => c.User)
                .OrderByDescending(p => p.SharedAt)
                .Take(50)
                .ToList();

            ViewBag.CurrentUserId = userId;
            return View(progresses);
        }

        // DTOs
        public class ReactProgressDto { public int ProgressId { get; set; } public string Emoji { get; set; } }
        public class CommentProgressDto { public int ProgressId { get; set; } public string Content { get; set; } }

        // 📅 CALENDARIO
        public IActionResult Calendar(DateTime? month)
        {
            var userId = GetUserId();
            DateTime selectedDate = month ?? DateTime.Today;

            var firstDay = new DateTime(selectedDate.Year, selectedDate.Month, 1);
            var lastDay = firstDay.AddMonths(1).AddDays(-1);

            var userHabitIds = _context.Habits
                .Where(h => h.UserId == userId)
                .Select(h => h.Id)
                .ToList();

            var monthlyHistory = _context.HabitHistories
                .Where(h => userHabitIds.Contains(h.HabitId)
                    && h.Date >= firstDay
                    && h.Date <= lastDay)
                .ToList();

            var fullHistory = _context.HabitHistories
                .Where(h => userHabitIds.Contains(h.HabitId))
                .ToList();

            var bestDay = monthlyHistory
                .Where(h => h.Completed)
                .GroupBy(h => h.Date.Date)
                .Select(g => new { Date = g.Key, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .FirstOrDefault();

            ViewBag.BestDay = bestDay;

            int daysInMonth = DateTime.DaysInMonth(selectedDate.Year, selectedDate.Month);
            int completedDays = monthlyHistory
                .Where(h => h.Completed)
                .Select(h => h.Date.Date)
                .Distinct()
                .Count();

            int consistency = daysInMonth > 0
                ? (int)((double)completedDays / daysInMonth * 100)
                : 0;

            ViewBag.Consistency = consistency;

            var startOfWeek = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
            var endOfWeek = startOfWeek.AddDays(6);

            var weeklyData = fullHistory
                .Where(h => h.Date >= startOfWeek && h.Date <= endOfWeek)
                .ToList();

            ViewBag.WeeklyCompleted = weeklyData.Count(h => h.Completed);
            ViewBag.WeeklyFailed = weeklyData.Count(h => !h.Completed);

            int totalHabits = userHabitIds.Count;
            int monthlyGoal = totalHabits * daysInMonth;
            int monthlyCompleted = monthlyHistory.Count(h => h.Completed);

            ViewBag.MonthlyGoal = monthlyGoal;
            ViewBag.MonthlyCompleted = monthlyCompleted;

            var previousMonthStart = firstDay.AddMonths(-1);
            var previousMonthEnd = previousMonthStart.AddMonths(1).AddDays(-1);

            var previousMonthHistory = _context.HabitHistories
                .Where(h => userHabitIds.Contains(h.HabitId)
                    && h.Date >= previousMonthStart
                    && h.Date <= previousMonthEnd)
                .ToList();

            int previousCompleted = previousMonthHistory.Count(h => h.Completed);
            int currentCompleted = monthlyHistory.Count(h => h.Completed);

            int percentageChange = previousCompleted > 0
                ? (int)(((double)(currentCompleted - previousCompleted) / previousCompleted) * 100)
                : 0;

            ViewBag.PercentageChange = percentageChange;
            ViewBag.PreviousCompleted = previousCompleted;
            ViewBag.CurrentCompleted = currentCompleted;

            var allUserHistory = _context.HabitHistories
                .Where(h => userHabitIds.Contains(h.HabitId) && h.Completed)
                .ToList();

            var bestMonth = allUserHistory
                .GroupBy(h => new { h.Date.Year, h.Date.Month })
                .Select(g => new { Year = g.Key.Year, Month = g.Key.Month, Count = g.Count() })
                .OrderByDescending(g => g.Count)
                .FirstOrDefault();

            if (bestMonth != null)
            {
                ViewBag.BestMonthName = new DateTime(bestMonth.Year, bestMonth.Month, 1)
                    .ToString("MMMM yyyy");
                ViewBag.BestMonthCount = bestMonth.Count;
            }

            return View(monthlyHistory);
        }
    }
}