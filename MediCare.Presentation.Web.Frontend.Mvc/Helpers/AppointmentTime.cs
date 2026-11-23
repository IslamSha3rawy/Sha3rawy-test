using System.Globalization;

namespace MediCare.Presentation.Web.Frontend.Mvc.Helpers;

/// <summary>Presentation only. Appointment storage and comparisons remain UTC.</summary>
public static class AppointmentTime
{
    private static readonly TimeZoneInfo Cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");

    public static string Format(DateTime utc, string format = "ddd, dd MMM yyyy · HH:mm")
    {
        // SQL datetime2 does not preserve Kind; these fields are UTC by contract.
        var instant = utc.Kind == DateTimeKind.Local
            ? utc.ToUniversalTime()
            : DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(instant, Cairo);
        var offset = Cairo.GetUtcOffset(instant);
        return $"{local.ToString(format, CultureInfo.InvariantCulture)} (Cairo, UTC+{offset:hh\\:mm})";
    }
}
