using HoneyGrid.Sensors.Tcp;

// HoneyGrid.Sensors.Tcp — generyczny nasłuch TCP (worker service).
// Niskointeraktywny honeypot: akceptuje połączenia na skonfigurowanych portach
// (np. 23/Telnet, 3389/RDP), rejestruje banner-grabbing i próby połączeń.

var builder = Host.CreateApplicationBuilder(args);

// TODO (Track A, Tydzień 2): konfiguracja listy portów nasłuchu z appsettings.
// TODO (Track A, Tydzień 3): rejestracja EventHubProducerClient do publikacji zdarzeń "connect".
builder.Services.AddHostedService<TcpHoneypotWorker>();

var host = builder.Build();
host.Run();
