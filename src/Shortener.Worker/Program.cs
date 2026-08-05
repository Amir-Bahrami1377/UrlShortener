using Serilog;
using Shortener.Infrastructure;
using Shortener.Infrastructure.Observability;
using Shortener.Infrastructure.Security;
using Shortener.Worker.Workers;

// A WebApplication host (not the plain Generic Host) so this process can expose its own
// /health/live, /health/ready, and /metrics (§M8.2/M8.3) alongside its BackgroundServices —
// each deployable (Api, Admin, PublicWeb, Worker) independently health-checkable.
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) =>
    SerilogSetup.Configure(config, context.Configuration, context.HostingEnvironment, "Worker"));

builder.Services.AddInfrastructure(builder.Configuration, builder.Environment);
builder.Services.AddSingleton<SmsStreamEntryHandler>();
builder.Services.AddHostedService<OtpSmsWorker>();
builder.Services.AddHostedService<BulkSmsWorker>();
builder.Services.AddHostedService<OutboxPublisherService>();
builder.Services.AddHostedService<QueueMaintenanceService>();
builder.Services.AddHostedService<DeliveryStatusPollerService>();
builder.Services.AddHostedService<MaintenanceScheduler>();

builder.Services.AddCoreHealthChecks(builder.Configuration).AddOperationalHealthChecks();

var app = builder.Build();

app.Services.GetRequiredService<ShortenerMetrics>();

app.UseCorrelationId();
app.UseSecurityHeaders();

app.MapSystemHealthChecks();
app.MapShortenerMetrics();

app.Run();
