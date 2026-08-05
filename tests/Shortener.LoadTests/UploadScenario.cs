using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using NBomber.CSharp;

namespace Shortener.LoadTests;

/// <summary>§M8.4d row 1 — 28 req/s for 10 minutes, ~1MB file, p95 &lt; 2s, zero errors.
/// sendSmsImmediately=false so this measures the upload path alone, not also the Outbox/SMS pipeline
/// (that's scenario 3's job). Uses plain HttpClient rather than NBomber.Http's request-building
/// helpers — a smoke test showed those adding multi-second latency (p95 ~21s) that a manual curl
/// upload against the same running Api did not reproduce (0.27s), pointing at NBomber.Http's
/// multipart-body handling rather than the product itself.</summary>
public static class UploadScenario
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void Run(bool smokeTest)
    {
        var fileBytes = new byte[1_000_000];
        Array.Fill(fileBytes, (byte)0x41);
        "%PDF-1.4\n"u8.CopyTo(fileBytes);

        var httpClient = SetupHelpers.CreateApiClient();

        var scenario = Scenario.Create("upload_1mb", async _ =>
            {
                using var content = new MultipartFormDataContent();
                var metadata = new
                {
                    Shop = Guid.NewGuid().ToString("N")[..10],
                    Shod = Guid.NewGuid().ToString("N")[..10],
                    Radif = Guid.NewGuid().ToString("N")[..10],
                    ReportId = 1,
                    ReportName = "Load Test Report",
                    PhoneNumber = "09121234567",
                    SendSmsImmediately = false,
                };
                content.Add(new StringContent(JsonSerializer.Serialize(metadata, JsonOptions), Encoding.UTF8, "application/json"), "metadata");
                var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
                content.Add(fileContent, "file", "loadtest.pdf");

                using var httpResponse = await httpClient.PostAsync("/api/v1/links", content);
                return httpResponse.IsSuccessStatusCode
                    ? Response.Ok(statusCode: ((int)httpResponse.StatusCode).ToString())
                    : Response.Fail(statusCode: ((int)httpResponse.StatusCode).ToString());
            })
            .WithoutWarmUp()
            .WithLoadSimulations(smokeTest
                ? [Simulation.Inject(rate: 5, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromSeconds(20))]
                : [Simulation.Inject(rate: 28, interval: TimeSpan.FromSeconds(1), during: TimeSpan.FromMinutes(10))]);

        NBomberRunner.RegisterScenarios(scenario)
            .WithReportFolder(Path.Combine(AppContext.BaseDirectory, "reports", "upload"))
            .WithReportFormats(NBomber.Contracts.Stats.ReportFormat.Txt, NBomber.Contracts.Stats.ReportFormat.Md)
            .Run();
    }
}
