using MediCare.Common.Enum;
using MediCare.Core.Entity.Entity;
using MediCare.Infrastructure.Repository.Context;
using MediCare.Infrastructure.Repository.Repository;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MediCare.Tests;

// Real relational queries/transactions, not EF's InMemory provider. SQL Server
// locking/deadlocks still need a provider-specific suite before production use.
public sealed class RepositoryTests : IAsyncLifetime
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"medicare-{Guid.NewGuid():N}.db");
    private int firstSlot;
    private int secondSlot;
    private int firstPatient;
    private int otherPatient;
    private int recordId;
    private int attachmentId;

    private ApplicationDbContext Open() => new(new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlite($"Data Source={path};Default Timeout=10;Pooling=False") .Options);

    public async Task InitializeAsync()
    {
        await using var db = Open();
        await db.Database.EnsureCreatedAsync();
        var patient = new Patient { User = new ApplicationUser { Id = "patient-a", UserName = "patient-a" } };
        var other = new Patient { User = new ApplicationUser { Id = "patient-b", UserName = "patient-b" } };
        var doctor = new Doctor
        {
            User = new ApplicationUser { Id = "doctor-a", UserName = "doctor-a" },
            Specialization = new Specialization { EnName = "General", ArName = "General" }
        };
        var link = new ClinicDoctor
        {
            Doctor = doctor,
            Clinic = new Clinic { AreaId = await db.Areas.Select(area => area.AreaId).FirstAsync(), EnName = "Test clinic", ArName = "Test clinic" },
            ConsultationDurationMinutes = 30
        };
        var slots = new[] {
            new Slot { ClinicDoctor = link, SlotDateTime = DateTime.UtcNow.AddDays(2) },
            new Slot { ClinicDoctor = link, SlotDateTime = DateTime.UtcNow.AddDays(3) }
        };
        var past = new Appointment
        {
            Patient = patient,
            Slot = new Slot { ClinicDoctor = link, SlotDateTime = DateTime.UtcNow.AddDays(-1), IsAvailable = false },
            StartAt = DateTime.UtcNow.AddDays(-1), EndAt = DateTime.UtcNow.AddDays(-1).AddMinutes(30)
        };
        var record = new MedicalRecord { Appointment = past, Patient = patient, Doctor = doctor, Diagnosis = "Test only" };
        var attachment = new UploadedAttachment { MedicalRecord = record, FileName = "test.pdf", FilePath = "not-a-real-file" };
        db.AddRange(patient, other, link, slots[0], slots[1], attachment);
        await db.SaveChangesAsync();
        firstSlot = slots[0].SlotId;
        secondSlot = slots[1].SlotId;
        firstPatient = patient.PatientId;
        otherPatient = other.PatientId;
        recordId = record.MedicalRecordId;
        attachmentId = attachment.UploadedAttachmentId;
    }

    [Fact]
    public async Task CompetingBookingsProduceExactlyOneConfirmedAppointment()
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;
        async Task<Appointment?> Book(int patient, string user) => await Task.Run(async () =>
        {
            await using var db = Open();
            if (Interlocked.Increment(ref arrived) == 2) ready.SetResult();
            await ready.Task;
            return await new AppointmentRepository(db).TryBookSlotAsync(firstSlot, patient, user);
        });
        var results = await Task.WhenAll(Book(firstPatient, "patient-a"), Book(otherPatient, "patient-b"));
        Assert.Single(results, item => item is not null);
        await using var verify = Open();
        Assert.Equal(1, await verify.Appointments.CountAsync(item => item.SlotId == firstSlot));
        Assert.False((await verify.Slots.FindAsync(firstSlot))!.IsAvailable);
    }

    [Fact]
    public async Task OtherPatientCannotReadCancelOrRescheduleAppointment()
    {
        await using var db = Open();
        var repo = new AppointmentRepository(db);
        var booking = await repo.TryBookSlotAsync(firstSlot, firstPatient, "patient-a");
        Assert.NotNull(booking);
        Assert.Empty(await repo.GetPatientAppointmentsAsync(otherPatient));
        Assert.False(await repo.TryCancelAsync(booking.AppointmentId, otherPatient, "patient-b", null));
        Assert.False(await repo.TryRescheduleAsync(booking.AppointmentId, secondSlot, otherPatient, "patient-b"));
        await using var verify = Open();
        Assert.False((await verify.Slots.FindAsync(firstSlot))!.IsAvailable);
        Assert.True((await verify.Slots.FindAsync(secondSlot))!.IsAvailable);
    }

    [Fact]
    public async Task OwnerCanRescheduleThenCancelAndReleaseSlots()
    {
        await using var db = Open();
        var repo = new AppointmentRepository(db);
        var booking = await repo.TryBookSlotAsync(firstSlot, firstPatient, "patient-a");
        Assert.NotNull(booking);
        Assert.True(await repo.TryRescheduleAsync(booking.AppointmentId, secondSlot, firstPatient, "patient-a"));
        await using (var verify = Open())
        {
            Assert.True((await verify.Slots.FindAsync(firstSlot))!.IsAvailable);
            Assert.False((await verify.Slots.FindAsync(secondSlot))!.IsAvailable);
        }
        Assert.True(await repo.TryCancelAsync(booking.AppointmentId, firstPatient, "patient-a", "Test"));
        await using var final = Open();
        Assert.True((await final.Slots.FindAsync(secondSlot))!.IsAvailable);
        Assert.Equal((int)AppointmentStatusEnum.Cancelled, (await final.Appointments.FindAsync(booking.AppointmentId))!.AppointmentStatusId);
    }

    [Theory]
    [InlineData(false, "patient-b")]
    [InlineData(true, "unrelated-doctor")]
    public async Task UnrelatedUsersCannotReadRecordsOrAttachments(bool isDoctor, string user)
    {
        await using var db = Open();
        var repo = new ClinicalRecordRepository(db);
        Assert.Null(await repo.GetRecordForAttachmentAsync(recordId, user, isDoctor));
        Assert.Null(await repo.GetOwnedAttachmentAsync(attachmentId, user, isDoctor));
        Assert.Empty(isDoctor ? await repo.GetDoctorRecordsAsync(user) : await repo.GetPatientRecordsAsync(user));
    }

    [Theory]
    [InlineData(false, "patient-a")]
    [InlineData(true, "doctor-a")]
    public async Task RecordOwnersRetainAccess(bool isDoctor, string user)
    {
        await using var db = Open();
        var repo = new ClinicalRecordRepository(db);
        Assert.NotNull(await repo.GetRecordForAttachmentAsync(recordId, user, isDoctor));
        Assert.NotNull(await repo.GetOwnedAttachmentAsync(attachmentId, user, isDoctor));
    }

    public Task DisposeAsync()
    {
        SqliteConnection.ClearAllPools();
        File.Delete(path);
        return Task.CompletedTask;
    }
}
