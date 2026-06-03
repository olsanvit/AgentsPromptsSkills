# AgentsPromptsSkills — CLAUDE.md

## Project overview

Webová platforma pro sdílení a správu AI agentů, prompt šablon a reusable skills — browse, create, rate a fork veřejných záznamů.

## Stack

- **.NET 10 Blazor Server** (`@rendermode InteractiveServer`)
- **Entity Framework Core** — PostgreSQL via `Npgsql.EntityFrameworkCore.PostgreSQL`
- **DbContext**: `AppDbContextAps`
- **Connection string key**: `ApsDatabase`
- **SharedServices** — git submodule (`src/SharedServices/`)
- Bootstrap 5, Bootstrap Icons
- `Blazored.Typeahead` — autocomplete vyhledávání
- `Blazored.Modal`, `Blazored.LocalStorage`, `Blazored.SessionStorage`

## Struktura projektu

```
src/
  AgentsPromptsSkills.Web/
    Components/
      Pages/
        Admin/
          Admin.razor                  # /admin (Roles=Admin)
        Agents/
          AgentsPage.razor             # /agents (browse + search)
          AgentDetail.razor            # /agents/{id}
        Prompts/
          PromptsPage.razor            # /prompts (browse + search)
          PromptDetail.razor           # /prompts/{id}
        Skills/
          SkillsPage.razor             # /skills (browse + search)
          SkillDetail.razor            # /skills/{id}
        Dashboard.razor                # / (home — recent public items)
        Profile.razor                  # /profile
  AgentsPromptsSkills.Tests/           # xUnit integration tests
  AgentsPromptsSkills.Mobile/          # MAUI Blazor Hybrid
  SharedServices/                      # git submodule
```

## Key models (`SharedServices.Models.Aps` or `AgentsPromptsSkills.Domain`)

| Model | Popis | Klíčové vlastnosti |
|---|---|---|
| `Agent` | AI agent | `Guid`, `Name`, `Description`, `Tags`, `IsPublic`, `AuthorId` |
| `Prompt` | Prompt šablona | `Guid`, `Name`, `Content`, `Tags`, `IsPublic`, `AuthorId` |
| `Skill` | Reusable skill/tool def | `Guid`, `Name`, `Description`, `Tags`, `IsPublic`, `AuthorId` |

## SharedServices components & services

- `ThemePicker` — přepínač témat (pouze Admin)
- `Paginator` — stránkování
- `ConfirmDialog` — async modal potvrzení
- `ToastService` — notifikace (`ShowSuccess`, `ShowError`)
- `EfCoreService<TContext>` — generický EF helper
- `LoadingService`, `ConfirmService`, `ConnectionStateService`

## Pages

| Route | Komponenta | Popis |
|---|---|---|
| `/` | `Dashboard.razor` | Přehled nedávno přidaných veřejných agentů/promptů/skills |
| `/agents` | `AgentsPage.razor` | Browse + search agentů; filtr IsPublic |
| `/agents/{id}` | `AgentDetail.razor` | Detail agenta, fork |
| `/prompts` | `PromptsPage.razor` | Browse + search prompt šablon |
| `/prompts/{id}` | `PromptDetail.razor` | Detail promptu, kopírování obsahu |
| `/skills` | `SkillsPage.razor` | Browse + search skills |
| `/skills/{id}` | `SkillDetail.razor` | Detail skill definition |
| `/profile` | `Profile.razor` | Správa vlastních záznamů |
| `/admin` | `Admin.razor` | Admin přehled (Roles=Admin) |

## Auth a role

- Veřejné browse stránky: bez autorizace (read-only)
- Vytváření/editace: `@attribute [Authorize]`
- Admin sekce: `@attribute [Authorize(Roles = "Admin")]`

## Konvence

- `@rendermode InteractiveServer` na každé stránce
- Injektovat `IDbContextFactory<AppDbContextAps>` pro DB přístup
- Filter/search: `_search` string + `@bind:event="oninput"` + filtrování v code bloku
- `Blazored.Typeahead` pro tag/autocomplete vyhledávání
- Bootstrap 5 pro layout + Bootstrap Icons (`bi bi-*`)
- Connection string klíč: `ApsDatabase` (nikoli `DefaultConnection`)

## Tests

- `AgentsPromptsSkills.Tests` — xUnit + FluentAssertions + `WebApplicationFactory<Program>`
- Smoke test: GET `/` vrátí success/redirect, GET `/health` vrátí `Healthy`
- Test override: `ConnectionStrings:ApsDatabase = DataSource=:memory:`

## Mobile

- `AgentsPromptsSkills.Mobile` — MAUI Blazor Hybrid
- `AppDbContextAps` přes `IDbContextFactory`, přímé PostgreSQL připojení
- appsettings.json embedded resource — klíč `ApsDatabase`
- Stránky: `/` (Home), `/agents` (AgentsListPage — read-only browse)
