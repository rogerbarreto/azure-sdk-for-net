// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.ClientModel.Primitives;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClientModel.Tests.Mocks;
using NUnit.Framework;

namespace System.ClientModel.Tests.Options;

public class RequestOptionsScopeTests
{
    [Test]
    public async Task WithRequestOptions_InjectsPolicy_IntoConvenienceMethod()
    {
        var capture = new ObservablePolicy("capture");
        var reqOpts = new RequestOptions();
        reqOpts.AddPolicy(capture, PipelinePosition.PerCall);

        using var cts = new CancellationTokenSource();
        using var fwCt = cts.Token.WithRequestOptions(reqOpts);

        await SimulatedConvenienceMethodAsync("test", fwCt);

        Assert.IsTrue(capture.HasProcessed);
    }

    [Test]
    public async Task WithoutScope_NoCustomPolicy_IsApplied()
    {
        var capture = new ObservablePolicy("capture");

        using var cts = new CancellationTokenSource();
        await SimulatedConvenienceMethodAsync("test", cts.Token);

        Assert.IsFalse(capture.HasProcessed);
    }

    [Test]
    public async Task NestedScopes_InnerOverridesOuter_OuterRestoresOnDispose()
    {
        var outerCapture = new ObservablePolicy("outer");
        var innerCapture = new ObservablePolicy("inner");

        var outerOpts = new RequestOptions();
        outerOpts.AddPolicy(outerCapture, PipelinePosition.PerCall);

        var innerOpts = new RequestOptions();
        innerOpts.AddPolicy(innerCapture, PipelinePosition.PerCall);

        using var cts = new CancellationTokenSource();
        using var outerCt = cts.Token.WithRequestOptions(outerOpts);

        // Outer scope active
        await SimulatedConvenienceMethodAsync("s1", outerCt);
        Assert.IsTrue(outerCapture.HasProcessed, "Outer policy should run in outer scope");

        // Inner scope overrides
        using (var innerCt = cts.Token.WithRequestOptions(innerOpts))
        {
            await SimulatedConvenienceMethodAsync("s2", innerCt);
        }

        Assert.IsTrue(innerCapture.HasProcessed, "Inner policy should run in inner scope");

        // Inner disposed — outer should be active again
        outerCapture.Reset();
        await SimulatedConvenienceMethodAsync("s3", outerCt);
        Assert.IsTrue(outerCapture.HasProcessed, "Outer policy should restore after inner Dispose");
    }

    [Test]
    public async Task DefaultCancellationToken_WorksWithScope()
    {
        var capture = new ObservablePolicy("capture");
        var reqOpts = new RequestOptions();
        reqOpts.AddPolicy(capture, PipelinePosition.PerCall);

        using var fwCt = default(CancellationToken).WithRequestOptions(reqOpts);

        await SimulatedConvenienceMethodAsync("test", fwCt);

        Assert.IsTrue(capture.HasProcessed);
    }

    [Test]
    public async Task MultipleCallsInSameScope_SharePolicy()
    {
        int callCount = 0;
        var counting = new CountingPolicy(() => callCount++);

        var reqOpts = new RequestOptions();
        reqOpts.AddPolicy(counting, PipelinePosition.PerCall);

        using var cts = new CancellationTokenSource();
        using var fwCt = cts.Token.WithRequestOptions(reqOpts);

        await SimulatedConvenienceMethodAsync("s1", fwCt);
        await SimulatedConvenienceMethodAsync("s2", fwCt);
        await SimulatedConvenienceMethodAsync("s3", fwCt);

        Assert.AreEqual(3, callCount);
    }

    [Test]
    public async Task ScopeCleanup_NoLeakAfterDispose()
    {
        var capture = new ObservablePolicy("capture");
        var reqOpts = new RequestOptions();
        reqOpts.AddPolicy(capture, PipelinePosition.PerCall);

        using var cts = new CancellationTokenSource();

        var fwCt = cts.Token.WithRequestOptions(reqOpts);
        fwCt.Dispose();

        capture.Reset();
        await SimulatedConvenienceMethodAsync("test", cts.Token);

        Assert.IsFalse(capture.HasProcessed, "Policy should not apply after scope Dispose");
    }

    // Simulates the exact pattern in Azure.AI.Projects convenience methods:
    //   1. Accept typed params + CancellationToken
    //   2. Internally call ToRequestOptions()
    //   3. Build message, apply options, send through pipeline
    private static async Task SimulatedConvenienceMethodAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        RequestOptions? requestOptions = cancellationToken.ToRequestOptions();

        var pipelineOptions = new ClientPipelineOptions
        {
            Transport = new MockPipelineTransport("test", 200)
        };
        ClientPipeline pipeline = ClientPipeline.Create(pipelineOptions);

        PipelineMessage message = pipeline.CreateMessage();
        message.Request.Method = "POST";
        message.Request.Uri = new Uri($"https://example.com/{name}");

        message.Apply(requestOptions);
        await pipeline.SendAsync(message).ConfigureAwait(false);
    }

    // Test policy that tracks whether it was invoked
    private sealed class ObservablePolicy : PipelinePolicy
    {
        public string Id { get; }
        public bool HasProcessed { get; private set; }

        public ObservablePolicy(string id) => Id = id;
        public void Reset() => HasProcessed = false;

        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            HasProcessed = true;
            ProcessNext(message, pipeline, currentIndex);
        }

        public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            HasProcessed = true;
            return ProcessNextAsync(message, pipeline, currentIndex);
        }
    }

    // Test policy that counts invocations
    private sealed class CountingPolicy : PipelinePolicy
    {
        private readonly Action _onInvoked;
        public CountingPolicy(Action onInvoked) => _onInvoked = onInvoked;

        public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            _onInvoked();
            ProcessNext(message, pipeline, currentIndex);
        }

        public override ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
        {
            _onInvoked();
            return ProcessNextAsync(message, pipeline, currentIndex);
        }
    }
}
