using HoneyGrid.Stix;

namespace HoneyGrid.Stix.Tests;

/// <summary>Testy buildera języka wzorców STIX 2.1 — dokładny kształt łańcuchów.</summary>
public sealed class StixPatternTests
{
    [Fact]
    public void Ipv4_EmitsExactComparisonExpression()
    {
        Assert.Equal("[ipv4-addr:value = '203.0.113.45']", StixPattern.Ipv4("203.0.113.45"));
    }

    [Fact]
    public void FileSha256_EmitsHashPathWithQuotedAlgorithm()
    {
        const string hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        Assert.Equal($"[file:hashes.'SHA-256' = '{hash}']", StixPattern.FileSha256(hash));
    }

    [Fact]
    public void UserAccount_EmitsAccountLoginExpression()
    {
        Assert.Equal("[user-account:account_login = 'admin']", StixPattern.UserAccount("admin"));
    }

    [Fact]
    public void FollowedByWithin_ComposesTemporalOperator()
    {
        var a = StixPattern.UserAccount("admin");
        var b = StixPattern.Ipv4("203.0.113.45");
        Assert.Equal(
            "[user-account:account_login = 'admin'] FOLLOWEDBY [ipv4-addr:value = '203.0.113.45'] WITHIN 1 MINUTES",
            StixPattern.FollowedByWithin(a, b, 1, "MINUTES"));
    }

    [Fact]
    public void Repeats_WrapsPatternInParensWithTimesQualifier()
    {
        var p = StixPattern.UserAccount("root");
        Assert.Equal("([user-account:account_login = 'root']) REPEATS 5 TIMES", StixPattern.Repeats(p, 5));
    }

    [Fact]
    public void EscapeValue_EscapesSingleQuote()
    {
        Assert.Equal("[user-account:account_login = 'o\\'brien']", StixPattern.UserAccount("o'brien"));
    }

    [Fact]
    public void EscapeValue_EscapesBackslash()
    {
        Assert.Equal(@"[user-account:account_login = 'domain\\user']", StixPattern.UserAccount(@"domain\user"));
    }
}
