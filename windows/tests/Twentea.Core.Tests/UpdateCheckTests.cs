using Twentea.Core;

namespace Twentea.Core.Tests;

public class UpdateCheckTests
{
    [Fact]
    public void NewerReleaseIsDetected()
    {
        Assert.True(UpdateCheck.IsNewer("v0.2.0", "0.1.0"));
        Assert.True(UpdateCheck.IsNewer("v0.10.0", "0.9.3"));
        Assert.True(UpdateCheck.IsNewer("v1.0.0-beta.1", "0.9"));
        Assert.False(UpdateCheck.IsNewer("v0.1.0", "0.1.0"));
        Assert.False(UpdateCheck.IsNewer("v0.1.0", "0.2.0"));
        Assert.False(UpdateCheck.IsNewer("nightly", "0.1.0"));
    }

    [Fact]
    public void UpdateAssetMatchesPlatform()
    {
        var names = new[] { "Twentea-0.2.0.dmg", "Twentea-0.2.0-mac.zip", "Twentea-0.2.0-win-x64-setup.exe",
                            "Twentea-0.2.0-win-arm64-setup.exe", "Twentea-0.2.0-win-x64.zip" };
        var release = new UpdateCheck.Release
        {
            TagName = "v0.2.0",
            Assets = names.Select(n => new UpdateCheck.Asset { Name = n, BrowserDownloadUrl = "https://example.com/" + n }).ToList(),
        };
        Assert.Equal("Twentea-0.2.0-win-x64-setup.exe", UpdateCheck.AssetFor(release, "win-x64")?.Name);
        Assert.Equal("Twentea-0.2.0-win-arm64-setup.exe", UpdateCheck.AssetFor(release, "win-arm64")?.Name);
    }
}
