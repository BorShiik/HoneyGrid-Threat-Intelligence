using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Azure.Cosmos;
using Azure.Identity;
using HoneyGrid.Sensors.NodeMetrics;

var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((context, services) =>
    {
        var config = context.Configuration;
        services.AddSingleton(sp =>
        {
            var endpoint = config["CosmosEndpoint"];
            if (string.IsNullOrEmpty(endpoint))
            {
                throw new InvalidOperationException("Missing CosmosEndpoint in configuration.");
            }
            return new CosmosClient(endpoint, new DefaultAzureCredential());
        });
        
        services.AddHostedService<Worker>();
    })
    .Build();

await host.RunAsync();
