using System.Net.Http.Headers;
using System.Text;
using WinServerMonitor.Core.Execution;

namespace WinServerMonitor.Infrastructure.Executors;

/// <summary>Calls an HTTP endpoint (health check, web hook, API job trigger).</summary>
public sealed class HttpRequestExecutor(IHttpClientFactory httpClientFactory) : ITaskExecutor
{
    public const string HttpClientName = "tasks";

    private const int MaxLoggedBodyLength = 4000;

    public string TaskType => "Http";

    public string DisplayName => "HTTP požadavek";

    public string Description => "Zavolá HTTP endpoint a ověří návratový kód.";

    public IReadOnlyList<TaskParameterDescriptor> Parameters =>
    [
        new("Url", "URL", Required: true),
        new("Method", "Metoda", TaskParameterKind.Choice, DefaultValue: "GET", Choices: ["GET", "POST", "PUT", "PATCH", "DELETE"]),
        new("Headers", "Hlavičky", TaskParameterKind.MultilineText, Help: "Jedna na řádek: Název: hodnota"),
        new("Body", "Tělo", TaskParameterKind.MultilineText),
        new("ContentType", "Content-Type", DefaultValue: "application/json"),
        new("ExpectedStatusCodes", "Očekávané kódy", Help: "Např. 200,204. Prázdné = libovolný 2xx."),
    ];

    public async Task<TaskExecutionResult> ExecuteAsync(TaskExecutionContext context, CancellationToken cancellationToken)
    {
        var method = new HttpMethod(context.GetParameter("Method") ?? "GET");
        using var request = new HttpRequestMessage(method, context.GetRequiredParameter("Url"));

        if (context.GetParameter("Body") is { } body)
        {
            request.Content = new StringContent(body, Encoding.UTF8);
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.GetParameter("ContentType") ?? "application/json");
        }

        foreach (var line in (context.GetParameter("Headers") ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (!request.Headers.TryAddWithoutValidation(name, value))
            {
                request.Content?.Headers.TryAddWithoutValidation(name, value);
            }
        }

        context.Log.Info($"{method} {request.RequestUri}");
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var response = await client.SendAsync(request, cancellationToken);
        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var status = (int)response.StatusCode;

        context.Log.Info($"Odpověď {status} {response.ReasonPhrase}, {content.Length} znaků");
        if (content.Length > 0)
        {
            context.Log.Debug(content.Length > MaxLoggedBodyLength ? content[..MaxLoggedBodyLength] + " …" : content);
        }

        var expected = context.GetParameter("ExpectedStatusCodes");
        var success = string.IsNullOrWhiteSpace(expected)
            ? response.IsSuccessStatusCode
            : expected.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries).Any(c => c.Trim() == status.ToString());
        var summary = $"HTTP {status}";
        return success ? TaskExecutionResult.Ok(summary) : TaskExecutionResult.Fail(summary);
    }
}
