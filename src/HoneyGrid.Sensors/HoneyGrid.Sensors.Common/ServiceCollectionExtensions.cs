using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HoneyGrid.Sensors.Common;

/// <summary>
/// Rejestracja w DI bezkluczowego producenta Event Hub dla sensorów HoneyGrid.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Rejestruje opcje (<see cref="SensorOptions"/>), współdzielony kanał,
    /// implementację <see cref="IEventSink"/> oraz serwis w tle <see cref="EventHubShipper"/>.
    /// Wywołać raz w konfiguracji aplikacji-hosta sensora.
    /// </summary>
    public static IServiceCollection AddHoneyGridEventHub(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<SensorOptions>()
            .Bind(configuration.GetSection(SensorOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<HoneypotEventChannel>();
        services.AddSingleton<IEventSink, ChannelEventSink>();
        services.AddHostedService<EventHubShipper>();

        return services;
    }

    /// <summary>
    /// Wariant dla <see cref="IHostApplicationBuilder"/> (Minimal API / Worker).
    /// </summary>
    public static IHostApplicationBuilder AddHoneyGridEventHub(this IHostApplicationBuilder builder)
    {
        builder.Services.AddHoneyGridEventHub(builder.Configuration);
        return builder;
    }
}
