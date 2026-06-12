using System.Net;
using HoneyGrid.Sensors.Web;

namespace HoneyGrid.Sensors.Tests;

/// <summary>
/// Testy rozwiązywania prawdziwego adresu atakującego za ingressem Container Apps:
/// parsowanie X-Forwarded-For, odporność na spoofing, normalizacja ::ffff:
/// i rozpoznawanie zaufanych podsieci proxy.
/// </summary>
public sealed class AttackerIpResolutionTests
{
    private static readonly IReadOnlyList<IPNetwork> ContainerAppsNetworks =
        WebHoneypotLogic.ParseTrustedNetworks(["100.100.0.0/16"]);

    // ---- ResolveAttackerIp: parsowanie X-Forwarded-For ----

    [Fact]
    public void Pojedynczy_adres_w_XFF_za_zaufanym_proxy()
    {
        var ip = WebHoneypotLogic.ResolveAttackerIp("203.0.113.7", "100.100.0.12", remoteIsTrustedProxy: true);

        Assert.Equal("203.0.113.7", ip);
    }

    [Fact]
    public void Lista_w_XFF_bierze_ostatni_element_dodany_przez_zaufane_proxy()
    {
        // Envoy dopisuje klienta na koniec — wcześniejsze elementy mógł sfałszować atakujący.
        var ip = WebHoneypotLogic.ResolveAttackerIp(
            "10.0.0.1, 198.51.100.4, 203.0.113.7", "100.100.0.12", remoteIsTrustedProxy: true);

        Assert.Equal("203.0.113.7", ip);
    }

    [Fact]
    public void Spoofing_XFF_przez_atakujacego_nie_podmienia_adresu()
    {
        // Atakujący wysłał własny nagłówek "X-Forwarded-For: 1.2.3.4",
        // a envoy dopisał jego prawdziwy adres na końcu.
        var ip = WebHoneypotLogic.ResolveAttackerIp(
            "1.2.3.4, 203.0.113.7", "100.100.0.12", remoteIsTrustedProxy: true);

        Assert.Equal("203.0.113.7", ip);
    }

    [Fact]
    public void Naglowek_ignorowany_gdy_polaczenie_nie_jest_z_zaufanego_proxy()
    {
        // Bezpośrednie połączenie z internetu z podstawionym XFF — wierzymy tylko adresowi połączenia.
        var ip = WebHoneypotLogic.ResolveAttackerIp("1.2.3.4", "203.0.113.7", remoteIsTrustedProxy: false);

        Assert.Equal("203.0.113.7", ip);
    }

    [Fact]
    public void Brak_naglowka_daje_adres_polaczenia()
    {
        var ip = WebHoneypotLogic.ResolveAttackerIp(null, "203.0.113.7", remoteIsTrustedProxy: true);

        Assert.Equal("203.0.113.7", ip);
    }

    [Fact]
    public void Pusty_naglowek_daje_adres_polaczenia()
    {
        var ip = WebHoneypotLogic.ResolveAttackerIp("   ", "203.0.113.7", remoteIsTrustedProxy: true);

        Assert.Equal("203.0.113.7", ip);
    }

    [Fact]
    public void Smieci_w_XFF_daja_adres_polaczenia()
    {
        var ip = WebHoneypotLogic.ResolveAttackerIp("nie-adres-ip", "100.100.0.12", remoteIsTrustedProxy: true);

        Assert.Equal("100.100.0.12", ip);
    }

    [Fact]
    public void Brak_naglowka_i_adresu_polaczenia_daje_unknown()
    {
        var ip = WebHoneypotLogic.ResolveAttackerIp(null, null, remoteIsTrustedProxy: false);

        Assert.Equal("unknown", ip);
    }

    // ---- Normalizacja adresów IPv4-mapped (::ffff:) ----

    [Fact]
    public void Usuwa_prefiks_ffff_z_adresu_polaczenia()
    {
        var ip = WebHoneypotLogic.ResolveAttackerIp(null, "::ffff:203.0.113.7", remoteIsTrustedProxy: false);

        Assert.Equal("203.0.113.7", ip);
    }

    [Fact]
    public void Usuwa_prefiks_ffff_z_elementu_XFF()
    {
        var ip = WebHoneypotLogic.ResolveAttackerIp("::ffff:203.0.113.7", "100.100.0.12", remoteIsTrustedProxy: true);

        Assert.Equal("203.0.113.7", ip);
    }

    [Theory]
    [InlineData("::ffff:1.2.3.4", "1.2.3.4")]
    [InlineData("1.2.3.4", "1.2.3.4")]
    [InlineData("2001:db8::1", "2001:db8::1")]
    [InlineData("nie-adres", "nie-adres")]
    public void NormalizeIp_zmienia_tylko_adresy_IPv4_mapped(string input, string expected)
    {
        Assert.Equal(expected, WebHoneypotLogic.NormalizeIp(input));
    }

    // ---- IsTrustedProxy: rozpoznawanie podsieci ingressu ----

    [Fact]
    public void Adres_z_podsieci_Container_Apps_jest_zaufany()
    {
        Assert.True(WebHoneypotLogic.IsTrustedProxy(IPAddress.Parse("100.100.0.5"), ContainerAppsNetworks));
    }

    [Fact]
    public void Adres_IPv4_mapped_z_podsieci_Container_Apps_jest_zaufany()
    {
        Assert.True(WebHoneypotLogic.IsTrustedProxy(IPAddress.Parse("::ffff:100.100.0.5"), ContainerAppsNetworks));
    }

    [Fact]
    public void Adres_publiczny_nie_jest_zaufany()
    {
        Assert.False(WebHoneypotLogic.IsTrustedProxy(IPAddress.Parse("203.0.113.7"), ContainerAppsNetworks));
    }

    [Fact]
    public void Loopback_jest_zawsze_zaufany()
    {
        Assert.True(WebHoneypotLogic.IsTrustedProxy(IPAddress.Loopback, ContainerAppsNetworks));
        Assert.True(WebHoneypotLogic.IsTrustedProxy(IPAddress.IPv6Loopback, ContainerAppsNetworks));
    }

    [Fact]
    public void Brak_adresu_nie_jest_zaufany()
    {
        Assert.False(WebHoneypotLogic.IsTrustedProxy(null, ContainerAppsNetworks));
    }

    [Fact]
    public void ParseTrustedNetworks_pomija_niepoprawne_wpisy()
    {
        var networks = WebHoneypotLogic.ParseTrustedNetworks(["100.100.0.0/16", "zepsute", "10.0.0.0/8"]);

        Assert.Equal(2, networks.Count);
    }
}
