using HoneyGrid.Sensors.Common;
using HoneyGrid.Sensors.CowrieShipper;

// HoneyGrid.Sensors.CowrieShipper — most logu JSON Cowrie → Event Hub (worker service).
// Śledzi plik logu Cowrie (follow-file), mapuje zdarzenia na kontrakt HoneypotEvent
// i publikuje je przez IEventSink.

var builder = Host.CreateApplicationBuilder(args);

// Bezkluczowy producent Event Hub (kanał + shipper + sink), sekcja "HoneyGrid".
builder.AddHoneyGridEventHub();

// Ścieżka logu i parametry śledzenia z sekcji "CowrieShipper".
builder.Services.AddOptions<CowrieShipperOptions>()
    .Bind(builder.Configuration.GetSection(CowrieShipperOptions.SectionName));

// Uploader binarnych nagrań TTY do Blob (bezkluczowo). Pusty BlobServiceUri → no-op.
builder.Services.AddSingleton<TtyBlobUploader>();

builder.Services.AddHostedService<CowrieTailWorker>();

var host = builder.Build();
host.Run();
