namespace You.Tests;

using You.Library;

public class BrowserInputLogicTests
{
    [Theory]
    [InlineData("twitter.com", true)]
    [InlineData("http://twitter.com", true)]
    [InlineData("https://twitter.com", true)]
    [InlineData("https://twitter.com/", true)]
    [InlineData("  https://twitter.com/  ", true)]
    [InlineData("x.com", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void UrlTextMatchesTarget_NormalizesCommonForms(string? input, bool expected)
    {
        var isMatch = BrowserInputLogic.UrlTextMatchesTarget(input, "twitter.com");
        Assert.Equal(expected, isMatch);
    }

    [Theory]
    [InlineData('a', true, (byte)'A')]
    [InlineData('z', true, (byte)'Z')]
    [InlineData('A', true, (byte)'A')]
    [InlineData('Z', true, (byte)'Z')]
    [InlineData('.', true, (byte)0xBE)]
    [InlineData('1', false, (byte)0)]
    [InlineData('-', false, (byte)0)]
    public void TryGetVirtualKeyForCharacter_MapsExpectedKeys(char input, bool expectedSuccess, byte expectedVirtualKey)
    {
        var success = BrowserInputLogic.TryGetVirtualKeyForCharacter(input, out var actualVirtualKey);
        Assert.Equal(expectedSuccess, success);
        Assert.Equal(expectedVirtualKey, actualVirtualKey);
    }

    [Fact]
    public void EaseInOutCubic_ReturnsExpectedAnchorPoints()
    {
        Assert.Equal(0.0, BrowserInputLogic.EaseInOutCubic(0.0), 12);
        Assert.Equal(0.5, BrowserInputLogic.EaseInOutCubic(0.5), 12);
        Assert.Equal(1.0, BrowserInputLogic.EaseInOutCubic(1.0), 12);
    }

    [Fact]
    public void EaseInOutCubic_IsMonotonicOnZeroToOne()
    {
        var previous = BrowserInputLogic.EaseInOutCubic(0.0);

        for (var i = 1; i <= 100; i++)
        {
            var t = i / 100.0;
            var current = BrowserInputLogic.EaseInOutCubic(t);
            Assert.True(current >= previous, $"Expected monotonic increase at t={t}, prev={previous}, current={current}");
            previous = current;
        }
    }
}
