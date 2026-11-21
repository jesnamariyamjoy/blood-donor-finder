using Microsoft.EntityFrameworkCore;

class AppDb : DbContext
{
    public AppDb(DbContextOptions<AppDb> options) : base(options) { }

    public DbSet<Donor> Donors => Set<Donor>();
    public DbSet<BloodRequest> Requests => Set<BloodRequest>();
    public DbSet<Pledge> Pledges => Set<Pledge>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Donor>().HasIndex(d => d.Phone).IsUnique();
        b.Entity<BloodRequest>().HasMany(r => r.Pledges).WithOne()
            .HasForeignKey(p => p.RequestId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<Pledge>().HasIndex(p => new { p.RequestId, p.DonorId }).IsUnique();
    }
}

static class Seeder
{
    // Demo accounts: phone 9876500001..9876500006, password "demo1234"
    public static async Task SeedAsync(AppDb db)
    {
        if (await db.Donors.AnyAsync()) return;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var hash = PasswordHasher.Hash("demo1234");

        Donor D(string n, string bg, string city, string ph, int? daysAgo, double lat, double lng, int count) => new()
        {
            Name = n, BloodGroup = bg, City = city, Phone = ph, PasswordHash = hash,
            LastDonation = daysAgo is null ? null : today.AddDays(-daysAgo.Value),
            Lat = lat, Lng = lng, DonationCount = count
        };

        var donors = new[]
        {
            D("Anu Thomas", "O-", "Kochi", "9876500001", null, 9.9312, 76.2673, 0),
            D("Rahul Nair", "A+", "Kochi", "9876500002", 200, 9.9816, 76.2999, 6),
            D("Sneha Joseph", "B+", "Kochi", "9876500003", 30, 10.0159, 76.3419, 4),
            D("Meera Das", "B+", "Kozhikode", "9876500004", 400, 11.2588, 75.7804, 2),
            D("Arjun Menon", "AB+", "Thrissur", "9876500005", null, 10.5276, 76.2144, 0),
            D("Fathima Noor", "O+", "Thrissur", "9876500006", 120, 10.5167, 76.2100, 11),
        };
        db.Donors.AddRange(donors);
        db.Requests.Add(new BloodRequest
        {
            RequesterId = donors[0].Id, Patient = "Ravi K.", BloodGroup = "A+", Units = 2,
            Hospital = "City General Hospital", City = "Kochi", Contact = "9876511111",
            Urgency = "Critical", CreatedAt = DateTime.UtcNow.AddHours(-2)
        });
        await db.SaveChangesAsync();
    }
}
