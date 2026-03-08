using HabitTrackerApp.Data;
using HabitTrackerApp.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Linq;
using System.Security.Claims;

namespace HabitTrackerApp.Controllers.Api
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class HabitsController : ControllerBase
    {
        private readonly HabitDbContext _context;

        public HabitsController(HabitDbContext context)
        {
            _context = context;
        }

        // 🔐 Obtener ID del usuario desde el token
        private int GetUserId()
        {
            var userIdClaim = User.FindFirst("UserId")?.Value;
            return int.Parse(userIdClaim);
        }

        // GET: api/habits
        [HttpGet]
        public IActionResult GetAll()
        {
            var userId = GetUserId();

            var habits = _context.Habits
                .Where(h => h.UserId == userId)
                .ToList();

            return Ok(habits);
        }

        // GET: api/habits/5
        [HttpGet("{id}")]
        public IActionResult GetById(int id)
        {
            var userId = GetUserId();

            var habit = _context.Habits
                .FirstOrDefault(h => h.Id == id && h.UserId == userId);

            if (habit == null)
                return NotFound();

            return Ok(habit);
        }

        // POST: api/habits
        [HttpPost]
        public IActionResult Create([FromBody] Habit habit)
        {
            var userId = GetUserId();

            habit.UserId = userId;
            habit.CreatedDate = DateTime.Now;
            habit.Completed = false;
            habit.StreakDays = 0;
            habit.MaxStreak = 0;
            habit.LastCheckDate = null;

            _context.Habits.Add(habit);
            _context.SaveChanges();

            return CreatedAtAction(nameof(GetById), new { id = habit.Id }, habit);
        }

        // PUT: api/habits/5/complete
        [HttpPut("{id}/complete")]
        public IActionResult Complete(int id)
        {
            var userId = GetUserId();

            var habit = _context.Habits
                .FirstOrDefault(h => h.Id == id && h.UserId == userId);

            if (habit == null)
                return NotFound();

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

            _context.SaveChanges();

            return Ok(habit);
        }

        // PUT: api/habits/5/fail
        [HttpPut("{id}/fail")]
        public IActionResult Fail(int id)
        {
            var userId = GetUserId();

            var habit = _context.Habits
                .FirstOrDefault(h => h.Id == id && h.UserId == userId);

            if (habit == null)
                return NotFound();

            habit.StreakDays = 0;
            habit.Completed = false;
            habit.LastCheckDate = DateTime.Today;

            _context.SaveChanges();

            return Ok(habit);
        }

        // DELETE: api/habits/5
        [HttpDelete("{id}")]
        public IActionResult Delete(int id)
        {
            var userId = GetUserId();

            var habit = _context.Habits
                .FirstOrDefault(h => h.Id == id && h.UserId == userId);

            if (habit == null)
                return NotFound();

            _context.Habits.Remove(habit);
            _context.SaveChanges();

            return NoContent();
        }

    }
}