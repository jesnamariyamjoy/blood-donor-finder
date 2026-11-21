
class Donor
{
    public const int CooldownDays = 90;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Phone { get; set; } = "";          // normalised 10 digits, unique, used to log in
    public string PasswordHash { get; set; } = "";
    public string BloodGroup { get; set; } = "";
    public string City { get; set; } = "";
    public DateOnly? LastDonation { get; set; }
    public bool Available { get; set; } = true;
    public bool SmsAlerts { get; set; } = true;
    public double? Lat { get; set; }
    public double? Lng { get; set; }
    public int DonationCount { get; set; }

    public DateOnly? NextEligible => LastDonation?.AddDays(CooldownDays);

    public string Level => DonationCount switch
    {
        >= 10 => "Hero",
        >= 5 => "Gold",
        >= 3 => "Silver",
        >= 1 => "Bronze",
        _ => "New"
    };

    public bool IsEligible() =>
        Available && (NextEligible is null || NextEligible <= DateOnly.FromDateTime(DateTime.UtcNow));
}

class BloodRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequesterId { get; set; }
    public string Patient { get; set; } = "";
    public string BloodGroup { get; set; } = "";
    public int Units { get; set; }
    public string Hospital { get; set; } = "";
    public string City { get; set; } = "";
    public string Contact { get; set; } = "";
    public string Urgency { get; set; } = "Normal";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string Status { get; set; } = "Open";
    public List<Pledge> Pledges { get; set; } = new();
}

class Pledge
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RequestId { get; set; }
    public Guid DonorId { get; set; }
    public string DonorLabel { get; set; } = "";
}

// ---------- DTOs (never expose password hashes or private data) ----------

record DonorView(Guid Id, string Name, string BloodGroup, string City, string Phone, DateOnly? LastDonation,
                 DateOnly? NextEligible, bool Available, bool SmsAlerts, int DonationCount, string Level,
                 double? Lat, double? Lng)
{
    // isPublic hides health/location details; maskPhone hides the number from anonymous visitors
    public static DonorView From(Donor d, bool isPublic = false, bool maskPhone = false) =>
        new(d.Id, d.Name, d.BloodGroup, d.City, maskPhone ? Phones.Mask(d.Phone) : d.Phone,
            isPublic ? null : d.LastDonation, isPublic ? null : d.NextEligible, d.Available, d.SmsAlerts,
            d.DonationCount, d.Level, isPublic ? null : d.Lat, isPublic ? null : d.Lng);
}

record DonorMatch(DonorView Donor, double? DistanceKm, bool ExactMatch);

record RequestView(Guid Id, string Patient, string BloodGroup, int Units, string Hospital, string City,
                   string Contact, string Urgency, DateTime CreatedAt, string Status, List<string> Pledges, bool Mine)
{
    public static RequestView From(BloodRequest r, Guid? me, bool authed) =>
        new(r.Id, r.Patient, r.BloodGroup, r.Units, r.Hospital, r.City,
            authed ? r.Contact : Phones.Mask(r.Contact), r.Urgency, r.CreatedAt, r.Status,
            r.Pledges.Select(p => p.DonorLabel).ToList(), me is not null && r.RequesterId == me);
}

record AuthResponse(string Token, DonorView Profile);
record LoginInput(string? Phone, string? Password);

record RegisterInput(string? Name, string? BloodGroup, string? City, string? Phone, string? Password,
                     DateOnly? LastDonation, double? Lat, double? Lng)
{
    public Dictionary<string, string[]> Validate()
    {
        var e = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(Name)) e["name"] = ["Name is required."];
        if (string.IsNullOrWhiteSpace(City)) e["city"] = ["City is required."];
        if (Phone is null || Phone.Count(char.IsDigit) < 10) e["phone"] = ["A valid phone number is required."];
        if (Password is null || Password.Length < 8) e["password"] = ["Password must be at least 8 characters."];
        if (BloodGroup is null || !BloodRules.CompatibleDonors.ContainsKey(BloodGroup.ToUpperInvariant()))
            e["bloodGroup"] = ["Must be one of A+, A-, B+, B-, AB+, AB-, O+, O-."];
        if (LastDonation > DateOnly.FromDateTime(DateTime.UtcNow))
            e["lastDonation"] = ["Last donation cannot be in the future."];
        return e;
    }
}

record RequestInput(string? Patient, string? BloodGroup, int Units, string? Hospital, string? City,
                    string? Contact, string? Urgency)
{
    public Dictionary<string, string[]> Validate()
    {
        var e = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(Patient)) e["patient"] = ["Patient name is required."];
        if (string.IsNullOrWhiteSpace(Hospital)) e["hospital"] = ["Hospital is required."];
        if (string.IsNullOrWhiteSpace(City)) e["city"] = ["City is required."];
        if (Contact is null || Contact.Count(char.IsDigit) < 10) e["contact"] = ["A valid contact number is required."];
        if (Units is < 1 or > 10) e["units"] = ["Units must be between 1 and 10."];
        if (BloodGroup is null || !BloodRules.CompatibleDonors.ContainsKey(BloodGroup.ToUpperInvariant()))
            e["bloodGroup"] = ["Must be one of A+, A-, B+, B-, AB+, AB-, O+, O-."];
        if (Urgency is null || Array.IndexOf(BloodRules.Urgencies, Urgency) < 0)
            e["urgency"] = ["Must be Critical, Urgent or Normal."];
        return e;
    }
}

static class Phones
{
    public static string Normalize(string raw)
    {
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        return digits.Length > 10 ? digits[^10..] : digits;
    }
    public static string ToE164(string normalized, string countryCode) => countryCode + normalized;
    public static string Mask(string phone) =>
        phone.Length <= 4 ? "****" : new string('*', phone.Length - 4) + phone[^4..];
}

static class BloodRules
{
    public static readonly string[] Urgencies = ["Critical", "Urgent", "Normal"];

    // recipient -> donor groups that can safely give to them
    public static readonly Dictionary<string, HashSet<string>> CompatibleDonors = new()
    {
        ["O-"]  = ["O-"],
        ["O+"]  = ["O-", "O+"],
        ["A-"]  = ["O-", "A-"],
        ["A+"]  = ["O-", "O+", "A-", "A+"],
        ["B-"]  = ["O-", "B-"],
        ["B+"]  = ["O-", "O+", "B-", "B+"],
        ["AB-"] = ["O-", "A-", "B-", "AB-"],
        ["AB+"] = ["O-", "O+", "A-", "A+", "B-", "B+", "AB-", "AB+"],
    };
}

static class Geo
{
    // Haversine distance in km
    public static double Km(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371;
        static double Rad(double d) => d * Math.PI / 180;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * R * Math.Asin(Math.Sqrt(a));
    }
}
