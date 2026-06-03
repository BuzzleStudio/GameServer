using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BackpackAdventures.CloudCode.Tests;

/// <summary>
/// Programmable HTTP handler for server-side xUnit tests.
///
/// Key-matching strategy:
///   GET  requests → match by URL fragment (key is in the query string: ?keys=mail_meta_state)
///   POST requests → match by JSON body fragment (Cloud Save POST body: {"key":"mail_meta_state",...})
///
/// Responses are ordered: each call to a matching key pops the next queued response.
/// The last-queued response becomes the sticky default once the queue is exhausted.
///
/// Responses are stored as (status, json) and a FRESH HttpResponseMessage is built per send.
/// CloudSaveHelper wraps every response in `using var resp`, disposing it (and its content) after
/// reading — so returning a cached instance twice (sticky default, or a re-read key like mails_all
/// in MarkGlobalRead) would surface a disposed body. Building fresh avoids that entirely.
/// </summary>
internal sealed class ProgrammableHttpMessageHandler : HttpMessageHandler
{
    public record Request(string Method, string Url, string? Body);
    private readonly record struct Resp(HttpStatusCode Status, string Json);

    private readonly List<Request> _recorded = new();
    // Guards _recorded + the queues: MarkAllRead (and other Task.WhenAll paths) issue requests
    // concurrently, so SendAsync can be entered re-entrantly. Without this, List/Queue mutation races.
    private readonly object _gate = new();

    // Separate queues for GET vs POST per key fragment
    private readonly Dictionary<string, Queue<Resp>> _getQueues  = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Queue<Resp>> _postQueues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Resp>        _getDefaults  = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Resp>        _postDefaults = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<Request> Recorded { get { lock (_gate) return _recorded.ToList(); } }

    // ── Counts ──────────────────────────────────────────────────────────────────

    public int PostCount(string keyFragment)
    {
        lock (_gate)
            return _recorded.Count(r => r.Method == "POST" && BodyContainsKey(r.Body, keyFragment));
    }

    public int GetCount(string urlFragment)
    {
        lock (_gate)
            return _recorded.Count(r => r.Method == "GET" && r.Url.Contains(urlFragment, StringComparison.OrdinalIgnoreCase));
    }

    public int TotalRequests { get { lock (_gate) return _recorded.Count; } }

    /// <summary>Last recorded POST whose body targets keyFragment (null if none). For body inspection.</summary>
    public Request? LastPost(string keyFragment)
    {
        lock (_gate)
            return _recorded.LastOrDefault(r => r.Method == "POST" && BodyContainsKey(r.Body, keyFragment));
    }

    // ── Setup API ────────────────────────────────────────────────────────────────

    /// <summary>Enqueue a response for GET requests containing urlFragment.</summary>
    public void AddGet(string urlFragment, HttpResponseMessage response)
        => Enqueue(_getQueues, _getDefaults, urlFragment, response);

    /// <summary>Enqueue a response for POST requests whose body contains keyFragment.</summary>
    public void AddPost(string keyFragment, HttpResponseMessage response)
        => Enqueue(_postQueues, _postDefaults, keyFragment, response);

    /// <summary>Cloud Save GET result: {"results":[{"key":..,"value":..,"writeLock":..}]}</summary>
    public void AddGetCloudSaveResult(string keyName, string valueJson, string writeLock = "lock-v1")
        => AddGet(keyName, JsonResponse(HttpStatusCode.OK,
            $@"{{""results"":[{{""key"":""{keyName}"",""value"":{valueJson},""writeLock"":""{writeLock}""}}]}}"));

    /// <summary>Successful POST (Cloud Save set item).</summary>
    public void AddPostOk(string keyFragment)
        => AddPost(keyFragment, JsonResponse(HttpStatusCode.OK, @"{""results"":[]}"));

    /// <summary>409 conflict POST response (writeLock conflict).</summary>
    public void AddPostConflict(string keyFragment)
        => AddPost(keyFragment, JsonResponse(HttpStatusCode.Conflict,
            @"{""title"":""WriteLock conflict"",""detail"":""WriteLock mismatch""}"));

    // ── Handler ──────────────────────────────────────────────────────────────────

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri  = request.RequestUri?.ToString() ?? "";
        var body = request.Content == null ? null
            : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // No await past this point — safe to hold the gate for record + queue selection.
        lock (_gate)
        {
            _recorded.Add(new Request(request.Method.Method.ToUpperInvariant(), uri, body));

            if (request.Method == HttpMethod.Get)
            {
                foreach (var (key, queue) in _getQueues)
                {
                    if (!uri.Contains(key, StringComparison.OrdinalIgnoreCase)) continue;
                    return Build(Dequeue(queue, _getDefaults[key]));
                }
                return JsonResponse(HttpStatusCode.OK, @"{""results"":[]}"); // fallback GET: empty
            }

            if (request.Method == HttpMethod.Post && body != null)
            {
                foreach (var (key, queue) in _postQueues)
                {
                    if (!BodyContainsKey(body, key)) continue;
                    return Build(Dequeue(queue, _postDefaults[key]));
                }
                return JsonResponse(HttpStatusCode.OK, @"{""results"":[]}"); // fallback POST: success
            }

            return JsonResponse(HttpStatusCode.OK, @"{""results"":[]}"); // DELETE etc.
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────────

    private static void Enqueue(
        Dictionary<string, Queue<Resp>> queues, Dictionary<string, Resp> defaults,
        string fragment, HttpResponseMessage response)
    {
        var k = fragment.ToLowerInvariant();
        var json = response.Content == null ? "" : response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        var resp = new Resp(response.StatusCode, json);
        if (!queues.ContainsKey(k)) queues[k] = new Queue<Resp>();
        queues[k].Enqueue(resp);
        defaults[k] = resp;
    }

    private static bool BodyContainsKey(string? body, string key)
    {
        if (body == null) return false;
        // Cloud Save POST body format: {"key":"mail_meta_state",...}
        // Match both "key":"value" and "key": "value" (with/without space after colon)
        return body.Contains($@"""key"":""{key}""", StringComparison.OrdinalIgnoreCase)
            || body.Contains($@"""key"": ""{key}""", StringComparison.OrdinalIgnoreCase);
    }

    private static Resp Dequeue(Queue<Resp> queue, Resp sticky)
    {
        if (queue.Count > 1) return queue.Dequeue();
        // Last entry stays (sticky) — don't dequeue, just return it.
        return queue.Count == 1 ? queue.Peek() : sticky;
    }

    private static HttpResponseMessage Build(Resp resp) => JsonResponse(resp.Status, resp.Json);

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
