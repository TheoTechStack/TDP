using System.ComponentModel.DataAnnotations;
using DisputePortal.Api.Infrastructure.Persistence;

namespace DisputePortal.Api.Tests.Infrastructure;

public sealed class SeedOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too-short")]
    public void Seeding_without_a_strong_demo_password_fails_validation(string? password)
    {
        var options = new SeedOptions { Enabled = true, DemoPassword = password };

        Assert.False(IsValid(options));
    }

    [Fact]
    public void Seeding_with_a_strong_demo_password_is_valid() =>
        Assert.True(IsValid(new SeedOptions { Enabled = true, DemoPassword = "a-long-enough-password" }));

    [Fact]
    public void Password_is_not_required_when_seeding_is_off() =>
        Assert.True(IsValid(new SeedOptions { Enabled = false, DemoPassword = null }));

    private static bool IsValid(SeedOptions options) =>
        Validator.TryValidateObject(options, new ValidationContext(options), [], validateAllProperties: true);
}
