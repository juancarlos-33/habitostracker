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
            if (claim == null)
                throw new UnauthorizedAccessException("Usuario no autenticado correctamente.");
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

            // si ya reaccionó con el mismo emoji, quitarlo (toggle)
            var existing = _context.HabitProgressReactions
                .FirstOrDefault(r => r.HabitProgressId == dto.ProgressId && r.UserId == userId && r.Emoji == dto.Emoji);

            if (existing != null)
            {
                _context.HabitProgressReactions.Remove(existing);
            }
            else
            {
                // quitar reacción anterior si tenía otra
                var old = _context.HabitProgressReactions
                    .FirstOrDefault(r => r.HabitProgressId == dto.ProgressId && r.UserId == userId);
                if (old != null) _context.HabitProgressReactions.Remove(old);

                _context.HabitProgressReactions.Add(new HabitProgressReaction
                {
                    HabitProgressId = dto.ProgressId,
                    UserId = userId,
                    Emoji = dto.Emoji
                });

                // notificar al dueño del progreso
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

            // contar reacciones agrupadas
            var counts = _context.HabitProgressReactions
                .Where(r => r.HabitProgressId == dto.ProgressId)
                .GroupBy(r => r.Emoji)
                .Select(g => new { emoji = g.Key, count = g.Count() })
                .ToList();

            return Json(new { success = true, counts });
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