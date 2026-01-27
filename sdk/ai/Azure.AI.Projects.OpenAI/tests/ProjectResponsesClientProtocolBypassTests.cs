// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.ClientModel;
using System.ClientModel.Primitives;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.ClientModel.TestFramework;
using NUnit.Framework;
using OpenAI;
using OpenAI.Responses;

namespace Azure.AI.Projects.OpenAI.Tests;

/// <summary>
/// Tests to verify that protocol method bypass protection works correctly.
/// These tests ensure that when protocol methods are called directly
/// with BinaryContent (as MEAI does), the Azure AI Projects-specific transformations
/// (agent injection, model removal, conversation ID) are still applied.
/// </summary>
public class ProjectResponsesClientProtocolBypassTests : ProjectsOpenAITestBase
{
    public ProjectResponsesClientProtocolBypassTests(bool isAsync) : base(isAsync)
    {
    }

    /// <summary>
    /// Test that calling the protocol method directly with BinaryContent still applies agent defaults.
    /// This simulates what MEAI does when it casts CreateResponseOptions to BinaryContent.
    /// </summary>
    [RecordedTest]
    public async Task ProtocolMethod_WithAgentDefault_AppliesAgentAndRemovesModel()
    {
        // Arrange - Create client with agent default
        ProjectResponsesClient responsesClient = GetTestProjectResponsesClient(
            defaultAgentName: TestEnvironment.AGENT_NAME);

        var options = new CreateResponseOptions
        {
            // Note: Model NOT set explicitly - agent default should be used
            InputItems = { ResponseItem.CreateUserMessageItem("Hello, agent!") }
        };

        // Act - Call protocol method directly (simulating MEAI behavior)
        BinaryContent content = (BinaryContent)options;
        ClientResult result = await responsesClient.CreateResponseAsync(content, new RequestOptions());

        // Assert - Parse the raw response to verify agent was applied
        using JsonDocument doc = JsonDocument.Parse(result.GetRawResponse().Content.ToString());
        JsonElement root = doc.RootElement;

        // Verify agent is present in response
        Assert.That(root.TryGetProperty("agent", out JsonElement agentElement), Is.True,
            "Agent should be present in response (bypass protection working)");
        Assert.That(agentElement.TryGetProperty("name", out JsonElement nameElement), Is.True);
        Assert.That(nameElement.GetString(), Is.EqualTo(TestEnvironment.AGENT_NAME),
            "Agent name should match expected default");
    }

    /// <summary>
    /// Test that calling the protocol method directly with BinaryContent still applies model defaults.
    /// </summary>
    [RecordedTest]
    public async Task ProtocolMethod_WithModelDefault_AppliesModel()
    {
        // Arrange - Create client with model default
        ProjectResponsesClient responsesClient = GetTestProjectResponsesClient(
            defaultModelName: TestEnvironment.MODELDEPLOYMENTNAME);

        var options = new CreateResponseOptions
        {
            // Note: Model NOT set explicitly - model default should be used
            InputItems = { ResponseItem.CreateUserMessageItem("Hello, model!") }
        };

        // Act - Call protocol method directly
        BinaryContent content = (BinaryContent)options;
        ClientResult result = await responsesClient.CreateResponseAsync(content, new RequestOptions());

        // Assert - Parse the raw response to verify model was applied
        using JsonDocument doc = JsonDocument.Parse(result.GetRawResponse().Content.ToString());
        JsonElement root = doc.RootElement;

        // Verify model is present in response
        Assert.That(root.TryGetProperty("model", out JsonElement modelElement), Is.True,
            "Model should be present in response");
        Assert.That(modelElement.GetString(), Does.StartWith(TestEnvironment.MODELDEPLOYMENTNAME),
            "Model should match expected default (bypass protection working)");
    }

    /// <summary>
    /// Test that protocol and convenience methods produce equivalent results for agent mode.
    /// </summary>
    [RecordedTest]
    public async Task ProtocolAndConvenienceMethods_ProduceSameAgentBehavior()
    {
        // Arrange - Create client with agent default
        ProjectOpenAIClient client = GetTestProjectOpenAIClient();
        ProjectResponsesClient responsesClient = client.GetProjectResponsesClientForAgent(TestEnvironment.AGENT_NAME);
        responsesClient = CreateProxyFromClient(responsesClient);

        // Act 1 - Call convenience method
        ResponseResult convenienceResponse = await responsesClient.CreateResponseAsync("Convenience path test");

        // Act 2 - Call protocol method with same input
        var options = new CreateResponseOptions
        {
            InputItems = { ResponseItem.CreateUserMessageItem("Protocol path test") }
        };
        BinaryContent content = (BinaryContent)options;
        ClientResult protocolResult = await responsesClient.CreateResponseAsync(content, new RequestOptions());

        // Parse protocol result
        using JsonDocument doc = JsonDocument.Parse(protocolResult.GetRawResponse().Content.ToString());
        JsonElement root = doc.RootElement;
        string protocolAgentName = root.GetProperty("agent").GetProperty("name").GetString();

        // Assert - Both should have agent set (demonstrating protocol method applies same transformations)
        Assert.That(convenienceResponse.Agent?.Name, Is.EqualTo(TestEnvironment.AGENT_NAME),
            "Convenience method should have agent");
        Assert.That(protocolAgentName, Is.EqualTo(TestEnvironment.AGENT_NAME),
            "Protocol method should have agent (bypass protection working)");
    }

    /// <summary>
    /// Test that calling the protocol method with conversation ID default applies it.
    /// </summary>
    [RecordedTest]
    public async Task ProtocolMethod_WithConversationDefault_AppliesConversationId()
    {
        // Arrange - Create client with conversation default
        ProjectResponsesClient responsesClient = GetTestProjectResponsesClient(
            defaultModelName: TestEnvironment.MODELDEPLOYMENTNAME,
            defaultConversationId: TestEnvironment.KNOWN_CONVERSATION_ID);

        var options = new CreateResponseOptions
        {
            InputItems = { ResponseItem.CreateUserMessageItem("Hello with conversation!") }
        };

        // Act - Call protocol method directly
        BinaryContent content = (BinaryContent)options;
        ClientResult result = await responsesClient.CreateResponseAsync(content, new RequestOptions());

        // Assert - Parse the raw response to verify conversation ID was applied
        using JsonDocument doc = JsonDocument.Parse(result.GetRawResponse().Content.ToString());
        JsonElement root = doc.RootElement;

        // Verify conversation ID is present in response
        Assert.That(root.TryGetProperty("agent_conversation_id", out JsonElement convIdElement), Is.True,
            "Conversation ID should be present in response (bypass protection working)");
        Assert.That(convIdElement.GetString(), Is.EqualTo(TestEnvironment.KNOWN_CONVERSATION_ID),
            "Conversation ID should match expected default");
    }
}
