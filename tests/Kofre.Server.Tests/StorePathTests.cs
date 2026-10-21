namespace Kofre.Server.Tests;

public sealed class StorePathTests
{
    [Theory]
    [InlineData("data/3f/3f9c.bin", true)]
    [InlineData("header.json", true)]
    [InlineData("a_b-c.d", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("/a", false)]
    [InlineData("a/", false)]
    [InlineData("a/../b", false)]
    [InlineData("./a", false)]
    [InlineData("a\\b", false)]
    [InlineData("a:b", false)]
    [InlineData("a/b/c/d/e/f/g/h/i/j/k/l/m/n/o/p/q", false)]
    public void StorePaths_StayInTheirStore(string? path, bool valid) => Assert.Equal(valid, StorePath.IsValid(path));
}
