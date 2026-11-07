using System.Net;
using System.Text;

namespace Snail.Sdk.Mcp.Testing;

/// <summary>The guard rules every Snail server's HTTP endpoint keeps, checked by requests to the running server.</summary>
/// <remarks>
/// The endpoint has no key: it listens on loopback, and a request that carries an <c>Origin</c> is refused, since a page
/// in a browser always sends one and an agent's client never does. A server that builds a host of its own loses that
/// refusal without a test failing (Snail.MCP.Documents, 2026-10-16: a request with <c>Origin</c> was answered 200), so
/// every server with HTTP asks it of the process. The refusal is proven only beside an answer: an endpoint that refuses
/// every request refuses a browser's too. A test asserts the answer is empty:
/// <c>Assert.Empty(await HttpGuards.ProblemsAsync(server.Endpoint))</c>.
/// </remarks>
public static class HttpGuards
{
    private const string Page = "https://example.com";

    private const string Sandboxed = "null";

    private const string Ping = """{"jsonrpc":"2.0","id":1,"method":"ping"}""";

    /// <summary>Every rule the endpoint breaks, one line each; empty when all hold.</summary>
    public static async Task<IReadOnlyList<string>> ProblemsAsync(Uri endpoint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        using var http = new HttpClient();
        var fromPage = await StatusAsync(http, endpoint, Page, cancellationToken).ConfigureAwait(false);
        var fromSandbox = await StatusAsync(http, endpoint, Sandboxed, cancellationToken).ConfigureAwait(false);
        var fromClient = await StatusAsync(http, endpoint, null, cancellationToken).ConfigureAwait(false);
        var problems = new List<string>();

        if (fromPage is not HttpStatusCode.Forbidden)
            problems.Add($"POST {endpoint} with Origin: {Page} was answered {(int)fromPage}, not 403: a page in a browser reaches the server");

        if (fromSandbox is not HttpStatusCode.Forbidden)
            problems.Add($"POST {endpoint} with Origin: {Sandboxed} was answered {(int)fromSandbox}, not 403: a sandboxed frame or a local page reaches the server");

        if (fromClient is HttpStatusCode.Forbidden)
            problems.Add($"POST {endpoint} without Origin was answered 403: an agent's client is refused too, so the refusal of browsers proves nothing");

        return problems;
    }

    private static async Task<HttpStatusCode> StatusAsync(HttpClient http, Uri endpoint, string? origin, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new StringContent(Ping, Encoding.UTF8, "application/json") };

        request.Headers.Add("Accept", "application/json, text/event-stream");

        if (origin is not null)
            request.Headers.Add("Origin", origin);

        using var answer = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        return answer.StatusCode;
    }
}
