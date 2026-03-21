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

        // 🔐 Obtener UserId seguro
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
            {
                historyQuery = historyQuery
                    .Where(h => h.Date.Date == date.Value.Date);
            }

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
            var userId = GetUserId();

            var user = _context.Users.FirstOrDefault(u => u.Id == userId);

            // 🚫 límite para usuarios gratis
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

            var habit = _context.Habits
                .FirstOrDefault(h => h.Id == id && h.UserId == userId);

            if (habit == null) return NotFound();

            return View(habit);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(Habit habit)
        {
            var userId = GetUserId();

            var habitInDb = _context.Habits
                .FirstOrDefault(h => h.Id == habit.Id && h.UserId == userId);

            if (habitInDb == null) return NotFound();

            habitInDb.Name = habit.Name;

            _context.SaveChanges();
            return RedirectToAction("Index");
        }

        // 🗑 ELIMINAR
        public IActionResult Delete(int id)
        {
            var userId = GetUserId();

            var habit = _context.Habits
                .FirstOrDefault(h => h.Id == id && h.UserId == userId);

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

            var habit = _context.Habits
                .FirstOrDefault(h => h.Id == id && h.UserId == userId);

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

            var habit = _context.Habits
                .FirstOrDefault(h => h.Id == id && h.UserId == userId);

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
            _context.SaveChanges(); // 🔥 primero guardamos

            var commentId = comment.Id; // 🔥 ahora sí existe

            // 🔥 obtener hábito y usuario actual
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
        // 🏆 LOGROS
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

            // 🔥 Historial SOLO del mes (para el heatmap)
            var monthlyHistory = _context.HabitHistories
                .Where(h => userHabitIds.Contains(h.HabitId)
                    && h.Date >= firstDay
                    && h.Date <= lastDay)
                .ToList();

            // 🔥 Historial TOTAL del usuario (para estadísticas reales)
            var fullHistory = _context.HabitHistories
                .Where(h => userHabitIds.Contains(h.HabitId))
                .ToList();

            // 🏆 Mejor día del mes
            var bestDay = monthlyHistory
                .Where(h => h.Completed)
                .GroupBy(h => h.Date.Date)
                .Select(g => new
                {
                    Date = g.Key,
                    Count = g.Count()
                })
                .OrderByDescending(g => g.Count)
                .FirstOrDefault();

            ViewBag.BestDay = bestDay;

            // 📊 CONSISTENCIA REAL DEL MES
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

            // 📅 SEMANA REAL (aunque cruce meses)
            var startOfWeek = DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek);
            var endOfWeek = startOfWeek.AddDays(6);

            var weeklyData = fullHistory
                .Where(h => h.Date >= startOfWeek && h.Date <= endOfWeek)
                .ToList();

            ViewBag.WeeklyCompleted = weeklyData.Count(h => h.Completed);
            ViewBag.WeeklyFailed = weeklyData.Count(h => !h.Completed);

            // 🎯 META DINÁMICA SEGÚN HÁBITOS
            int totalHabits = userHabitIds.Count;
            int monthlyGoal = totalHabits * daysInMonth; // meta perfecta

            int monthlyCompleted = monthlyHistory.Count(h => h.Completed);

            ViewBag.MonthlyGoal = monthlyGoal;
            ViewBag.MonthlyCompleted = monthlyCompleted;

            // 📊 Comparación con mes anterior

            var previousMonthStart = firstDay.AddMonths(-1);
            var previousMonthEnd = previousMonthStart.AddMonths(1).AddDays(-1);

            var previousMonthHistory = _context.HabitHistories
                .Where(h => userHabitIds.Contains(h.HabitId)
                    && h.Date >= previousMonthStart
                    && h.Date <= previousMonthEnd)
                .ToList();

            int previousCompleted = previousMonthHistory.Count(h => h.Completed);
            int currentCompleted = monthlyHistory.Count(h => h.Completed);

            int percentageChange = 0;

            if (previousCompleted > 0)
            {
                percentageChange = (int)(((double)(currentCompleted - previousCompleted)
                    / previousCompleted) * 100);
            }

            ViewBag.PercentageChange = percentageChange;
            ViewBag.PreviousCompleted = previousCompleted;
            ViewBag.CurrentCompleted = currentCompleted;
            // 🏆 Mejor mes histórico del usuario

            var allUserHistory = _context.HabitHistories
                .Where(h => userHabitIds.Contains(h.HabitId) && h.Completed)
                .ToList();

            var bestMonth = allUserHistory
                .GroupBy(h => new { h.Date.Year, h.Date.Month })
                .Select(g => new
                {
                    Year = g.Key.Year,
                    Month = g.Key.Month,
                    Count = g.Count()
                })
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