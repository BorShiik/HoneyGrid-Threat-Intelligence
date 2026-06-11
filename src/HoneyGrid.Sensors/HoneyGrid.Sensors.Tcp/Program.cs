using HoneyGrid.Sensors.Common;
using HoneyGrid.Sensors.Tcp;

// HoneyGrid.Sensors.Tcp — generyczny nasłuch TCP (worker service).
// Niskointeraktywny honeypot: akceptuje połączenia na skonfigurowanych portach
// (np. 23/Telnet, 3389/RDP), wykonuje banner-grabbing i publikuje zdarzenia "connect".

var builder = Host.CreateApplicationBuilder(args);

// Bezkluczowy producent Event Hub (kanał + shipper + sink), sekcja "HoneyGrid".
builder.AddHoneyGridEventHub();

// Lista portów nasłuchu i parametry banner-grab z sekcji "TcpSensor".
builder.Services.AddOptions<TcpSensorOptions>()
    .Bind(builder.Configuration.GetSection(TcpSensorOptions.SectionName));

builder.Services.AddHostedService<TcpHoneypotWorker>();

var host = builder.Build();
host.Run();
