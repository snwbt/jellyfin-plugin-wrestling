using Jellyfin.Plugin.Wrestling.Services;
using Xunit;

namespace Jellyfin.Plugin.Wrestling.Tests;

public class LookupKeyTests
{
    [Fact]
    public void Build_NormalizesTitleAndPrefersFullDate()
    {
        var key = LookupKey.Build("AEW: Full Gear!", 2021, new DateTime(2021, 11, 13));

        Assert.Equal("aew full gear|2021-11-13", key);
    }

    [Fact]
    public void Build_UsesYearWhenDateIsMissing()
    {
        var key = LookupKey.Build("WrestleMania X-Seven", 2001, null);

        Assert.Equal("wrestlemania x seven|2001", key);
    }
}
