using Anthropic.SDK;
using Anthropic.SDK.Messaging;
using Microsoft.EntityFrameworkCore;
using SharedServices;
using SharedServices.Models.Aps;
using System.Diagnostics;

namespace AgentsPromptsSkills.Web.Services;

/// <summary>
/// Runs a Claude API completion and persists the session to the database.
/// </summary>
public sealed class ApsPlaygroundService
{
    private readonly IDbContextFactory<AppDbContextAps> _dbFactory;
    private readonly AnthropicClient _anthropic;
    private readonly ILogger<ApsPlaygroundService> _logger;

    public ApsPlaygroundService(
        IDbContextFactory<AppDbContextAps> dbFactory,
        AnthropicClient anthropic,
        ILogger<ApsPlaygroundService> logger)
    {
        _dbFactory = dbFactory;
        _anthropic = anthropic;
        _logger    = logger;
    }

    /// <summary>
    /// Calls the Anthropic Claude API and saves the session.
    /// </summary>
    // AUDIT:FIXED|byl: MaxTokens hardcoded; nyní parametr s defaultem 4096
    public async Task<ApsPlaygroundResult> RunAsync(
        string  systemPrompt,
        string  userMessage,
        string  modelName,
        Guid?   agentId,
        string  ownerId,
        CancellationToken ct = default,
        int     maxTokens = 4096)
    {
        var sw = Stopwatch.StartNew();

        var messages = new List<Message>
        {
            new(RoleType.User, userMessage)
        };

        var parameters = new MessageParameters
        {
            Model     = modelName,
            MaxTokens = maxTokens,
            Messages  = messages
        };

        if (!string.IsNullOrWhiteSpace(systemPrompt))
            parameters.SystemMessage = systemPrompt;

        MessageResponse response;
        try
        {
            response = await _anthropic.Messages.GetClaudeMessageAsync(parameters, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Anthropic API call failed");
            throw;
        }

        sw.Stop();

        var responseText = string.Concat(
            response.Content
                .OfType<TextContent>()
                .Select(c => c.Text));

        var inputTokens  = response.Usage?.InputTokens  ?? 0;
        var outputTokens = response.Usage?.OutputTokens ?? 0;
        var durationMs   = (int)sw.ElapsedMilliseconds;

        // Persist session
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var session = new ApsPlaygroundSession
        {
            OwnerId          = ownerId,
            AgentId          = agentId,
            ModelName        = modelName,
            SystemPrompt     = systemPrompt,
            UserMessage      = userMessage,
            AssistantResponse = responseText,
            InputTokens      = inputTokens,
            OutputTokens     = outputTokens,
            DurationMs       = durationMs
        };

        db.ApsPlaygroundSessions.Add(session);
        await db.SaveChangesAsync(ct);

        return new ApsPlaygroundResult(responseText, inputTokens, outputTokens, durationMs);
    }
}

/// <summary>
/// Lightweight result DTO returned from <see cref="ApsPlaygroundService.RunAsync"/>.
/// </summary>
public sealed record ApsPlaygroundResult(
    string ResponseText,
    int    InputTokens,
    int    OutputTokens,
    int    DurationMs);
