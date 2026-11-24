using MediCare.Web.Models;

namespace MediCare.Web.Services;

public static class AppointmentRules
{
    public static bool ValidDuration(int minutes) => minutes is 15 or 30 or 45 or 60;
    public static bool ValidStart(TimeOnly time) => time.Minute % 15 == 0 && time.Ticks % TimeSpan.TicksPerMinute == 0;
    // Adjacent shifts form continuous working hours; gaps are never bridged.
    public static bool Covers(IEnumerable<DoctorSchedule> schedules, DayOfWeek day, TimeOnly from, TimeOnly until)
    {
        if (until <= from) return false;
        var coveredUntil = from;
        foreach (var shift in schedules.Where(s => s.IsActive && s.DayOfWeek == day).OrderBy(s => s.StartTime))
        {
            if (shift.EndTime <= coveredUntil) continue;
            if (shift.StartTime > coveredUntil) return false;
            coveredUntil = shift.EndTime;
            if (coveredUntil >= until) return true;
        }
        return false;
    }

    public static bool MayChangeStatus(AppointmentStatus current, AppointmentStatus next, DateTime endUtc, DateTime nowUtc) =>
        current == AppointmentStatus.Scheduled && (next == AppointmentStatus.Cancelled ||
        ((next == AppointmentStatus.Completed || next == AppointmentStatus.NoShow) && nowUtc >= endUtc));
}
