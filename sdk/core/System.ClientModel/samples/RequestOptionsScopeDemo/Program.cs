// ==========================================================================
// RequestOptionsScope Integration Test
//
// Validates that WithRequestOptions() allows injecting pipeline policies
// into convenience methods WITHOUT changing their signatures.
//
// Simulates the Azure.AI.Projects SDK pattern:
//   Convenience method → CancellationToken.ToRequestOptions() → Protocol method → Pipeline
// ==========================================================================

using System.ClientModel;
using System.ClientModel.Primitives;

Console.WriteLine("=== RequestOptionsScope Integration Test ===\n");

int passed = 0;
int failed = 0;

// -------------------------------------------------------------------------
// Test 1: WithRequestOptions injects a policy into a convenience method call
// -------------------------------------------------------------------------
{
    Console.Write("Test 1: Policy injected via WithRequestOptions... ");

    var capture = new HeaderCapturePolicy("X-Custom-Telemetry", "MEAI/1.0");
    var reqOpts = new RequestOptions();
    reqOpts.AddPolicy(capture, PipelinePosition.PerCall);

    using var cts = new CancellationTokenSource();
    using var fwCt = cts.Token.WithRequestOptions(reqOpts);

    await SimulatedSearchMemoriesAsync("test-store", fwCt);

    Assert(capture.WasApplied, "Policy was applied", ref passed, ref failed);
}

// -------------------------------------------------------------------------
// Test 2: Without WithRequestOptions, no custom policy is applied
// -------------------------------------------------------------------------
{
    Console.Write("Test 2: Without scope, no custom policy... ");

    var capture = new HeaderCapturePolicy("X-Custom-Telemetry", "MEAI/1.0");

    using var cts = new CancellationTokenSource();
    await SimulatedSearchMemoriesAsync("test-store", cts.Token);

    Assert(!capture.WasApplied, "Policy was NOT applied", ref passed, ref failed);
}

// -------------------------------------------------------------------------
// Test 3: Nesting scopes — inner scope overrides outer
// -------------------------------------------------------------------------
{
    Console.Write("Test 3: Nested scopes — inner overrides outer... ");

    var outerCapture = new HeaderCapturePolicy("X-Outer", "outer");
    var innerCapture = new HeaderCapturePolicy("X-Inner", "inner");

    var outerOpts = new RequestOptions();
    outerOpts.AddPolicy(outerCapture, PipelinePosition.PerCall);

    var innerOpts = new RequestOptions();
    innerOpts.AddPolicy(innerCapture, PipelinePosition.PerCall);

    using var cts = new CancellationTokenSource();
    using var outerCt = cts.Token.WithRequestOptions(outerOpts);

    // Outer scope active
    await SimulatedSearchMemoriesAsync("s1", outerCt);
    bool outerApplied = outerCapture.WasApplied;

    // Inner scope overrides
    using (var innerCt = cts.Token.WithRequestOptions(innerOpts))
    {
        await SimulatedSearchMemoriesAsync("s2", innerCt);
    }
    bool innerApplied = innerCapture.WasApplied;

    // Inner disposed — outer should be active again
    outerCapture.Reset();
    await SimulatedSearchMemoriesAsync("s3", outerCt);
    bool outerRestored = outerCapture.WasApplied;

    Assert(outerApplied && innerApplied && outerRestored,
        $"outer={outerApplied}, inner={innerApplied}, restored={outerRestored}",
        ref passed, ref failed);
}

// -------------------------------------------------------------------------
// Test 4: Default CancellationToken works with WithRequestOptions
// -------------------------------------------------------------------------
{
    Console.Write("Test 4: Default CancellationToken with scope... ");

    var capture = new HeaderCapturePolicy("X-Framework", "MAF/1.0");
    var reqOpts = new RequestOptions();
    reqOpts.AddPolicy(capture, PipelinePosition.PerCall);

    using var fwCt = default(CancellationToken).WithRequestOptions(reqOpts);

    await SimulatedSearchMemoriesAsync("test-store", fwCt);

    Assert(capture.WasApplied, "Policy was applied", ref passed, ref failed);
}

// -------------------------------------------------------------------------
// Test 5: Multiple calls in same scope share the policy
// -------------------------------------------------------------------------
{
    Console.Write("Test 5: Multiple calls share the policy... ");

    int callCount = 0;
    var counting = new CountingPolicy(() => callCount++);

    var reqOpts = new RequestOptions();
    reqOpts.AddPolicy(counting, PipelinePosition.PerCall);

    using var cts = new CancellationTokenSource();
    using var fwCt = cts.Token.WithRequestOptions(reqOpts);

    await SimulatedSearchMemoriesAsync("s1", fwCt);
    await SimulatedSearchMemoriesAsync("s2", fwCt);
    await SimulatedSearchMemoriesAsync("s3", fwCt);

    Assert(callCount == 3, $"callCount={callCount}, expected 3", ref passed, ref failed);
}

// -------------------------------------------------------------------------
// Test 6: Scope cleanup — no leak after Dispose
// -------------------------------------------------------------------------
{
    Console.Write("Test 6: Scope cleanup — no leak after Dispose... ");

    var capture = new HeaderCapturePolicy("X-Leak-Check", "should-not-leak");
    var reqOpts = new RequestOptions();
    reqOpts.AddPolicy(capture, PipelinePosition.PerCall);

    using var cts = new CancellationTokenSource();

    var fwCt = cts.Token.WithRequestOptions(reqOpts);
    fwCt.Dispose();

    capture.Reset();
    await SimulatedSearchMemoriesAsync("test-store", cts.Token);

    Assert(!capture.WasApplied, "Policy NOT applied after Dispose", ref passed, ref failed);
}

