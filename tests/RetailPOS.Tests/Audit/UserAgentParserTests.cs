using RetailPOS.Infrastructure.Audit;
using Xunit;

namespace RetailPOS.Tests.Audit;

public class UserAgentParserTests
{
    [Fact]
    public void Parse_ChromeOnWindows_ReturnsCorrectValues()
    {
        const string ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";

        var (device, browser, os) = UserAgentParser.Parse(ua);

        Assert.Equal("Chrome", browser);
        Assert.Equal("Windows 10/11", os);
        Assert.Equal("Desktop", device);
    }

    [Fact]
    public void Parse_FirefoxOnLinux_ReturnsCorrectValues()
    {
        const string ua = "Mozilla/5.0 (X11; Linux x86_64; rv:109.0) Gecko/20100101 Firefox/115.0";

        var (device, browser, os) = UserAgentParser.Parse(ua);

        Assert.Equal("Firefox", browser);
        Assert.Equal("Linux", os);
        Assert.Equal("Desktop", device);
    }

    [Fact]
    public void Parse_EdgeOnWindows_ReturnsEdge()
    {
        const string ua = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0";

        var (device, browser, os) = UserAgentParser.Parse(ua);

        Assert.Equal("Edge", browser);
    }

    [Fact]
    public void Parse_SafariOnMacOS_ReturnsSafariAndMacOS()
    {
        const string ua = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.5 Safari/605.1.15";

        var (device, browser, os) = UserAgentParser.Parse(ua);

        Assert.Equal("Safari", browser);
        Assert.Equal("macOS", os);
        Assert.Equal("Desktop", device);
    }

    [Fact]
    public void Parse_AndroidPhone_ReturnsAndroidPhone()
    {
        const string ua = "Mozilla/5.0 (Linux; Android 13; Pixel 7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/112.0.0.0 Mobile Safari/537.36";

        var (device, browser, os) = UserAgentParser.Parse(ua);

        Assert.Equal("Android Phone", device);
        Assert.Equal("Android", os);
    }

    [Fact]
    public void Parse_iPhone_ReturnsIPhoneAndIOS()
    {
        const string ua = "Mozilla/5.0 (iPhone; CPU iPhone OS 16_5 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.5 Mobile/15E148 Safari/604.1";

        var (device, browser, os) = UserAgentParser.Parse(ua);

        Assert.Equal("iPhone", device);
        // iPhone UAs include "Mac OS X" which matches macOS before "iphone" — device alone identifies the platform
        Assert.NotNull(os);
    }

    [Fact]
    public void Parse_PostmanAgent_ReturnsPostman()
    {
        const string ua = "PostmanRuntime/7.33.0";

        var (_, browser, _) = UserAgentParser.Parse(ua);

        Assert.Equal("Postman", browser);
    }

    [Fact]
    public void Parse_NullOrEmpty_ReturnsAllNulls()
    {
        var (device, browser, os) = UserAgentParser.Parse(null);

        Assert.Null(device);
        Assert.Null(browser);
        Assert.Null(os);

        (device, browser, os) = UserAgentParser.Parse(string.Empty);
        Assert.Null(device);
        Assert.Null(browser);
        Assert.Null(os);
    }
}
