using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

// Boots the real app against a throw-away SQLite file for each test
public class ApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    readonly WebApplicationFactory<Program> _factory;

    public ApiTests(WebApplicationFactory<Program> factory)
    {
        var dbFile = Path.Combine(Path.GetTempPath(), $"bdf-test-{Guid.NewGuid():N}.db");
        _factory = factory.WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Default", $"Data Source={dbFile}");
            b.UseSetting("SeedDemoData", "false");
        });
    }

    async Task<HttpClient> RegisterAsync(string phone, string group, string city = "Testville")
    {
        var client = _factory.CreateClient();
        var res = await client.PostAsJsonAsync("/auth/register",
            new { name = "Test User", bloodGroup = group, city, phone, password = "password123" });
        res.EnsureSuccessStatusCode();
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", json.GetProperty("token").GetString());
        return client;
    }

    [Fact]
    public async Task Me_requires_a_token()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().GetAsync("/me")).StatusCode);
        var authed = await RegisterAsync("9000000001", "O-");
        Assert.Equal(HttpStatusCode.OK, (await authed.GetAsync("/me")).StatusCode);
    }

    [Fact]
    public async Task Search_masks_phone_numbers_for_anonymous_users_only()
    {
        await RegisterAsync("9000000001", "O-");
        var seeker = await RegisterAsync("9000000002", "A+");
        var url = $"/search?bloodGroup={Uri.EscapeDataString("O+")}&city=Testville";

        var anon = await _factory.CreateClient().GetFromJsonAsync<JsonElement>(url);
        var anonPhone = anon.GetProperty("matches")[0].GetProperty("donor").GetProperty("phone").GetString()!;
        Assert.Contains('*', anonPhone);

        var authed = await seeker.GetFromJsonAsync<JsonElement>(url);
        var phone = authed.GetProperty("matches")[0].GetProperty("donor").GetProperty("phone").GetString();
        Assert.Equal("9000000001", phone);
    }

    [Fact]
    public async Task Posting_a_request_requires_login()
    {
        var body = new { patient = "P", bloodGroup = "A+", units = 2, hospital = "H", city = "Testville", contact = "9000000009", urgency = "Urgent" };
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateClient().PostAsJsonAsync("/requests", body)).StatusCode);

        var user = await RegisterAsync("9000000003", "A+");
        Assert.Equal(HttpStatusCode.Created, (await user.PostAsJsonAsync("/requests", body)).StatusCode);
    }

    [Fact]
    public async Task Compatible_donor_can_pledge_and_incompatible_cannot()
    {
        var seeker = await RegisterAsync("9000000004", "A+");
        var good = await RegisterAsync("9000000005", "O-");
        var bad = await RegisterAsync("9000000006", "B+");

        var created = await seeker.PostAsJsonAsync("/requests",
            new { patient = "P", bloodGroup = "A+", units = 1, hospital = "H", city = "Testville", contact = "9000000004", urgency = "Critical" });
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var ok = await good.PostAsync($"/requests/{id}/pledge", null);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var view = await ok.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, view.GetProperty("pledges").GetArrayLength());

        Assert.Equal(HttpStatusCode.BadRequest, (await bad.PostAsync($"/requests/{id}/pledge", null)).StatusCode);
    }
}
