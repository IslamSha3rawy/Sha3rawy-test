using MediCare.Presentation.Web.Frontend.Mvc.Helpers;

namespace MediCare.Tests;

public class AppointmentTimeTests
{
    [Theory]
    [InlineData(2026, 1, 15, "15 Jan 2026 · 12:00 (Cairo, UTC+02:00)")]
    [InlineData(2026, 7, 15, "15 Jul 2026 · 13:00 (Cairo, UTC+03:00)")]
    public void UsesCairoRulesRatherThanServerTimezone(int year, int month, int day, string expected)
    {
        var utc = new DateTime(year, month, day, 10, 0, 0, DateTimeKind.Utc);
        Assert.Equal(expected, AppointmentTime.Format(utc, "dd MMM yyyy · HH:mm"));
        Assert.Equal(expected, AppointmentTime.Format(DateTime.SpecifyKind(utc, DateTimeKind.Unspecified), "dd MMM yyyy · HH:mm"));
    }

    [Fact]
    public void ConvertsAcrossMidnight()
    {
        var utc = new DateTime(2026, 1, 15, 23, 30, 0, DateTimeKind.Utc);
        Assert.Equal("16 Jan 2026 · 01:30 (Cairo, UTC+02:00)", AppointmentTime.Format(utc, "dd MMM yyyy · HH:mm"));
    }
}
