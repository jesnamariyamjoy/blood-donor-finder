using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

// ----- Services -----
builder.Services.AddDbContext<AppDb>((sp, o) =>
    o.UseSqlite(sp.GetRequiredService<IConfiguration>().GetConnectionString("Default")));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = cfg["Jwt:Issuer"],
        ValidateAudience = true, ValidAudience = cfg["Jwt:Audience"],
        ValidateLifetime = true, ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(cfg["Jwt:Key"]!)),
        ClockSkew = TimeSpan.FromMinutes(1)
    });
builder.Services.AddAuthorization();

if (cfg["Sms:Provider"] == "Twilio") builder.Services.AddHttpClient<ISmsSender, TwilioSmsSender>();
else builder.Services.AddSingleton<ISmsSender, ConsoleSmsSender>();
builder.Services.AddSingleton<AlertService>();

builder.WebHost.UseUrls("http://localhost:5000");
builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
    p.WithOrigins("http://localhost:5173").AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddFixedWindowLimiter("search", l => { l.PermitLimit = 30; l.Window = TimeSpan.FromMinutes(1); });
    o.AddFixedWindowLimiter("auth", l => { l.PermitLimit = 10; l.Window = TimeSpan.FromMinutes(1); }); // brute-force guard
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header, Name = "Authorization" });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() }
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    db.Database.EnsureCreated();
    if (app.Configuration.GetValue<bool>("SeedDemoData")) await Seeder.SeedAsync(db);
}

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseSwagger();
app.UseSwaggerUI();

// ----- Auth -----
app.MapPost("/auth/register", async (RegisterInput input, AppDb db, IConfiguration config) =>
{
    var errors = input.Validate();
    if (errors.Count > 0) return Results.ValidationProblem(errors);

    var phone = Phones.Normalize(input.Phone!);
    if (await db.Donors.AnyAsync(d => d.Phone == phone))
        return Results.Conflict("An account with this phone number already exists.");

    var donor = new Donor
    {
        Name = input.Name!.Trim(), Phone = phone, PasswordHash = PasswordHasher.Hash(input.Password!),
        BloodGroup = input.BloodGroup!.ToUpperInvariant(), City = input.City!.Trim(),
        LastDonation = input.LastDonation, Lat = input.Lat, Lng = input.Lng,
        DonationCount = input.LastDonation is null ? 0 : 1
    };
    db.Donors.Add(donor);
    await db.SaveChangesAsync();
    return Results.Ok(new AuthResponse(IssueToken(donor.Id, config), DonorView.From(donor)));
}).RequireRateLimiting("auth");

app.MapPost("/auth/login", async (LoginInput input, AppDb db, IConfiguration config) =>
{
    var phone = Phones.Normalize(input.Phone ?? "");
    var donor = await db.Donors.FirstOrDefaultAsync(d => d.Phone == phone);
    if (donor is null || !PasswordHasher.Verify(input.Password ?? "", donor.PasswordHash))
        return Results.Json("Invalid phone number or password.", statusCode: 401);
    return Results.Ok(new AuthResponse(IssueToken(donor.Id, config), DonorView.From(donor)));
}).RequireRateLimiting("auth");

// ----- My profile -----
app.MapGet("/me", async (ClaimsPrincipal u, AppDb db) =>
    await CurrentDonor(u, db) is { } d ? Results.Ok(DonorView.From(d)) : Results.NotFound())
    .RequireAuthorization();

app.MapPatch("/me/donation", async (ClaimsPrincipal u, AppDb db) =>
{
    var d = await CurrentDonor(u, db);
    if (d is null) return Results.NotFound();
    if (d.Available && !d.IsEligible()) return Results.BadRequest($"Next eligible date is {d.NextEligible}.");
    d.LastDonation = DateOnly.FromDateTime(DateTime.UtcNow);
    d.DonationCount++;
    await db.SaveChangesAsync();
    return Results.Ok(DonorView.From(d));
}).RequireAuthorization();

app.MapPatch("/me/availability", async (bool available, ClaimsPrincipal u, AppDb db) =>
{
    var d = await CurrentDonor(u, db);
    if (d is null) return Results.NotFound();
    d.Available = available;
    await db.SaveChangesAsync();
    return Results.Ok(DonorView.From(d));
}).RequireAuthorization();

