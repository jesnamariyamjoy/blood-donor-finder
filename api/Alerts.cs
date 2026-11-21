using System.Net.Http.Headers;
using System.Text;
using Microsoft.EntityFrameworkCore;

interface ISmsSender
{
    Task SendAsync(string toE164, string message, CancellationToken ct = default);
}

// Default: just logs, so the app works with no SMS account
class ConsoleSmsSender(ILogger<ConsoleSmsSender> log) : ISmsSender
{
    public Task SendAsync(string to, string message, CancellationToken ct = default)
    {
        log.LogInformation("SMS to {To}: {Message}", to, message);
        return Task.CompletedTask;
    }
}

// Set Sms:Provider=Twilio and fill the Twilio section in appsettings (or env vars)
class TwilioSmsSender(HttpClient http, IConfiguration cfg, ILogger<TwilioSmsSender> log) : ISmsSender
{
    public async Task SendAsync(string to, string message, CancellationToken ct = default)
    {
        var sid = cfg["Twilio:AccountSid"];
        var token = cfg["Twilio:AuthToken"];
        var from = cfg["Twilio:From"] ?? "";

        var req = new HttpRequestMessage(HttpMethod.Post,
            $"https://api.twilio.com/2010-04-01/Accounts/{sid}/Messages.json")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            { ["To"] = to, ["From"] = from, ["Body"] = message })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes($"{sid}:{token}")));

        var res = await http.SendAsync(req, ct);
        if (!res.IsSuccessStatusCode) log.LogWarning("Twilio SMS failed with status {Status}", res.StatusCode);
    }
}

// Fire-and-forget notifications so API calls never wait on SMS
class AlertService(IServiceScopeFactory scopes, ISmsSender sms, IConfiguration cfg, ILogger<AlertService> log)
{
    string CountryCode => cfg["Sms:DefaultCountryCode"] ?? "+91";

    // Alert eligible, compatible donors in the request's city (max 50)
    public void NotifyNewRequest(Guid requestId) => _ = Task.Run(async () =>
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDb>();
            var r = await db.Requests.AsNoTracking().FirstOrDefaultAsync(x => x.Id == requestId);
            if (r is null) return;

            var compatible = BloodRules.CompatibleDonors[r.BloodGroup].ToArray();
            var city = r.City.ToLower();
            var candidates = await db.Donors.AsNoTracking()
                .Where(d => d.SmsAlerts && d.Available && d.Id != r.RequesterId
                            && compatible.Contains(d.BloodGroup) && d.City.ToLower() == city)
                .ToListAsync();
            var targets = candidates.Where(d => d.IsEligible()).Take(50).ToList();

            var text = $"Blood Donor Finder: {r.Urgency} - {r.Units} unit(s) of {r.BloodGroup} needed at " +
                       $"{r.Hospital}, {r.City}. Contact {r.Contact}.";
            foreach (var d in targets)
                await sms.SendAsync(Phones.ToE164(d.Phone, CountryCode), text);

            log.LogInformation("Sent {Count} alert(s) for request {Id}", targets.Count, requestId);
        }
        catch (Exception ex) { log.LogError(ex, "Failed to send request alerts"); }
    });

    public void Notify(string normalizedPhone, string message) => _ = Task.Run(async () =>
    {
        try { await sms.SendAsync(Phones.ToE164(normalizedPhone, CountryCode), message); }
        catch (Exception ex) { log.LogError(ex, "Failed to send SMS"); }
    });
}
