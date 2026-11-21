using Xunit;

public class BloodRulesTests
{
    [Fact]
    public void ONegative_patient_only_accepts_ONegative() =>
        Assert.Equal(new[] { "O-" }, BloodRules.CompatibleDonors["O-"].ToArray());

    [Fact]
    public void ABPositive_patient_accepts_every_group() =>
        Assert.Equal(8, BloodRules.CompatibleDonors["AB+"].Count);

    [Theory]
    [InlineData("A+"), InlineData("A-"), InlineData("B+"), InlineData("B-")]
    [InlineData("AB+"), InlineData("AB-"), InlineData("O+"), InlineData("O-")]
    public void Every_group_accepts_itself_and_ONegative(string group)
    {
        Assert.Contains(group, BloodRules.CompatibleDonors[group]);
        Assert.Contains("O-", BloodRules.CompatibleDonors[group]);
    }

    [Fact]
    public void Rh_positive_blood_is_never_given_to_Rh_negative_patients()
    {
        foreach (var neg in new[] { "A-", "B-", "AB-", "O-" })
            Assert.DoesNotContain(BloodRules.CompatibleDonors[neg], g => g.EndsWith('+'));
    }
}

public class DonorTests
{
    static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact] public void Never_donated_is_eligible() => Assert.True(new Donor().IsEligible());
    [Fact] public void Donated_10_days_ago_is_not_eligible() => Assert.False(new Donor { LastDonation = Today.AddDays(-10) }.IsEligible());
    [Fact] public void Donated_89_days_ago_is_not_eligible() => Assert.False(new Donor { LastDonation = Today.AddDays(-89) }.IsEligible());
    [Fact] public void Donated_90_days_ago_is_eligible() => Assert.True(new Donor { LastDonation = Today.AddDays(-90) }.IsEligible());
    [Fact] public void Unavailable_donor_is_not_eligible() => Assert.False(new Donor { Available = false }.IsEligible());

    [Theory]
    [InlineData(0, "New"), InlineData(1, "Bronze"), InlineData(3, "Silver")]
    [InlineData(5, "Gold"), InlineData(10, "Hero"), InlineData(25, "Hero")]
    public void Level_follows_donation_count(int count, string level) =>
        Assert.Equal(level, new Donor { DonationCount = count }.Level);
}

public class HelperTests
{
    [Fact]
    public void Distance_between_same_point_is_zero() =>
        Assert.Equal(0, Geo.Km(9.9312, 76.2673, 9.9312, 76.2673), 3);

    [Fact]
    public void Kochi_to_Thrissur_is_roughly_65_km()
    {
        var km = Geo.Km(9.9312, 76.2673, 10.5276, 76.2144);
        Assert.InRange(km, 60, 75);
    }

    [Fact]
    public void Password_hash_verifies_and_rejects_wrong_password()
    {
        var hash = PasswordHasher.Hash("correct-horse");
        Assert.True(PasswordHasher.Verify("correct-horse", hash));
        Assert.False(PasswordHasher.Verify("wrong-horse", hash));
    }

    [Fact]
    public void Same_password_gets_different_hashes() =>
        Assert.NotEqual(PasswordHasher.Hash("abc12345"), PasswordHasher.Hash("abc12345"));

    [Fact]
    public void Phone_numbers_are_normalised_and_masked()
    {
        Assert.Equal("9876500001", Phones.Normalize("+91 98765-00001"));
        Assert.Equal("+919876500001", Phones.ToE164("9876500001", "+91"));
        Assert.Equal("******0001", Phones.Mask("9876500001"));
    }

    [Fact]
    public void Registration_validation_catches_bad_input()
    {
        var errors = new RegisterInput("", "XX", "", "123", "short", null, null, null).Validate();
        foreach (var key in new[] { "name", "city", "phone", "password", "bloodGroup" })
            Assert.True(errors.ContainsKey(key), $"expected an error for {key}");
    }

    [Fact]
    public void Valid_request_input_passes_validation() =>
        Assert.Empty(new RequestInput("Ravi", "A+", 2, "City Hospital", "Kochi", "9876511111", "Critical").Validate());
}