app.MapPatch("/me/sms", async (bool enabled, ClaimsPrincipal u, AppDb db) =>
{
    var d = await CurrentDonor(u, db);
    if (d is null) return Results.NotFound();
    d.SmsAlerts = enabled;
    await db.SaveChangesAsync();
    return Results.Ok(DonorView.From(d));
}).RequireAuthorization();

app.MapDelete("/me", async (ClaimsPrincipal u, AppDb db) =>
{
    var d = await CurrentDonor(u, db);
    if (d is null) return Results.NotFound();
    await db.Pledges.Where(p => p.DonorId == d.Id).ExecuteDeleteAsync();
    db.Donors.Remove(d);
    await db.SaveChangesAsync();
    return Results.NoContent();
}).RequireAuthorization();

// ----- Smart search (public; contact details only for logged-in users) -----
app.MapGet("/search", async (string bloodGroup, string? city, double? lat, double? lng, double? radiusKm,
                             ClaimsPrincipal user, AppDb db) =>
{
    var needed = bloodGroup.ToUpperInvariant();
    if (!BloodRules.CompatibleDonors.TryGetValue(needed, out var groups))
        return Results.BadRequest($"Unknown blood group '{bloodGroup}'. Use A+, A-, B+, B-, AB+, AB-, O+, O-.");

    var geo = lat is not null && lng is not null;
    if (!geo && string.IsNullOrWhiteSpace(city))
        return Results.BadRequest("Provide a city, or share your location.");
    var radius = radiusKm ?? 25;

    var authed = user.Identity?.IsAuthenticated == true;
    var me = UserId(user);
    var compat = groups.ToArray();
    var candidates = await db.Donors.AsNoTracking()
        .Where(d => d.Available && compat.Contains(d.BloodGroup)).ToListAsync();

    var matches = candidates
        .Where(d => d.IsEligible() && d.Id != me)
        .Select(d => new DonorMatch(
            DonorView.From(d, isPublic: true, maskPhone: !authed),
            geo && d.Lat is not null && d.Lng is not null
                ? Math.Round(Geo.Km(lat!.Value, lng!.Value, d.Lat.Value, d.Lng.Value), 1)
                : null,
            d.BloodGroup == needed))
        .Where(m => geo
            ? m.DistanceKm is not null && m.DistanceKm <= radius
            : m.Donor.City.Equals(city!.Trim(), StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(m => m.ExactMatch)
        .ThenBy(m => m.DistanceKm ?? double.MaxValue)
        .ToList();

    return Results.Ok(new { needed, count = matches.Count, radiusKm = geo ? radius : (double?)null, matches });
}).RequireRateLimiting("search");

// ----- Emergency requests -----
app.MapPost("/requests", async (RequestInput input, ClaimsPrincipal u, AppDb db, AlertService alerts) =>
{
    var errors = input.Validate();
    if (errors.Count > 0) return Results.ValidationProblem(errors);

    var r = new BloodRequest
    {
        RequesterId = UserId(u)!.Value, Patient = input.Patient!.Trim(), BloodGroup = input.BloodGroup!.ToUpperInvariant(),
        Units = input.Units, Hospital = input.Hospital!.Trim(), City = input.City!.Trim(),
        Contact = Phones.Normalize(input.Contact!), Urgency = input.Urgency!
    };
    db.Requests.Add(r);
    await db.SaveChangesAsync();
    alerts.NotifyNewRequest(r.Id); // SMS eligible donors in the city, in the background
    return Results.Created($"/requests/{r.Id}", RequestView.From(r, r.RequesterId, true));
}).RequireAuthorization();

app.MapGet("/requests", async (string? city, ClaimsPrincipal user, AppDb db) =>
{
    var me = UserId(user);
    var authed = user.Identity?.IsAuthenticated == true;
    var open = await db.Requests.AsNoTracking().Include(r => r.Pledges)
        .Where(r => r.Status == "Open").ToListAsync();

    return open
        .Where(r => string.IsNullOrWhiteSpace(city) || r.City.Equals(city.Trim(), StringComparison.OrdinalIgnoreCase))
        .OrderBy(r => Array.IndexOf(BloodRules.Urgencies, r.Urgency))
        .ThenByDescending(r => r.CreatedAt)
        .Select(r => RequestView.From(r, me, authed));
});

app.MapPost("/requests/{id:guid}/pledge", async (Guid id, ClaimsPrincipal u, AppDb db, AlertService alerts) =>
{
    var r = await db.Requests.Include(x => x.Pledges).FirstOrDefaultAsync(x => x.Id == id);
    if (r is null) return Results.NotFound("Request not found.");
    var d = await CurrentDonor(u, db);
    if (d is null) return Results.NotFound("Donor profile not found.");

    if (r.Status != "Open") return Results.BadRequest("This request is already fulfilled.");
    if (r.RequesterId == d.Id) return Results.BadRequest("You can't pledge to your own request.");
    if (!BloodRules.CompatibleDonors[r.BloodGroup].Contains(d.BloodGroup))
        return Results.BadRequest($"{d.BloodGroup} blood cannot be given to a {r.BloodGroup} patient.");
    if (!d.IsEligible())
        return Results.BadRequest(d.Available ? $"You can donate again from {d.NextEligible}." : "You are marked unavailable.");

    if (r.Pledges.All(p => p.DonorId != d.Id))
    {
        db.Pledges.Add(new Pledge {  RequestId = r.Id, DonorId = d.Id, DonorLabel = $"{d.Name.Split(' ')[0]} ({d.BloodGroup})" });
        await db.SaveChangesAsync();
        alerts.Notify(Phones.Normalize(r.Contact),
            $"Blood Donor Finder: {d.Name} ({d.BloodGroup}) pledged to donate for {r.Patient}. Call {d.Phone}.");
    }
    return Results.Ok(RequestView.From(r, d.Id, true));
}).RequireAuthorization();

app.MapPatch("/requests/{id:guid}/fulfill", async (Guid id, ClaimsPrincipal u, AppDb db) =>
{
    var r = await db.Requests.Include(x => x.Pledges).FirstOrDefaultAsync(x => x.Id == id);
    if (r is null) return Results.NotFound();
    if (r.RequesterId != UserId(u)) return Results.Json("Only the person who posted this request can close it.", statusCode: 403);
    r.Status = "Fulfilled";
    await db.SaveChangesAsync();
    return Results.Ok(RequestView.From(r, r.RequesterId, true));
}).RequireAuthorization();

// ----- Impact dashboard -----
app.MapGet("/stats", async (AppDb db) =>
{
    var donors = await db.Donors.AsNoTracking().ToListAsync();
    var open = await db.Requests.AsNoTracking().Where(r => r.Status == "Open").ToListAsync();

    var groups = BloodRules.CompatibleDonors.Keys.Select(g =>
    {
        var demand = open.Where(r => r.BloodGroup == g).Sum(r => r.Units);
        var supply = donors.Count(d => d.IsEligible() && BloodRules.CompatibleDonors[g].Contains(d.BloodGroup));
        return new
        {
            bloodGroup = g,
            donors = donors.Count(d => d.BloodGroup == g),
            eligible = donors.Count(d => d.BloodGroup == g && d.IsEligible()),
            openDemandUnits = demand,
            compatibleSupply = supply,
            shortage = demand > supply
        };
    }).ToList();

    return new
    {
        totalDonors = donors.Count,
        eligibleNow = donors.Count(d => d.IsEligible()),
        totalDonations = donors.Sum(d => d.DonationCount),
        openRequests = open.Count,
        byBloodGroup = groups
    };
});

app.Run();

// ----- Helpers -----
static Guid? UserId(ClaimsPrincipal u) =>
    Guid.TryParse(u.FindFirst("uid")?.Value, out var id) ? id : null;

static async Task<Donor?> CurrentDonor(ClaimsPrincipal u, AppDb db) =>
    UserId(u) is { } id ? await db.Donors.FirstOrDefaultAsync(d => d.Id == id) : null;

static string IssueToken(Guid id, IConfiguration c)
{
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(c["Jwt:Key"]!));
    var token = new JwtSecurityToken(
        issuer: c["Jwt:Issuer"], audience: c["Jwt:Audience"],
        claims: [new Claim("uid", id.ToString())],
        expires: DateTime.UtcNow.AddDays(7),
        signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
    return new JwtSecurityTokenHandler().WriteToken(token);
}

public partial class Program { } // lets integration tests boot the app
