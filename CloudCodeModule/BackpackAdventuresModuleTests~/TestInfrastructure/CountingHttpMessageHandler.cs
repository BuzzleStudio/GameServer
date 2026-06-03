using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BackpackAdventures.CloudCode.Tests;

/// <summary>
/// Records every HTTP request by the URL key-name fragment it contains.
/// Used to count how many times CloudSaveHelper reads a Cloud Save key over the wire.
/// </summary>
internal sealed class CountingHttpMessageHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, int> _counts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<HttpRequestMessage, HttpResponseMessage>? _responder;

    /// <param name="responder">
    /// Returns the response to send. If null, returns HTTP 200 with an empty results array.
    /// </param>
    public CountingHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage>? responder = null)
    {
        _responder = responder;
    }

    public int TotalRequests => _counts.Values.Sum();

    public int GetCount(string urlFragment) =>
        _counts.TryGetValue(urlFragment, out var c) ? c : 0;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri?.ToString() ?? string.Empty;

        // Track by Cloud Save key name (last path segment before '?' or end).
        // e.g. "/items?keys=mails_all" → "mails_all"
        //      "/items/mails_all"       → "mails_all"
        var fragment = ExtractKeyFragment(uri);
        _counts.AddOrUpdate(fragment, 1, (_, old) => old + 1);
        _counts.AddOrUpdate("__all__", 1, (_, old) => old + 1);

        HttpResponseMessage response;
        if (_responder != null)
        {
            response = _responder(request);
        }
        else
        {
            // Default: Cloud Save empty results array
            response = JsonOk(@"{""results"":[]}");
        }

        return Task.FromResult(response);
    }

    private static string ExtractKeyFragment(string uri)
    {
        // keys=<keyname> query param
        var qi = uri.IndexOf("keys=", StringComparison.OrdinalIgnoreCase);
        if (qi >= 0)
        {
            var start = qi + 5;
            var end = uri.IndexOf('&', start);
            return end < 0 ? uri.Substring(start) : uri.Substring(start, end - start);
        }

        // Last path segment (for DELETE or item-specific GETs)
        var slash = uri.LastIndexOf('/');
        if (slash >= 0 && slash < uri.Length - 1)
        {
            var seg = uri.Substring(slash + 1);
            var q = seg.IndexOf('?');
            return q >= 0 ? seg.Substring(0, q) : seg;
        }

        return uri;
    }

    public void Reset() => _counts.Clear();

    /// <summary>Builds a Cloud Save GET response with a single item.</summary>
    public static HttpResponseMessage JsonOk(string json) =>
        new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    /// <summary>Wraps a value as a Cloud Save results array response.</summary>
    public static HttpResponseMessage CloudSaveResult(string key, string valueJson, string writeLock = "lock-v1") =>
        JsonOk($@"{{""results"":[{{""key"":""{key}"",""value"":{valueJson},""writeLock"":""{writeLock}""}}]}}");

    /// <summary>Returns HTTP 409 to simulate a write-lock conflict.</summary>
    public static HttpResponseMessage Conflict() =>
        new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = new StringContent(@"{""title"":""WriteLock conflict""}", Encoding.UTF8, "application/json")
        };
}
