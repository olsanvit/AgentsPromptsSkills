using Microsoft.EntityFrameworkCore;
using SharedServices;
using SharedServices.Models.Aps;

namespace AgentsPromptsSkills.Web.Services;

/// <summary>
/// General-purpose service for querying APS items (agents, prompts, skills).
/// </summary>
public sealed class ApsItemService
{
    private readonly IDbContextFactory<AppDbContextAps> _dbFactory;

    public ApsItemService(IDbContextFactory<AppDbContextAps> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // AUDIT:OK
    public async Task<List<ApsAgent>> GetPublicAgentsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.ApsAgents.AsNoTracking()
            .Where(a => a.IsPublic)
            .OrderByDescending(a => a.LikeCount)
            .ToListAsync(ct);
    }

    // AUDIT:OK
    public async Task<List<ApsPrompt>> GetPublicPromptsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.ApsPrompts.AsNoTracking()
            .Where(p => p.IsPublic)
            .OrderByDescending(p => p.LikeCount)
            .ToListAsync(ct);
    }

    // AUDIT:OK
    public async Task<List<ApsSkill>> GetPublicSkillsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.ApsSkills.AsNoTracking()
            .Where(s => s.IsPublic)
            .OrderByDescending(s => s.LikeCount)
            .ToListAsync(ct);
    }

    // AUDIT:PENDING|Nízký|entityType parametr string místo enum – chybné hodnoty nespadnou
    public async Task IncrementLikesAsync(string entityType, Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        switch (entityType)
        {
            case "Agent":
                await db.ApsAgents
                    .Where(a => a.Guid == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(a => a.LikeCount, a => a.LikeCount + 1), ct);
                break;
            case "Prompt":
                await db.ApsPrompts
                    .Where(p => p.Guid == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(p => p.LikeCount, p => p.LikeCount + 1), ct);
                break;
            case "Skill":
                await db.ApsSkills
                    .Where(s => s.Guid == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(sk => sk.LikeCount, sk => sk.LikeCount + 1), ct);
                break;
        }
    }
}
