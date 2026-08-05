using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shortener.Application.Abstractions;
using Shortener.Domain.Enums;
using Shortener.Infrastructure.Persistence;
using Shortener.Infrastructure.Queueing;
using StackExchange.Redis;

namespace Shortener.Infrastructure.Observability;

/// <summary>§M8.2 — the doc's nine required metrics, all on one Meter so a single Prometheus
/// exporter instance picks all of them up. Counters/histograms are recorded inline by callers;
/// the three gauges (sms_queue_lag_seconds, disk_used_percent, files_pending_delete) are
/// ObservableGauges evaluated at scrape time using StackExchange.Redis/EF Core's synchronous APIs —
/// System.Diagnostics.Metrics gauge callbacks have no async overload.</summary>
public sealed class ShortenerMetrics
{
    public const string MeterName = "Shortener";

    private readonly Counter<long> _linksCreated;
    private readonly Histogram<long> _fileUploadBytes;
    private readonly Counter<long> _otpRequested;
    private readonly Counter<long> _otpVerify;
    private readonly Counter<long> _downloads;
    private readonly Counter<long> _smsSent;

    public ShortenerMetrics(
        IMeterFactory meterFactory,
        IFileStorage fileStorage,
        IConnectionMultiplexer redis,
        IOptions<QueueOptions> queueOptions,
        IServiceScopeFactory scopeFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _linksCreated = meter.CreateCounter<long>("links_created_total");
        _fileUploadBytes = meter.CreateHistogram<long>("file_upload_bytes");
        _otpRequested = meter.CreateCounter<long>("otp_requested_total");
        _otpVerify = meter.CreateCounter<long>("otp_verify_total");
        _downloads = meter.CreateCounter<long>("downloads_total");
        _smsSent = meter.CreateCounter<long>("sms_sent_total");

        meter.CreateObservableGauge("disk_used_percent", () => fileStorage.GetDiskSpace().UsedPercent);
        meter.CreateObservableGauge("sms_queue_lag_seconds", () => ObserveQueueLag(redis, queueOptions.Value));
        meter.CreateObservableGauge("files_pending_delete", () => ObserveFilesPendingDelete(scopeFactory));
    }

    public void LinkCreated(int clientId) =>
        _linksCreated.Add(1, new KeyValuePair<string, object?>("client", clientId));

    public void FileUploadBytes(int clientId, long bytes) =>
        _fileUploadBytes.Record(bytes, new KeyValuePair<string, object?>("client", clientId));

    public void OtpRequested(int clientId) =>
        _otpRequested.Add(1, new KeyValuePair<string, object?>("client", clientId));

    public void OtpVerify(string result) =>
        _otpVerify.Add(1, new KeyValuePair<string, object?>("result", result));

    public void Download(int clientId) =>
        _downloads.Add(1, new KeyValuePair<string, object?>("client", clientId));

    public void SmsSent(string type, string provider, string result) =>
        _smsSent.Add(1,
            new KeyValuePair<string, object?>("type", type),
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("result", result));

    private static IEnumerable<Measurement<double>> ObserveQueueLag(IConnectionMultiplexer redis, QueueOptions options)
    {
        var db = redis.GetDatabase();
        foreach (var (stream, group) in new[]
                 {
                     (options.OtpStream, options.OtpConsumerGroup),
                     (options.BulkStream, options.BulkConsumerGroup),
                 })
        {
            double lagSeconds = 0;
            try
            {
                var pending = db.StreamPendingMessages(stream, group, 1, RedisValue.Null, "-", "+");
                lagSeconds = pending.Length == 0 ? 0 : pending.Max(p => p.IdleTimeInMilliseconds) / 1000.0;
            }
            catch (RedisException)
            {
                // Stream/group not created yet, or Redis briefly unreachable — report 0 rather than
                // skipping the series entirely, so the gauge doesn't silently vanish from scrapes.
            }

            yield return new Measurement<double>(lagSeconds, new KeyValuePair<string, object?>("stream", stream));
        }
    }

    private static double ObserveFilesPendingDelete(IServiceScopeFactory scopeFactory)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.StoredFiles.Count(f => f.Status == FileStatus.Expired || f.Status == FileStatus.PendingDelete);
    }
}
