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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Claims"] = $"Data Source={database};Pooling=False",
            ["Demo:Password"] = DemoPassword
        }));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        File.Delete(database);
    }
}
