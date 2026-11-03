using Carvis.Core.Configuration;
using Carvis.Core.Context;

namespace Carvis.Tests.Context;

public class SystemContextProviderTests
{
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now.ToUniversalTime();
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }

    [Fact]
    public async Task GetContextAsync_IncludesDateUserAndFolders()
    {
        var folders = new UserFolders(new Dictionary<string, string> { ["Escritorio"] = @"C:\Users\Carlos\Desktop" });
        var provider = new SystemContextProvider(
            new FixedTime(new DateTimeOffset(2026, 10, 1, 18, 22, 0, TimeSpan.Zero)),
            new AssistantSettings { UserName = "Carlos" },
            folders);

        var text = (await provider.GetContextAsync("hola")).Single().Content;

        Assert.Contains("jueves, 1 de octubre de 2026, 18:22", text);
        Assert.Contains("Usuario: Carlos", text);
        Assert.Contains(@"Escritorio: C:\Users\Carlos\Desktop", text);
    }

    [Theory]
    [InlineData("escritorio")]
    [InlineData("Desktop")]
    [InlineData("/Escritorio/")]
    public void UserFolders_ResolveSpanishAndEnglishNames(string name)
    {
        var folders = new UserFolders(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Escritorio"] = "D" });

        Assert.Equal("D", folders.Resolve(name));
    }
}
