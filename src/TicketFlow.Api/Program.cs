using Serilog;
using TicketFlow.Api.Extensions;
using TicketFlow.Application;
using TicketFlow.Infrastructure;
using TicketFlow.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext());

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddApi(builder.Configuration);

var app = builder.Build();

app.UseApi();

await app.Services.InitializeDatabaseAsync();
await app.RunAsync();