// =========================================================================
Console.WriteLine($"\n=== Results: {passed} passed, {failed} failed ===");
return failed > 0 ? 1 : 0;

// =========================================================================
// Helpers
// =========================================================================
static void Assert(bool condition, string message, ref int passed, ref int failed)
{
    if (condition)
    {
        Console.WriteLine($"PASSED ✅  ({message})");
        passed++;
    }
    else
    {
        Console.WriteLine($"FAILED ❌  ({message})");
        failed++;
    }
}

// =========================================================================
// Simulated Azure.AI.Projects convenience method
// Mirrors AIProjectMemoryStoresOperations.SearchMemoriesAsync exactly:
//   1. Accept typed params + CancellationToken
//   2. Call cancellationToken.ToRequestOptions() internally
//   3. Build PipelineMessage + Apply options
//   4. Send through pipeline
// =========================================================================
static async Task SimulatedSearchMemoriesAsync(
    string memoryStoreName,
    CancellationToken cancellationToken = default)
{
    RequestOptions? requestOptions = cancellationToken.ToRequestOptions();

    var pipelineOptions = new ClientPipelineOptions
    {
        Transport = new MockTransport()
    };
    ClientPipeline pipeline = ClientPipeline.Create(pipelineOptions);

    PipelineMessage message = pipeline.CreateMessage();
    message.Request.Method = "POST";
    message.Request.Uri = new Uri($"https://example.com/memory_stores/{memoryStoreName}:search");
    message.Request.Headers.Set("Content-Type", "application/json");
    message.Request.Headers.Set("Accept", "application/json");

    message.Apply(requestOptions);
    await pipeline.SendAsync(message);
}

// =========================================================================
// Mock transport & types (based on System.ClientModel test infrastructure)
// =========================================================================

sealed class MockTransport : PipelineTransport
{
    protected override PipelineMessage CreateMessageCore() => new MockMessage();

    protected override void ProcessCore(PipelineMessage message)
        => ((MockMessage)message).SetResponse();

    protected override ValueTask ProcessCoreAsync(PipelineMessage message)
    {
        ((MockMessage)message).SetResponse();
        return ValueTask.CompletedTask;
    }

    private sealed class MockMessage : PipelineMessage
    {
        public MockMessage() : base(new MockRequest()) { }
        public void SetResponse() => Response = new MockResponse();
    }
}

sealed class MockRequest : PipelineRequest
{
    private readonly MockRequestHeaders _headers = new();

    protected override string MethodCore { get; set; } = "GET";
    protected override Uri? UriCore { get; set; }
    protected override PipelineRequestHeaders HeadersCore => _headers;
    protected override BinaryContent? ContentCore { get; set; }
    public override void Dispose() { }
}

sealed class MockRequestHeaders : PipelineRequestHeaders
{
    private readonly Dictionary<string, string> _headers = new(StringComparer.OrdinalIgnoreCase);

    public override void Add(string name, string value)
    {
        if (_headers.ContainsKey(name))
            _headers[name] += "," + value;
        else
            _headers[name] = value;
    }

    public override void Set(string name, string value) => _headers[name] = value;
    public override bool Remove(string name) => _headers.Remove(name);
    public override bool TryGetValue(string name, out string? value) => _headers.TryGetValue(name, out value);
    public override bool TryGetValues(string name, out IEnumerable<string>? values)
    {
        if (_headers.TryGetValue(name, out var v)) { values = v.Split(','); return true; }
        values = null; return false;
    }
    public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _headers.GetEnumerator();
}

sealed class MockResponse : PipelineResponse
{
    public override int Status => 200;
    public override string ReasonPhrase => "OK";
    public override Stream? ContentStream { get; set; }
    public override BinaryData Content => BinaryData.FromString("{}");
    protected override PipelineResponseHeaders HeadersCore => new MockResponseHeaders();
    public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;
    public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) => new(Content);
    public override void Dispose() { }
}

sealed class MockResponseHeaders : PipelineResponseHeaders
{
    private readonly Dictionary<string, string> _headers = new();
    public override bool TryGetValue(string name, out string? value) => _headers.TryGetValue(name, out value);
    public override bool TryGetValues(string name, out IEnumerable<string>? values) { values = null; return false; }
    public override IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _headers.GetEnumerator();
}

// =========================================================================
// Test pipeline policies
// =========================================================================

sealed class HeaderCapturePolicy(string headerName, string headerValue) : PipelinePolicy
{
    public bool WasApplied { get; private set; }
    public void Reset() => WasApplied = false;

    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        message.Request.Headers.Set(headerName, headerValue);
        WasApplied = true;
        ProcessNext(message, pipeline, currentIndex);
    }

    public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        message.Request.Headers.Set(headerName, headerValue);
        WasApplied = true;
        return ProcessNextAsync(message, pipeline, currentIndex);
    }
}

sealed class CountingPolicy(Action onInvoked) : PipelinePolicy
{
    public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        onInvoked();
        ProcessNext(message, pipeline, currentIndex);
    }

    public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
    {
        onInvoked();
        return ProcessNextAsync(message, pipeline, currentIndex);
    }
}
