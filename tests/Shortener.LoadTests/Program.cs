using Shortener.LoadTests;

var scenarioArg = args.Length > 0 ? args[0] : "help";
var smoke = args.Contains("--smoke");

if (scenarioArg == "help")
{
    Console.WriteLine("""
        Usage: dotnet run -- <upload|download|queue-priority> [--smoke]

        Requires the dev docker-compose stack (docker-compose.dev.yml) and a live Api + PublicWeb +
        Worker all running against it (dotnet run, Development environment). --smoke runs a much
        smaller/faster version of the same scenario to validate the pipeline before committing to the
        full doc-mandated scale/duration.
        """);
    return;
}

Console.WriteLine("[Setup] Provisioning a dedicated load-test client + API key...");
LoadTestConfig.ApiKey = await SetupHelpers.ProvisionClientAndApiKeyAsync();

switch (scenarioArg)
{
    case "upload":
        UploadScenario.Run(smoke);
        break;
    case "download":
        await DownloadScenario.RunAsync(smoke);
        break;
    case "queue-priority":
        await QueuePriorityScenario.RunAsync(smoke);
        break;
    default:
        Console.WriteLine($"Unknown scenario '{scenarioArg}'. Use upload, download, or queue-priority.");
        break;
}
