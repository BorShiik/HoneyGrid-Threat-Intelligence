using HoneyGrid.Contracts;
using HoneyGrid.Sensors.Web;

namespace HoneyGrid.Sensors.Tests;

/// <summary>Testy ekstrakcji poświadczeń i budowy zdarzeń honeypota webowego.</summary>
public sealed class WebHoneypotLogicTests
{
    [Fact]
    public void Wyciaga_poswiadczenia_z_pol_WordPress()
    {
        var form = new Dictionary<string, string?>
        {
            ["log"] = "admin",
            ["pwd"] = "haslo123",
            ["wp-submit"] = "Log In",
        };

        var creds = WebHoneypotLogic.ExtractCredentials(form);

        Assert.NotNull(creds);
        Assert.Equal("admin", creds!.Username);
        Assert.Equal("haslo123", creds.Password);
    }

    [Fact]
    public void Wyciaga_poswiadczenia_z_pol_phpMyAdmin()
    {
        var form = new Dictionary<string, string?>
        {
            ["pma_username"] = "root",
            ["pma_password"] = "toor",
        };

        var creds = WebHoneypotLogic.ExtractCredentials(form);

        Assert.Equal("root", creds!.Username);
        Assert.Equal("toor", creds.Password);
    }

    [Fact]
    public void Zwraca_null_gdy_brak_pol_poswiadczen()
    {
        var form = new Dictionary<string, string?>
        {
            ["csrf"] = "xyz",
            ["submit"] = "go",
        };

        Assert.Null(WebHoneypotLogic.ExtractCredentials(form));
    }

    [Fact]
    public void BuildLoginFailedEvent_ustawia_typ_i_poswiadczenia()
    {
        var creds = new CredentialPair { Username = "u", Password = "p" };
        var evt = WebHoneypotLogic.BuildLoginFailedEvent(
            "web-01", "8.8.8.8", "/wp-login.php", "curl/8.0", creds, DateTimeOffset.UtcNow);

        Assert.Equal(EventType.LoginFailed, evt.EventType);
        Assert.Equal(SensorType.Web, evt.SensorType);
        Assert.Equal("8.8.8.8", evt.AttackerIp);
        Assert.Equal(creds, evt.Credentials);
        Assert.Equal("/wp-login.php", evt.Http!.Path);
    }

    [Fact]
    public void BuildHttpRequestEvent_ustawia_metode_i_sciezke()
    {
        var evt = WebHoneypotLogic.BuildHttpRequestEvent(
            "web-01", "9.9.9.9", "GET", "/.env", "nmap", DateTimeOffset.UtcNow);

        Assert.Equal(EventType.HttpRequest, evt.EventType);
        Assert.Equal("GET", evt.Http!.Method);
        Assert.Equal("/.env", evt.Http.Path);
        Assert.Equal("nmap", evt.Http.UserAgent);
    }
}
