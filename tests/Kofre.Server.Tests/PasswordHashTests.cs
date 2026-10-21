namespace Kofre.Server.Tests;

public sealed class PasswordHashTests
{
    [Fact]
    public void AHash_MatchesItsPasswordOnly()
    {
        var hash = PasswordHash.Create("correct horse", 10);

        Assert.True(PasswordHash.Verify("correct horse", hash));
        Assert.False(PasswordHash.Verify("wrong horse", hash));
        Assert.NotEqual(hash, PasswordHash.Create("correct horse", 10));
    }

    [Theory]
    [InlineData("")]
    [InlineData("plain")]
    [InlineData("md5$10$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2-sha256$0$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2-sha256$ten$c2FsdA==$aGFzaA==")]
    [InlineData("pbkdf2-sha256$10$not base64$aGFzaA==")]
    public void AHashItCannotRead_MatchesNothing(string stored) => Assert.False(PasswordHash.Verify("", stored));
}
