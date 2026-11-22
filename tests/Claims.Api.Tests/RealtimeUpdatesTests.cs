using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;

namespace Claims.Api.Tests;

public sealed class RealtimeUpdatesTests
{
    [Fact]
    public async Task Queue_events_reach_staff_and_claim_owner_and_one_pickup_wins()
    {
        await using var factory = new ClaimsApiFactory();
        using var api = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
        var claimant = await Login(api, "claimant");
        var stranger = await Login(api, "other-claimant");
        var staff = await Login(api, "staff");
        var thirdStaff = await Login(api, "third-staff");
        await using var ownerConnection = RealtimeConnection.Create(factory.Server, "/hubs/claims", claimant);
        await using var strangerConnection = RealtimeConnection.Create(factory.Server, "/hubs/claims", stranger);
        await using var staffConnection = RealtimeConnection.Create(factory.Server, "/hubs/claims", thirdStaff);
        var ownerUpdates = Channel.CreateUnbounded<bool>();
        var staffUpdates = Channel.CreateUnbounded<bool>();
        var strangerUpdates = Channel.CreateUnbounded<bool>();
        using var ownerListener = ownerConnection.On("ClaimsChanged", () => { ownerUpdates.Writer.TryWrite(true); });
        using var staffListener = staffConnection.On("ClaimsChanged", () => { staffUpdates.Writer.TryWrite(true); });
        using var strangerListener = strangerConnection.On("ClaimsChanged", () => { strangerUpdates.Writer.TryWrite(true); });
        await Task.WhenAll(ownerConnection.StartAsync(timeout.Token), strangerConnection.StartAsync(timeout.Token),
            staffConnection.StartAsync(timeout.Token));

        using var submitted = await Send(api, HttpMethod.Post, "/claims", claimant, new
        {
            type = "Motor",
            incidentDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)),
            description = "Parking damage."
        });
        Assert.Equal(HttpStatusCode.Created, submitted.StatusCode);
        var claim = await submitted.Content.ReadFromJsonAsync<JsonElement>(timeout.Token);
        var id = claim.GetProperty("id").GetGuid();
        await ownerUpdates.Reader.ReadAsync(timeout.Token);
        await staffUpdates.Reader.ReadAsync(timeout.Token);

        var results = await Task.WhenAll(
            Send(api, HttpMethod.Post, $"/claims/{id}/pick-up", staff),
            Send(api, HttpMethod.Post, $"/claims/{id}/pick-up", thirdStaff));
        using var first = results[0];
        using var second = results[1];
        Assert.Equal(new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }, results.Select(response => response.StatusCode).Order());
        var conflict = await Assert.Single(results, response => response.StatusCode == HttpStatusCode.Conflict)
            .Content.ReadFromJsonAsync<JsonElement>(timeout.Token);
        Assert.Contains("already been picked up", conflict.GetProperty("title").GetString());
        await ownerUpdates.Reader.ReadAsync(timeout.Token);
        await staffUpdates.Reader.ReadAsync(timeout.Token);
        Assert.False(strangerUpdates.Reader.TryRead(out _));

        var losingStaff = first.IsSuccessStatusCode ? thirdStaff : staff;
        using var denied = await Send(api, HttpMethod.Get, $"/claims/{id}", losingStaff);
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        using var queue = await Send(api, HttpMethod.Get, "/claims", losingStaff);
        var visible = await queue.Content.ReadFromJsonAsync<JsonElement>(timeout.Token);
        Assert.DoesNotContain(visible.EnumerateArray(), row => row.GetProperty("id").GetGuid() == id);
    }

    private static async Task<string> Login(HttpClient api, string account)
    {
        using var response = await api.PostAsJsonAsync("/auth/login",
            new { email = $"{account}@example.test", password = ClaimsApiFactory.DemoPassword });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("accessToken").GetString()!;
    }

    private static async Task<HttpResponseMessage> Send(HttpClient api, HttpMethod method, string path, string token,
        object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await api.SendAsync(request);
    }
}

internal static class RealtimeConnection
{
    public static HubConnection Create(TestServer server, string path, string token) =>
        new HubConnectionBuilder().WithUrl("https://localhost" + path, options =>
        {
            options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            options.HttpMessageHandlerFactory = _ => server.CreateHandler();
            options.Transports = HttpTransportType.LongPolling;
        }).Build();
}
