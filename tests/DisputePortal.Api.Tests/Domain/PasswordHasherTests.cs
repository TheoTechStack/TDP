using DisputePortal.Api.Infrastructure.Security;

namespace DisputePortal.Api.Tests.Domain;

public sealed class PasswordHasherTests
{
    [Fact]
    public void Verify_accepts_the_original_password_and_rejects_others()
    {
        var hash = PasswordHasher.Hash("correct horse battery staple");

        Assert.True(PasswordHasher.Verify("correct horse battery staple", hash));
        Assert.False(PasswordHasher.Verify("Correct horse battery staple", hash));
    }

    [Fact]
    public void Hashing_is_salted()
    {
        Assert.NotEqual(PasswordHasher.Hash("same"), PasswordHasher.Hash("same"));
    }

    [Fact]
    public void Verify_rejects_malformed_hashes()
    {
        Assert.False(PasswordHasher.Verify("x", "not-a-hash"));
    }
}
