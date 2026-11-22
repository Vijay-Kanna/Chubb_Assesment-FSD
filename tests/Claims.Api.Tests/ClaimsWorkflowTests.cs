using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Claims.Api.Tests;

public sealed class ClaimsWorkflowTests : IClassFixture<ClaimsApiFactory>
{
    private readonly HttpClient client;

    public ClaimsWorkflowTests(ClaimsApiFactory factory)
    {
        client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false
        });
    }

    [Fact]
    public async Task Login_returns_role_and_rejects_wrong_credentials()
    {
        var claimant = await Login("claimant");
        var staff = await Login("staff");
        Assert.Equal("Claimant", claimant.GetProperty("role").GetString());
        Assert.Equal("ClaimsStaff", staff.GetProperty("role").GetString());

        using var response = await client.PostAsJsonAsync("/auth/login",
            new { email = "claimant@example.test", password = "incorrect" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Unauthenticated_and_invalid_bearer_tokens_are_rejected()
    {
        using var missing = await Request(HttpMethod.Get, "/claims");
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);

        using var invalid = await Request(HttpMethod.Get, "/claims", "invalid-token");
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
    }

    [Fact]
    public async Task Owner_can_submit_and_track_a_claim_and_staff_can_pick_it_up()
    {
        var claimant = await Token("claimant");
        var staff = await Token("staff");
        var otherStaff = await Token("other-staff");
        var id = await Submit(claimant);
        var path = $"/claims/{id}";

        using var submitted = await Request(HttpMethod.Get, path, claimant);
        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);
        Assert.Equal("Submitted", (await Read(submitted)).GetProperty("status").GetString());

        using var queue = await Request(HttpMethod.Get, "/claims?status=Submitted", staff);
        Assert.Contains((await Read(queue)).EnumerateArray(), claim => claim.GetProperty("id").GetGuid() == id);

        using var assigned = await Request(HttpMethod.Post, $"{path}/pick-up", staff);
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        var assignedClaim = await Read(assigned);
        Assert.Equal("UnderReview", assignedClaim.GetProperty("status").GetString());
        var staffId = assignedClaim.GetProperty("assignedStaffId").GetGuid();

        using var tracked = await Request(HttpMethod.Get, path, claimant);
        Assert.Equal("UnderReview", (await Read(tracked)).GetProperty("status").GetString());

        using var assignedQueue = await Request(HttpMethod.Get, $"/claims?status=UnderReview&assignedStaffId={staffId}", staff);
        Assert.Contains((await Read(assignedQueue)).EnumerateArray(), claim => claim.GetProperty("id").GetGuid() == id);

        using var otherStaffRead = await Request(HttpMethod.Get, path, otherStaff);
        Assert.Equal(HttpStatusCode.NotFound, otherStaffRead.StatusCode);

        using var history = await Request(HttpMethod.Get, $"{path}/history", claimant);
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var entries = (await Read(history)).EnumerateArray().ToArray();
        Assert.Equal(["Submitted", "Assigned"], entries.Select(entry => entry.GetProperty("action").GetString()));
        Assert.Equal(staffId, entries[1].GetProperty("actorUserId").GetGuid());
        Assert.Equal("Submitted", entries[1].GetProperty("previousStatus").GetString());
        Assert.Equal("UnderReview", entries[1].GetProperty("newStatus").GetString());
    }

    [Fact]
    public async Task Claim_ownership_and_roles_are_enforced_by_the_api()
    {
        var claimant = await Token("claimant");
        var other = await Token("other-claimant");
        var staff = await Token("staff");
        var id = await Submit(claimant);

        using var read = await Request(HttpMethod.Get, $"/claims/{id}", other);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        using var history = await Request(HttpMethod.Get, $"/claims/{id}/history", other);
        Assert.Equal(HttpStatusCode.NotFound, history.StatusCode);
        using var list = await Request(HttpMethod.Get, "/claims", other);
        Assert.DoesNotContain((await Read(list)).EnumerateArray(), claim => claim.GetProperty("id").GetGuid() == id);
        using var pickUp = await Request(HttpMethod.Post, $"/claims/{id}/pick-up", claimant);
        Assert.Equal(HttpStatusCode.Forbidden, pickUp.StatusCode);
        using var create = await Request(HttpMethod.Post, "/claims", staff, ValidClaim());
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    [Fact]
    public async Task Competing_staff_requests_can_only_assign_once()
    {
        var claimant = await Token("claimant");
        var first = await Token("staff");
        var second = await Token("other-staff");
        var id = await Submit(claimant);
        var path = $"/claims/{id}";

        var responses = await Task.WhenAll(
            Request(HttpMethod.Post, $"{path}/pick-up", first),
            Request(HttpMethod.Post, $"{path}/pick-up", second));
        using var firstResponse = responses[0];
        using var secondResponse = responses[1];
        Assert.Equal(
            new[] { HttpStatusCode.OK, HttpStatusCode.Conflict },
            responses.Select(response => response.StatusCode).Order());

        using var history = await Request(HttpMethod.Get, $"{path}/history", claimant);
        Assert.Single((await Read(history)).EnumerateArray(), item => item.GetProperty("action").GetString() == "Assigned");
    }

    [Fact]
    public async Task Invalid_claim_input_returns_validation_errors()
    {
        var claimant = await Token("claimant");
        using var response = await Request(HttpMethod.Post, "/claims", claimant,
            new { type = "Motor", incidentDate = DateOnly.MaxValue, description = " " });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await Read(response)).GetProperty("errors");
        Assert.True(errors.TryGetProperty("incidentDate", out _));
        Assert.True(errors.TryGetProperty("description", out _));
    }

    [Fact]
    public async Task Assigned_staff_can_review_a_conversation_approve_then_record_settlement()
    {
        var claimant = await Token("claimant");
        var staff = await Token("staff");
        var path = $"/claims/{await Submit(claimant)}";
        var submitted = await CheckStatus(HttpMethod.Post, $"{path}/pick-up", staff, "UnderReview");
        var staffId = submitted.GetProperty("assignedStaffId").GetGuid();
        var claimantId = submitted.GetProperty("claimantId").GetGuid();

        await CheckStatus(HttpMethod.Post, $"{path}/questions", staff, "AwaitingInformation", "Where was the car parked?");
        await CheckStatus(HttpMethod.Get, path, claimant, "AwaitingInformation");
        await CheckStatus(HttpMethod.Post, $"{path}/replies", claimant, "UnderReview", "Outside my home.");
        await CheckStatus(HttpMethod.Post, $"{path}/questions", staff, "AwaitingInformation", "When did you notice the damage?");
        await CheckStatus(HttpMethod.Post, $"{path}/replies", claimant, "UnderReview", "The following morning.");
        await CheckStatus(HttpMethod.Post, $"{path}/approve", staff, "Approved");
        await CheckStatus(HttpMethod.Get, path, claimant, "Approved");
        await CheckStatus(HttpMethod.Post, $"{path}/settle", staff, "Settled");
        await CheckStatus(HttpMethod.Get, path, claimant, "Settled");

        using var messages = await Request(HttpMethod.Get, $"{path}/messages", claimant);
        Assert.Equal(HttpStatusCode.OK, messages.StatusCode);
        var conversation = (await Read(messages)).EnumerateArray().ToArray();
        Assert.Equal(
            ["Where was the car parked?", "Outside my home.", "When did you notice the damage?", "The following morning."],
            conversation.Select(message => message.GetProperty("body").GetString()));
        Assert.Equal([staffId, claimantId, staffId, claimantId],
            conversation.Select(message => message.GetProperty("senderUserId").GetGuid()));

        using var history = await Request(HttpMethod.Get, $"{path}/history", staff);
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        var entries = (await Read(history)).EnumerateArray().ToArray();
        Assert.Equal(
            ["Submitted", "Assigned", "QuestionAsked", "ReplyReceived", "QuestionAsked", "ReplyReceived", "Approved", "Settled"],
            entries.Select(entry => entry.GetProperty("action").GetString()));
        Assert.Equal(conversation.Select(message => message.GetProperty("id").GetInt64()),
            entries[2..6].Select(entry => entry.GetProperty("claimMessageId").GetInt64()));
        Assert.Equal("Approved", entries[^1].GetProperty("previousStatus").GetString());
        Assert.Equal("Settled", entries[^1].GetProperty("newStatus").GetString());
    }

    [Fact]
    public async Task Workflow_rejects_invalid_transitions_without_recording_them()
    {
        var claimant = await Token("claimant");
        var staff = await Token("staff");
        var path = $"/claims/{await Submit(claimant)}";
        await CheckStatus(HttpMethod.Post, $"{path}/pick-up", staff, "UnderReview");
        await ExpectStatus(HttpMethod.Post, $"{path}/settle", staff, HttpStatusCode.Conflict);
        await ExpectStatus(HttpMethod.Post, $"{path}/replies", claimant, HttpStatusCode.Conflict, new { body = "An unsolicited reply" });
        await ExpectStatus(HttpMethod.Post, $"{path}/questions", staff, HttpStatusCode.BadRequest, new { body = " " });
        await CheckStatus(HttpMethod.Post, $"{path}/questions", staff, "AwaitingInformation", "Is there a witness?");
        await ExpectStatus(HttpMethod.Post, $"{path}/approve", staff, HttpStatusCode.Conflict);
        await ExpectStatus(HttpMethod.Post, $"{path}/reject", staff, HttpStatusCode.Conflict);
        await ExpectStatus(HttpMethod.Post, $"{path}/questions", staff, HttpStatusCode.Conflict, new { body = "A second outstanding question" });
        await CheckStatus(HttpMethod.Post, $"{path}/replies", claimant, "UnderReview", "No witness.");
        await CheckStatus(HttpMethod.Post, $"{path}/reject", staff, "Rejected");
        await ExpectStatus(HttpMethod.Post, $"{path}/settle", staff, HttpStatusCode.Conflict);
        await ExpectStatus(HttpMethod.Post, $"{path}/questions", staff, HttpStatusCode.Conflict, new { body = "A late question" });
        await CheckStatus(HttpMethod.Get, path, claimant, "Rejected");

        using var history = await Request(HttpMethod.Get, $"{path}/history", claimant);
        Assert.Equal(["Submitted", "Assigned", "QuestionAsked", "ReplyReceived", "Rejected"],
            (await Read(history)).EnumerateArray().Select(entry => entry.GetProperty("action").GetString()));
    }

    [Fact]
    public async Task Review_actions_and_messages_are_restricted_to_the_assigned_staff_and_owner()
    {
        var claimant = await Token("claimant");
        var otherClaimant = await Token("other-claimant");
        var staff = await Token("staff");
        var otherStaff = await Token("other-staff");
        var path = $"/claims/{await Submit(claimant)}";
        var message = new { body = "What was damaged?" };

        await ExpectStatus(HttpMethod.Get, $"{path}/messages", staff, HttpStatusCode.NotFound);
        await ExpectStatus(HttpMethod.Post, $"{path}/questions", staff, HttpStatusCode.NotFound, message);
        await CheckStatus(HttpMethod.Post, $"{path}/pick-up", staff, "UnderReview");
        await CheckStatus(HttpMethod.Post, $"{path}/questions", staff, "AwaitingInformation", message.body);
        await ExpectStatus(HttpMethod.Get, $"{path}/messages", otherStaff, HttpStatusCode.NotFound);
        await ExpectStatus(HttpMethod.Get, $"{path}/messages", otherClaimant, HttpStatusCode.NotFound);
        await ExpectStatus(HttpMethod.Get, $"{path}/history", otherStaff, HttpStatusCode.NotFound);
        await ExpectStatus(HttpMethod.Post, $"{path}/questions", otherStaff, HttpStatusCode.NotFound, message);
        await ExpectStatus(HttpMethod.Post, $"{path}/questions", claimant, HttpStatusCode.Forbidden, message);
        await ExpectStatus(HttpMethod.Post, $"{path}/replies", otherClaimant, HttpStatusCode.NotFound, message);
        await ExpectStatus(HttpMethod.Post, $"{path}/replies", staff, HttpStatusCode.Forbidden, message);
        await CheckStatus(HttpMethod.Post, $"{path}/replies", claimant, "UnderReview", "The bumper.");
        await ExpectStatus(HttpMethod.Post, $"{path}/approve", otherStaff, HttpStatusCode.NotFound);
        await ExpectStatus(HttpMethod.Post, $"{path}/reject", otherStaff, HttpStatusCode.NotFound);
        await ExpectStatus(HttpMethod.Post, $"{path}/approve", claimant, HttpStatusCode.Forbidden);
        await CheckStatus(HttpMethod.Post, $"{path}/approve", staff, "Approved");
        await ExpectStatus(HttpMethod.Post, $"{path}/settle", otherStaff, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Competing_questions_leave_only_the_winning_message_and_history()
    {
        var claimant = await Token("claimant");
        var staff = await Token("staff");
        var path = $"/claims/{await Submit(claimant)}";
        await CheckStatus(HttpMethod.Post, $"{path}/pick-up", staff, "UnderReview");

        var responses = await Task.WhenAll(
            Request(HttpMethod.Post, $"{path}/questions", staff, new { body = "First question" }),
            Request(HttpMethod.Post, $"{path}/questions", staff, new { body = "Second question" }));
        using var first = responses[0];
        using var second = responses[1];
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Conflict], responses.Select(response => response.StatusCode).Order());

        using var messages = await Request(HttpMethod.Get, $"{path}/messages", claimant);
        var message = Assert.Single((await Read(messages)).EnumerateArray());
        Assert.Equal(first.IsSuccessStatusCode ? "First question" : "Second question", message.GetProperty("body").GetString());
        using var history = await Request(HttpMethod.Get, $"{path}/history", claimant);
        var entry = Assert.Single((await Read(history)).EnumerateArray(), eventEntry =>
            eventEntry.GetProperty("action").GetString() == "QuestionAsked");
        Assert.Equal(message.GetProperty("id").GetInt64(), entry.GetProperty("claimMessageId").GetInt64());
    }

    private async Task<JsonElement> CheckStatus(HttpMethod method, string path, string token, string expected, string? message = null)
    {
        using var response = await Request(method, path, token, message is null ? null : new { body = message });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await Read(response);
        Assert.Equal(expected, result.GetProperty("status").GetString());
        return result;
    }

    private async Task ExpectStatus(HttpMethod method, string path, string token, HttpStatusCode expected, object? body = null)
    {
        using var response = await Request(method, path, token, body);
        Assert.Equal(expected, response.StatusCode);
    }

    private async Task<Guid> Submit(string token)
    {
        using var response = await Request(HttpMethod.Post, "/claims", token, ValidClaim());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var id = (await Read(response)).GetProperty("id").GetGuid();
        Assert.Equal($"/claims/{id}", response.Headers.Location?.OriginalString);
        return id;
    }

    private static object ValidClaim() => new
    {
        type = "Motor",
        incidentDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
        description = "Damage to the rear bumper."
    };

    private async Task<JsonElement> Login(string account)
    {
        using var response = await client.PostAsJsonAsync("/auth/login",
            new { email = $"{account}@example.test", password = ClaimsApiFactory.DemoPassword });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return await Read(response);
    }

    private async Task<string> Token(string account) => (await Login(account)).GetProperty("accessToken").GetString()!;

    private async Task<HttpResponseMessage> Request(HttpMethod method, string path, string? token = null, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await client.SendAsync(request);
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>();
}

public sealed class ClaimsApiFactory : WebApplicationFactory<Program>
{
    internal const string DemoPassword = "Fixture credential only 2026!";
    private readonly string database = Path.Combine(Path.GetTempPath(), $"chubb-claims-{Guid.NewGuid():N}.db");
    internal Dictionary<string, string?> Settings { get; init; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Claims"] = $"Data Source={database};Pooling=False",
            ["Demo:Password"] = DemoPassword
        }).AddInMemoryCollection(Settings));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        File.Delete(database);
    }
}
