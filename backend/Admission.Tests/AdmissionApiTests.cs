using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Admission.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Admission.Tests;

[Collection("postgres")]
public sealed class AdmissionApiTests : IAsyncLifetime
{
    private readonly AdmissionApiFactory _factory;
    private HttpClient _client = null!;

    public AdmissionApiTests(PostgresFixture postgres)
    {
        _factory = new AdmissionApiFactory(postgres.ConnectionString);
    }

    public async Task InitializeAsync()
    {
        _client = _factory.CreateClient();
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AdmissionDbContext>();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE holds, classes CASCADE;");
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Class_can_be_created_and_listed_with_derived_counts()
    {
        var created = await CreateClassAsync("Class 6 - Morning Shift", 12);
        Assert.StartsWith("c_", created.Id);
        Assert.Equal(12, created.TotalSeats);

        var classes = await _client.GetFromJsonAsync<List<ClassSeatDto>>("/classes");
        var listed = Assert.Single(classes!);
        Assert.Equal(created.Id, listed.Id);
        Assert.Equal(0, listed.HeldSeats);
        Assert.Equal(0, listed.ConfirmedSeats);
        Assert.Equal(12, listed.AvailableSeats);

        var detail = await _client.GetFromJsonAsync<ClassSeatDto>($"/classes/{created.Id}");
        Assert.Equal(12, detail!.AvailableSeats);
    }

    [Fact]
    public async Task Same_student_cannot_create_two_active_holds_in_one_class()
    {
        var admissionClass = await CreateClassAsync("Class 7 - Day Shift", 3);
        var studentId = "20152691234567890";

        var first = await ApplyAsync(admissionClass.Id, studentId);
        var second = await ApplyAsync(admissionClass.Id, studentId);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("ALREADY_APPLIED", await ErrorCodeAsync(second));
    }

    [Fact]
    public async Task Confirmation_is_owner_only_and_idempotent()
    {
        var admissionClass = await CreateClassAsync("Class 8 - Morning Shift", 2);
        var owner = "20152691234567890";
        var other = "20152691234567891";
        var applied = await ApplyAsync(admissionClass.Id, owner);
        var hold = (await applied.Content.ReadFromJsonAsync<CreateHoldDto>())!;

        var forbidden = await ConfirmAsync(hold.HoldId, other);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal("NOT_HOLD_OWNER", await ErrorCodeAsync(forbidden));

        var first = await ConfirmAsync(hold.HoldId, owner);
        var second = await ConfirmAsync(hold.HoldId, owner);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        var seatSummary = await _client.GetFromJsonAsync<ClassSeatDto>($"/classes/{admissionClass.Id}");
        Assert.Equal(1, seatSummary!.ConfirmedSeats);
        Assert.Equal(1, seatSummary.AvailableSeats);
    }

    [Fact]
    public async Task Cancelled_hold_releases_seat_and_is_idempotent()
    {
        var admissionClass = await CreateClassAsync("Class 9 - Evening Shift", 1);
        var studentId = "20152691234567890";
        var applied = await ApplyAsync(admissionClass.Id, studentId);
        var hold = (await applied.Content.ReadFromJsonAsync<CreateHoldDto>())!;

        Assert.Equal(HttpStatusCode.NoContent, (await CancelAsync(hold.HoldId, studentId)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await CancelAsync(hold.HoldId, studentId)).StatusCode);

        var next = await ApplyAsync(admissionClass.Id, "20152691234567891");
        Assert.Equal(HttpStatusCode.Created, next.StatusCode);
    }

    [Fact]
    public async Task Expired_hold_does_not_consume_capacity_or_allow_confirmation()
    {
        var admissionClass = await CreateClassAsync("Class 10 - Morning Shift", 1);
        var studentId = "20152691234567890";
        var applied = await ApplyAsync(admissionClass.Id, studentId);
        var hold = (await applied.Content.ReadFromJsonAsync<CreateHoldDto>())!;

        await Task.Delay(TimeSpan.FromMilliseconds(2300));
        var statusResponse = await _client.GetFromJsonAsync<HoldDetailsDto>($"/holds/{hold.HoldId}");
        Assert.Equal("EXPIRED", statusResponse!.Status);

        var confirm = await ConfirmAsync(hold.HoldId, studentId);
        Assert.Equal(HttpStatusCode.Conflict, confirm.StatusCode);
        Assert.Equal("HOLD_EXPIRED", await ErrorCodeAsync(confirm));

        var next = await ApplyAsync(admissionClass.Id, "20152691234567891");
        Assert.Equal(HttpStatusCode.Created, next.StatusCode);
    }

    [Fact]
    public async Task Concurrent_applications_never_overbook_real_postgresql()
    {
        const int seats = 20;
        const int applicants = 250;
        var admissionClass = await CreateClassAsync("Concurrency Class - Test", seats);

        var requests = Enumerable.Range(0, applicants).Select(async index =>
        {
            var studentId = (20152691234560000L + index).ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var request = new HttpRequestMessage(HttpMethod.Post, $"/classes/{admissionClass.Id}/holds");
            request.Headers.Add("X-Student-Id", studentId);
            return await _client.SendAsync(request);
        });

        var responses = await Task.WhenAll(requests);
        try
        {
            Assert.Equal(seats, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
            Assert.Equal(applicants - seats, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

            var summary = await _client.GetFromJsonAsync<ClassSeatDto>($"/classes/{admissionClass.Id}");
            Assert.NotNull(summary);
            Assert.Equal(seats, summary!.HeldSeats + summary.ConfirmedSeats);
            Assert.InRange(summary.AvailableSeats, 0, seats);
        }
        finally
        {
            foreach (var response in responses)
                response.Dispose();
        }
    }

    [Fact]
    public async Task Student_id_must_be_exactly_seventeen_ascii_digits()
    {
        var admissionClass = await CreateClassAsync("Class 11 - Day Shift", 2);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/classes/{admissionClass.Id}/holds");
        request.Headers.Add("X-Student-Id", "2015269123456789");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_STUDENT_ID", await ErrorCodeAsync(response));
    }

    [Fact]
    public async Task My_holds_returns_all_statuses_for_the_current_student()
    {
        var admissionClass = await CreateClassAsync("Class 12 - Afternoon Shift", 2);
        var studentId = "20152691234567890";
        var applied = await ApplyAsync(admissionClass.Id, studentId);
        var hold = (await applied.Content.ReadFromJsonAsync<CreateHoldDto>())!;
        await ConfirmAsync(hold.HoldId, studentId);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/me/holds");
        request.Headers.Add("X-Student-Id", studentId);
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var holds = await response.Content.ReadFromJsonAsync<List<StudentHoldDto>>();
        var item = Assert.Single(holds!);
        Assert.Equal("CONFIRMED", item.Status);
        Assert.Equal("Class 12 - Afternoon Shift", item.ClassName);
    }


    [Fact]
    public async Task Leading_zero_student_id_is_preserved_as_text()
    {
        var admissionClass = await CreateClassAsync("Class 6 - Day Shift", 1);
        const string studentId = "01234567890123456";
        var response = await ApplyAsync(admissionClass.Id, studentId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var hold = (await response.Content.ReadFromJsonAsync<CreateHoldDto>())!;
        Assert.Equal(studentId, hold.StudentId);
    }

    [Fact]
    public async Task A_student_can_apply_to_a_different_class_shift_pool()
    {
        var morning = await CreateClassAsync("Class 6 - Morning Shift", 1);
        var day = await CreateClassAsync("Class 6 - Day Shift", 1);
        const string studentId = "20152691234567890";

        var first = await ApplyAsync(morning.Id, studentId);
        var second = await ApplyAsync(day.Id, studentId);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
    }

    [Fact]
    public async Task Hold_lookup_uses_the_published_headerless_contract()
    {
        var admissionClass = await CreateClassAsync("Class 7 - Morning Shift", 1);
        var created = await ApplyAsync(admissionClass.Id, "20152691234567890");
        var hold = (await created.Content.ReadFromJsonAsync<CreateHoldDto>())!;

        // The assignment does not specify X-Student-Id for GET /holds/{holdId}.
        var details = await _client.GetFromJsonAsync<HoldDetailsDto>($"/holds/{hold.HoldId}");
        Assert.NotNull(details);
        Assert.Equal(hold.HoldId, details!.HoldId);
        Assert.Equal(admissionClass.Id, details.ClassId);
        Assert.Equal("HELD", details.Status);
    }

    [Fact]
    public async Task Health_endpoint_reports_database_availability()
    {
        var health = await _client.GetFromJsonAsync<HealthDto>("/health");
        Assert.NotNull(health);
        Assert.Equal("ok", health!.Status);
    }

    private async Task<CreateClassDto> CreateClassAsync(string name, int totalSeats)
    {
        var response = await _client.PostAsJsonAsync("/classes", new { name, totalSeats });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateClassDto>())!;
    }

    private async Task<HttpResponseMessage> ApplyAsync(string classId, string studentId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/classes/{classId}/holds");
        request.Headers.Add("X-Student-Id", studentId);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> ConfirmAsync(string holdId, string studentId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/holds/{holdId}/confirm");
        request.Headers.Add("X-Student-Id", studentId);
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> CancelAsync(string holdId, string studentId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"/holds/{holdId}");
        request.Headers.Add("X-Student-Id", studentId);
        return await _client.SendAsync(request);
    }

    private static async Task<string?> ErrorCodeAsync(HttpResponseMessage response)
    {
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return json.RootElement.GetProperty("error").GetProperty("code").GetString();
    }

    private sealed record HealthDto(string Status);
    private sealed record CreateClassDto(string Id, string Name, int TotalSeats);
    private sealed record ClassSeatDto(string Id, string Name, int TotalSeats, int HeldSeats, int ConfirmedSeats, int AvailableSeats);
    private sealed record CreateHoldDto(string HoldId, string ClassId, string StudentId, string Status, DateTime ExpiresAt);
    private sealed record HoldDetailsDto(string HoldId, string ClassId, string Status, DateTime ExpiresAt);
    private sealed record StudentHoldDto(string HoldId, string ClassId, string ClassName, string Status, DateTime ExpiresAt);
}
