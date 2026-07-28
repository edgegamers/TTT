# RDM Manager Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a victim-driven RDM (Random Death Match) reporting system with a staff case queue (chat commands) and a slay-based punishment queue, persisted to SQLite, alongside the existing auto-karma system.

**Architecture:** A new engine-agnostic module `TTT/RDM` (mirrors `TTT/Karma`) listens to existing game events to log deaths and classify "suspect" kills via a shared `IDamageTracker` extracted from `KarmaListener`. Victims opt-in to file reports (auto-prompt on suspect kills + `!rdm`); staff process an SQLite-backed case queue with `!cases`/`!handle`/`!info`/`!verdict`; guilty verdicts enqueue role-scaled slays paid down over rounds. Non-response defaults to "not RDM" (no case).

**Tech Stack:** C# / .NET 10, CounterStrikeSharp, xUnit v3 on Microsoft.Testing.Platform (MTP), `Microsoft.Data.Sqlite`, YAML→JSON localization.

## Global Constraints

- **Target framework:** `net10.0` for every project (copy from `TTT/Karma/Karma.csproj`).
- **Module registration:** production code registers `ITerrorModule`s via `AddModBehavior` (from `TTT.API.Extensions`). **In `TTT/Test/Startup.cs`, register RDM services with plain `AddScoped`/`AddSingleton` — NOT `AddModBehavior`** — otherwise the module-count assertions in `Test/Abstract/ModuleInitializationTest.cs` (which enumerate `ITerrorModule`/`IPluginModule`) break.
- **Config is optional:** every RDM service loads config via `provider.GetService<IStorage<RdmConfig>>()?.Load().GetAwaiter().GetResult() ?? new RdmConfig()` so tests need no config registration. `IStorage<T>.Load()` returns `Task<T?>`.
- **Player identity:** `IPlayer.Id` (string, the steam id) is the persistence key everywhere. Roles are read via `IRoleAssigner.GetRoles(player).First()`; concrete roles are `InnocentRole`, `TraitorRole`, `DetectiveRole` in `TTT.Game.Roles`.
- **Slaying:** kill a live player by setting `IOnlinePlayer.Health = 0` (triggers suicide in `CS2Player`). Never call CS2 APIs from the `RDM`/`Game` projects.
- **Staff flag:** default `@ttt/admin` (same flag `LogsCommand` uses), exposed as `RdmConfig.StaffFlag`. Gate staff commands with `RequiredFlags`.
- **Localization:** user-facing strings go through `IMsgLocalizer` with keys in `TTT/RDM/lang/RdmMsgs.cs` + `TTT/RDM/lang/en.yml`. Rebuilding any project that references `Locale` regenerates `lang/en.json` (the `PreprocessYaml` target globs `**/lang/*.yml`). Mirror existing token style: `%PREFIX%`, `{yellow}`, `{grey}`, `{red}`, `{0}`, `%s%`.

## Build & Test Commands (use these verbatim)

- **Generate the version file once per fresh worktree** (gitignored, required by `Directory.Build.props`):
  `cp /home/gkh/projects/TTT/GitVersionInformation.g.cs ./GitVersionInformation.g.cs`
  (Already present in this worktree. If a build fails with `CS2001: GitVersionInformation.g.cs could not be found`, re-run the copy.)
- **Build (compiles all referenced code projects + regenerates `lang/en.json`):**
  `dotnet build TTT/Test/Test.csproj -v q --nologo`
- **Run tests (MTP binary — classic `dotnet test` is rejected on the .NET 10 SDK):**
  `./TTT/Test/bin/Debug/net10.0/Test`
  The run prints a `Test run summary` with `total/failed/succeeded` (≈1s). Failures are listed as `failed <FullyQualifiedName>`.
- **Baseline:** `185 passed, 1 failed`. The one failure, `TTT.Test.Abstract.ModuleInitializationTest.Started_ShouldNotBeInit_IfNotStarted`, is a pre-existing order-dependent flake — **not** caused by this work. Regression gate: **no NEW failing tests** (that one may flip between pass/fail depending on ordering; ignore only it).

---

### Task 1: RDM module scaffold + RdmConfig + registration

**Files:**
- Create: `TTT/RDM/RDM.csproj`
- Create: `TTT/RDM/RdmConfig.cs`
- Create: `TTT/RDM/RdmServiceCollection.cs`
- Modify: `TTT.sln` (add project)
- Modify: `TTT/Plugin/Plugin.csproj` (add ProjectReference)
- Modify: `TTT/Plugin/TTTServiceCollection.cs` (call `AddRdmService`)
- Test: `TTT/Test/RDM/RdmConfigTests.cs`
- Modify: `TTT/Test/Test.csproj` (add `..\RDM\RDM.csproj` ProjectReference)

**Interfaces:**
- Produces: `record RdmConfig` with init properties: `string DbString = "Data Source=rdm.db"`, `int TraitorSlays = 5`, `int DetectiveSlays = 5`, `int InnocentSlays = 3`, `bool NotifyAdmins = true`, `bool AutoPromptOnSuspectKill = true`, `int ReportWindowSeconds = 60`, `int MaxReportsPerVictimPerRound = 3`, `string StaffFlag = "@ttt/admin"`; method `int SlaysForRole(string roleName)`.
- Produces: `static class RdmServiceCollection { static void AddRdmService(this IServiceCollection) }`.

- [ ] **Step 1: Create the project file** `TTT/RDM/RDM.csproj`

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>TTT.RDM</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Game\Game.csproj"/>
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Data.Sqlite" Version="9.0.9"/>
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write `TTT/RDM/RdmConfig.cs`**

```csharp
namespace TTT.RDM;

public record RdmConfig {
  public string DbString { get; init; } = "Data Source=rdm.db";

  public int TraitorSlays { get; init; } = 5;
  public int DetectiveSlays { get; init; } = 5;
  public int InnocentSlays { get; init; } = 3;

  public bool NotifyAdmins { get; init; } = true;
  public bool AutoPromptOnSuspectKill { get; init; } = true;
  public int ReportWindowSeconds { get; init; } = 60;
  public int MaxReportsPerVictimPerRound { get; init; } = 3;
  public string StaffFlag { get; init; } = "@ttt/admin";

  /// <summary>
  ///   Number of slays owed when a guilty verdict's victim held the given role.
  ///   roleName is compared against IRole.Name (case-insensitive).
  /// </summary>
  public int SlaysForRole(string roleName) {
    if (roleName.Contains("Traitor", StringComparison.OrdinalIgnoreCase))
      return TraitorSlays;
    if (roleName.Contains("Detective", StringComparison.OrdinalIgnoreCase))
      return DetectiveSlays;
    return InnocentSlays;
  }
}
```

- [ ] **Step 3: Write the (empty) registration** `TTT/RDM/RdmServiceCollection.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;

namespace TTT.RDM;

public static class RdmServiceCollection {
  public static void AddRdmService(this IServiceCollection collection) {
    // Services are registered by later tasks.
  }
}
```

- [ ] **Step 4: Add the project to the solution and references**

Run: `dotnet sln TTT.sln add TTT/RDM/RDM.csproj`

In `TTT/Plugin/Plugin.csproj`, add inside the existing `<ItemGroup>` of `ProjectReference`s (after the Karma line):
```xml
    <ProjectReference Include="..\RDM\RDM.csproj"/>
```

In `TTT/Test/Test.csproj`, add inside the first `<ItemGroup>` of `ProjectReference`s:
```xml
    <ProjectReference Include="..\RDM\RDM.csproj"/>
```

In `TTT/Plugin/TTTServiceCollection.cs`, add `using TTT.RDM;` at the top and add this line right after `serviceCollection.AddKarmaService();`:
```csharp
    serviceCollection.AddRdmService();
```

- [ ] **Step 5: Write the failing test** `TTT/Test/RDM/RdmConfigTests.cs`

```csharp
using TTT.RDM;
using Xunit;

namespace TTT.Test.RDM;

public class RdmConfigTests {
  [Fact]
  public void Defaults_AreSourceModParity() {
    var config = new RdmConfig();
    Assert.Equal(5, config.TraitorSlays);
    Assert.Equal(5, config.DetectiveSlays);
    Assert.Equal(3, config.InnocentSlays);
    Assert.Equal("@ttt/admin", config.StaffFlag);
  }

  [Theory]
  [InlineData(" Traitor", 5)]
  [InlineData(" Detective", 5)]
  [InlineData(" Innocent", 3)]
  public void SlaysForRole_MapsByRoleName(string roleName, int expected) {
    Assert.Equal(expected, new RdmConfig().SlaysForRole(roleName));
  }
}
```

- [ ] **Step 6: Build and run tests**

Run: `dotnet build TTT/Test/Test.csproj -v q --nologo && ./TTT/Test/bin/Debug/net10.0/Test`
Expected: PASS — `RdmConfigTests` (4 cases) pass; no NEW failures vs baseline.

- [ ] **Step 7: Commit**

```bash
git add TTT/RDM TTT.sln TTT/Plugin/Plugin.csproj TTT/Plugin/TTTServiceCollection.cs TTT/Test/Test.csproj TTT/Test/RDM/RdmConfigTests.cs
git commit -m "feat(rdm): scaffold RDM module + RdmConfig"
```

---

### Task 2: Extract shared `IDamageTracker` and refactor `KarmaListener`

Behavior-preserving refactor: move `KarmaListener`'s private `firstDamage` first-damage tracking into a shared `DamageTracker` (in `Game`) consumed by both Karma and (later) RDM.

**Files:**
- Create: `TTT/Game/Damage/KillFault.cs`
- Create: `TTT/Game/Damage/IDamageTracker.cs`
- Create: `TTT/Game/Damage/DamageTracker.cs`
- Modify: `TTT/Game/GameServiceCollection.cs` (register tracker)
- Modify: `TTT/Karma/KarmaListener.cs` (consume tracker; drop private `firstDamage`)
- Modify: `TTT/Test/Startup.cs` (register `IDamageTracker` via plain `AddScoped`)
- Modify: `TTT/Test/Karma/KarmaListenerTests.cs` (register the tracker on the bus)
- Test: `TTT/Test/Game/Damage/DamageTrackerTests.cs`

**Interfaces:**
- Produces: `enum KillFault { Unknown, KillerGuilty, VictimGuilty }` (namespace `TTT.Game.Damage`).
- Produces: `interface IDamageTracker : ITerrorModule { void RecordFirstDamage(string attackerId, string victimId); KillFault GetFault(string killerId, string victimId); void Clear(); }`.
- Consumes (DamageTracker): `PlayerDamagedEvent` (`.Player`, `.Attacker`), `GameStateUpdateEvent` (`.NewState`), `IGameManager.ActiveGame`, `State.IN_PROGRESS`.

- [ ] **Step 1: Write the failing test** `TTT/Test/Game/Damage/DamageTrackerTests.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using TTT.API.Events;
using TTT.API.Game;
using TTT.API.Player;
using TTT.Game.Damage;
using TTT.Game.Events.Player;
using Xunit;

namespace TTT.Test.Game.Damage;

public class DamageTrackerTests {
  private readonly IEventBus bus;
  private readonly IGameManager games;
  private readonly IPlayerFinder players;
  private readonly IDamageTracker tracker;

  public DamageTrackerTests(IServiceProvider provider) {
    bus     = provider.GetRequiredService<IEventBus>();
    games   = provider.GetRequiredService<IGameManager>();
    players = provider.GetRequiredService<IPlayerFinder>();
    tracker = provider.GetRequiredService<IDamageTracker>();
    bus.RegisterListener(tracker);
  }

  [Fact]
  public void GetFault_NoDamage_ReturnsUnknown() {
    Assert.Equal(KillFault.Unknown, tracker.GetFault("a", "b"));
  }

  [Fact]
  public void OnHurt_DuringGame_RecordsKillerGuilty() {
    var victim   = TestPlayer.Random();
    var attacker = TestPlayer.Random();
    players.AddPlayers(victim, attacker);
    games.CreateGame()?.Start();

    bus.Dispatch(new PlayerDamagedEvent(victim, attacker, 100));

    Assert.Equal(KillFault.KillerGuilty,
      tracker.GetFault(attacker.Id, victim.Id));
    Assert.Equal(KillFault.VictimGuilty,
      tracker.GetFault(victim.Id, attacker.Id));
  }

  [Fact]
  public void RoundStart_ClearsPriorDamage() {
    var victim   = TestPlayer.Random();
    var attacker = TestPlayer.Random();
    players.AddPlayers(victim, attacker);
    var game = games.CreateGame();
    game?.Start();
    bus.Dispatch(new PlayerDamagedEvent(victim, attacker, 100));

    // New round start clears first-damage state.
    bus.Dispatch(new TTT.Game.Events.Game.GameStateUpdateEvent(game!,
      State.IN_PROGRESS));

    Assert.Equal(KillFault.Unknown,
      tracker.GetFault(attacker.Id, victim.Id));
  }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build TTT/Test/Test.csproj -v q --nologo`
Expected: FAIL to compile — `KillFault`/`IDamageTracker` do not exist.

- [ ] **Step 3: Create `TTT/Game/Damage/KillFault.cs`**

```csharp
namespace TTT.Game.Damage;

public enum KillFault {
  Unknown,
  KillerGuilty,
  VictimGuilty
}
```

- [ ] **Step 4: Create `TTT/Game/Damage/IDamageTracker.cs`**

```csharp
using TTT.API;

namespace TTT.Game.Damage;

public interface IDamageTracker : ITerrorModule {
  /// <summary>Record that attacker dealt the first damage to victim this round.</summary>
  void RecordFirstDamage(string attackerId, string victimId);

  /// <summary>Who threw the first punch between these two this round.</summary>
  KillFault GetFault(string killerId, string victimId);

  /// <summary>Clear all first-damage state (called at round start).</summary>
  void Clear();
}
```

- [ ] **Step 5: Create `TTT/Game/Damage/DamageTracker.cs`**

```csharp
using JetBrains.Annotations;
using TTT.API.Events;
using TTT.API.Game;
using TTT.Game.Events.Game;
using TTT.Game.Events.Player;
using TTT.Game.Listeners;

namespace TTT.Game.Damage;

public class DamageTracker(IServiceProvider provider)
  : BaseListener(provider), IDamageTracker {
  // Ordered pairs (attackerId, victimId) of who damaged whom first this round.
  private readonly HashSet<(string, string)> firstDamage = [];

  public void RecordFirstDamage(string attackerId, string victimId) {
    // If the victim already hit the attacker first, this is not first damage.
    if (firstDamage.Contains((victimId, attackerId))) return;
    firstDamage.Add((attackerId, victimId));
  }

  public KillFault GetFault(string killerId, string victimId) {
    if (firstDamage.Contains((killerId, victimId)))
      return KillFault.KillerGuilty;
    if (firstDamage.Contains((victimId, killerId)))
      return KillFault.VictimGuilty;
    return KillFault.Unknown;
  }

  public void Clear() { firstDamage.Clear(); }

  [EventHandler]
  [UsedImplicitly]
  public void OnHurt(PlayerDamagedEvent ev) {
    if (Games.ActiveGame is not { State: State.IN_PROGRESS }) return;
    var attacker = ev.Attacker;
    if (attacker == null) return;
    RecordFirstDamage(attacker.Id, ev.Player.Id);
  }

  [EventHandler]
  [UsedImplicitly]
  public void OnRoundStart(GameStateUpdateEvent ev) {
    if (ev.NewState == State.IN_PROGRESS) Clear();
  }
}
```

- [ ] **Step 6: Register the tracker (production)** in `TTT/Game/GameServiceCollection.cs`

Add `using TTT.Game.Damage;` and, in the `// Listeners` block, add:
```csharp
    collection.AddModBehavior<IDamageTracker, DamageTracker>();
```

- [ ] **Step 7: Register the tracker (tests)** in `TTT/Test/Startup.cs`

Add `using TTT.Game.Damage;` and, near the other `AddScoped` lines, add:
```csharp
    services.AddScoped<IDamageTracker, DamageTracker>();
```
(Plain `AddScoped` — do NOT use `AddModBehavior` here; see Global Constraints.)

- [ ] **Step 8: Refactor `KarmaListener` to use the tracker**

In `TTT/Karma/KarmaListener.cs`:

1. Add `using TTT.Game.Damage;`.
2. Add a field (next to the other injected services):
```csharp
  private readonly IDamageTracker damageTracker =
    provider.GetRequiredService<IDamageTracker>();
```
3. Delete the field `private readonly List<(string, string)> firstDamage = new();`.
4. Delete the entire `OnHurt(PlayerDamagedEvent ev)` method (first-damage recording now lives in `DamageTracker`).
5. In `OnRoundStart`, remove the `firstDamage.Clear();` line (keep `badKills.Clear();`).
6. In `OnKill`, replace:
```csharp
    var killerIsGuilty = firstDamage.Contains((killer.Id, victim.Id));
    var victimIsGuilty = firstDamage.Contains((victim.Id, killer.Id));
```
with:
```csharp
    var fault = damageTracker.GetFault(killer.Id, victim.Id);
    var killerIsGuilty = fault == KillFault.KillerGuilty;
    var victimIsGuilty = fault == KillFault.VictimGuilty;
```
(The existing `if (!killerIsGuilty && !victimIsGuilty) { killerIsGuilty = true; ... }` block stays and handles `KillFault.Unknown` identically to before.)

- [ ] **Step 9: Keep `KarmaListenerTests` green by subscribing the tracker**

In `TTT/Test/Karma/KarmaListenerTests.cs` constructor, add `using TTT.Game.Damage;` and, immediately before `var listener = new KarmaListener(provider);`, add:
```csharp
    bus.RegisterListener(provider.GetRequiredService<IDamageTracker>());
```
(The `KarmaListener` resolves the same scoped `IDamageTracker` instance, so the damage events it dispatches are now recorded by the tracker.)

- [ ] **Step 10: Build and run tests**

Run: `dotnet build TTT/Test/Test.csproj -v q --nologo && ./TTT/Test/bin/Debug/net10.0/Test`
Expected: PASS — new `DamageTrackerTests` pass AND all existing `KarmaListenerTests` (the `[Theory]` karma tables) still pass unchanged. No NEW failures.

- [ ] **Step 11: Commit**

```bash
git add TTT/Game/Damage TTT/Game/GameServiceCollection.cs TTT/Karma/KarmaListener.cs TTT/Test/Startup.cs TTT/Test/Karma/KarmaListenerTests.cs TTT/Test/Game/Damage/DamageTrackerTests.cs
git commit -m "refactor(karma): extract shared IDamageTracker (RDM prep)"
```

---

### Task 3: Suspect-kill classifier

A kill is "suspect" (RDM-worthy) per the SourceMod `BadKill` truth table: suspect when the two share a role (teamkill) OR neither is a Traitor; not suspect when the roles differ and exactly one party is a Traitor.

**Files:**
- Create: `TTT/RDM/RdmClassifier.cs`
- Test: `TTT/Test/RDM/RdmClassifierTests.cs`

**Interfaces:**
- Produces: `static class RdmClassifier { static bool IsSuspectKill(IRole killer, IRole victim) }` (namespace `TTT.RDM`).

- [ ] **Step 1: Write the failing test** `TTT/Test/RDM/RdmClassifierTests.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using TTT.API.Role;
using TTT.Game.Roles;
using TTT.RDM;
using Xunit;

namespace TTT.Test.RDM;

public class RdmClassifierTests {
  public enum R { Innocent, Traitor, Detective }

  private readonly IList<IRole> roles;

  public RdmClassifierTests(IServiceProvider provider) {
    roles = new List<IRole> {
      new InnocentRole(provider),
      new TraitorRole(provider),
      new DetectiveRole(provider)
    };
  }

  [Theory]
  // same role -> suspect
  [InlineData(R.Innocent, R.Innocent, true)]
  [InlineData(R.Traitor, R.Traitor, true)]
  [InlineData(R.Detective, R.Detective, true)]
  // different roles, neither traitor -> suspect
  [InlineData(R.Innocent, R.Detective, true)]
  [InlineData(R.Detective, R.Innocent, true)]
  // different roles, one is traitor -> NOT suspect
  [InlineData(R.Traitor, R.Innocent, false)]
  [InlineData(R.Traitor, R.Detective, false)]
  [InlineData(R.Innocent, R.Traitor, false)]
  [InlineData(R.Detective, R.Traitor, false)]
  public void IsSuspectKill_MatchesBadKillTable(R killer, R victim,
    bool expected) {
    Assert.Equal(expected,
      RdmClassifier.IsSuspectKill(roles[(int)killer], roles[(int)victim]));
  }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build TTT/Test/Test.csproj -v q --nologo`
Expected: FAIL to compile — `RdmClassifier` does not exist.

- [ ] **Step 3: Write `TTT/RDM/RdmClassifier.cs`**

```csharp
using TTT.API.Role;
using TTT.Game.Roles;

namespace TTT.RDM;

public static class RdmClassifier {
  /// <summary>
  ///   A kill is "suspect" (worth an RDM report) when the parties share a role
  ///   (teamkill) or neither is a Traitor. A Traitor killing a non-Traitor (or
  ///   vice versa) is legitimate and not suspect.
  /// </summary>
  public static bool IsSuspectKill(IRole killer, IRole victim) {
    var killerIsTraitor = killer is TraitorRole;
    var victimIsTraitor = victim is TraitorRole;

    if (killer.GetType() == victim.GetType()) return true; // same role
    return !killerIsTraitor && !victimIsTraitor;           // neither is traitor
  }
}
```

- [ ] **Step 4: Build and run tests**

Run: `dotnet build TTT/Test/Test.csproj -v q --nologo && ./TTT/Test/bin/Debug/net10.0/Test`
Expected: PASS — all 9 `RdmClassifierTests` cases pass. No NEW failures.

- [ ] **Step 5: Commit**

```bash
git add TTT/RDM/RdmClassifier.cs TTT/Test/RDM/RdmClassifierTests.cs
git commit -m "feat(rdm): suspect-kill classifier"
```

---

### Task 4: Domain models + `IRdmStore` + in-memory store

**Files:**
- Create: `TTT/RDM/Models/CaseState.cs`
- Create: `TTT/RDM/Models/Verdict.cs`
- Create: `TTT/RDM/Models/DeathRecord.cs`
- Create: `TTT/RDM/Models/RdmCase.cs`
- Create: `TTT/RDM/Models/SlayDebt.cs`
- Create: `TTT/RDM/IRdmStore.cs`
- Create: `TTT/RDM/InMemoryRdmStore.cs`
- Test: `TTT/Test/RDM/InMemoryRdmStoreTests.cs`

**Interfaces:**
- Produces: enums `CaseState { Open, Claimed, Resolved }`, `Verdict { None, Forgiven, Guilty }` (namespace `TTT.RDM.Models`).
- Produces: records (namespace `TTT.RDM.Models`) — `DeathRecord`, `RdmCase`, `SlayDebt` (fields below).
- Produces: `interface IRdmStore` (namespace `TTT.RDM`) with the methods used by every later task:
  - `Task<int> AddDeath(DeathRecord death)` — returns assigned id (id on the input is ignored/overwritten).
  - `Task<DeathRecord?> GetDeath(int id)`
  - `Task<IReadOnlyList<DeathRecord>> GetSuspectDeathsForVictim(string victimId, int round)`
  - `Task<int> AddCase(RdmCase rdmCase)` — returns assigned id.
  - `Task<RdmCase?> GetCase(int id)`
  - `Task<IReadOnlyList<RdmCase>> GetOpenCases()` — `State != Resolved`, oldest first.
  - `Task UpdateCase(RdmCase rdmCase)`
  - `Task<bool> HasReport(string reporterId, int deathId)`
  - `Task<int> CountReportsByVictim(string reporterId, int round)`
  - `Task SetSlayDebt(string playerId, int remaining, int sourceCaseId)`
  - `Task<int> GetSlayDebt(string playerId)` — 0 if none.
  - `Task<IReadOnlyList<SlayDebt>> GetAllSlayDebts()` — `RemainingSlays > 0`.

- [ ] **Step 1: Write the model files**

`TTT/RDM/Models/CaseState.cs`:
```csharp
namespace TTT.RDM.Models;

public enum CaseState { Open, Claimed, Resolved }
```

`TTT/RDM/Models/Verdict.cs`:
```csharp
namespace TTT.RDM.Models;

public enum Verdict { None, Forgiven, Guilty }
```

`TTT/RDM/Models/DeathRecord.cs`:
```csharp
using TTT.Game.Damage;

namespace TTT.RDM.Models;

public record DeathRecord {
  public int Id { get; init; }
  public required int Round { get; init; }
  public required string VictimId { get; init; }
  public required string VictimName { get; init; }
  public required string VictimRole { get; init; }
  public required string AttackerId { get; init; }
  public required string AttackerName { get; init; }
  public required string AttackerRole { get; init; }
  public string? Weapon { get; init; }
  public required DateTime Timestamp { get; init; }
  public required bool IsSuspect { get; init; }
  public required KillFault Fault { get; init; }
}
```

`TTT/RDM/Models/RdmCase.cs`:
```csharp
namespace TTT.RDM.Models;

public record RdmCase {
  public int Id { get; init; }
  public required int DeathId { get; init; }
  public required string ReporterId { get; init; }
  public string? Reason { get; init; }
  public CaseState State { get; init; } = CaseState.Open;
  public string? HandlerAdminId { get; init; }
  public Verdict Verdict { get; init; } = Verdict.None;
  public required DateTime CreatedAt { get; init; }
}
```

`TTT/RDM/Models/SlayDebt.cs`:
```csharp
namespace TTT.RDM.Models;

public record SlayDebt {
  public required string PlayerId { get; init; }
  public required int RemainingSlays { get; init; }
  public int SourceCaseId { get; init; }
}
```

- [ ] **Step 2: Write `TTT/RDM/IRdmStore.cs`**

```csharp
using TTT.RDM.Models;

namespace TTT.RDM;

public interface IRdmStore {
  Task<int> AddDeath(DeathRecord death);
  Task<DeathRecord?> GetDeath(int id);
  Task<IReadOnlyList<DeathRecord>> GetSuspectDeathsForVictim(string victimId,
    int round);

  Task<int> AddCase(RdmCase rdmCase);
  Task<RdmCase?> GetCase(int id);
  Task<IReadOnlyList<RdmCase>> GetOpenCases();
  Task UpdateCase(RdmCase rdmCase);
  Task<bool> HasReport(string reporterId, int deathId);
  Task<int> CountReportsByVictim(string reporterId, int round);

  Task SetSlayDebt(string playerId, int remaining, int sourceCaseId);
  Task<int> GetSlayDebt(string playerId);
  Task<IReadOnlyList<SlayDebt>> GetAllSlayDebts();
}
```

- [ ] **Step 3: Write the failing test** `TTT/Test/RDM/InMemoryRdmStoreTests.cs`

```csharp
using TTT.Game.Damage;
using TTT.RDM;
using TTT.RDM.Models;
using Xunit;

namespace TTT.Test.RDM;

public class InMemoryRdmStoreTests {
  private static DeathRecord SampleDeath(string victimId = "v",
    bool suspect = true, int round = 1) {
    return new DeathRecord {
      Round = round, VictimId = victimId, VictimName = "Victim",
      VictimRole = "Innocent", AttackerId = "k", AttackerName = "Killer",
      AttackerRole = "Innocent", Weapon = "ak47",
      Timestamp = new DateTime(2026, 1, 1), IsSuspect = suspect,
      Fault = KillFault.KillerGuilty
    };
  }

  private static IRdmStore NewStore() { return new InMemoryRdmStore(); }

  [Fact]
  public async Task AddDeath_AssignsIncrementingIds() {
    var store = NewStore();
    var id1   = await store.AddDeath(SampleDeath());
    var id2   = await store.AddDeath(SampleDeath());
    Assert.True(id2 > id1);
    Assert.Equal(id1, (await store.GetDeath(id1))!.Id);
  }

  [Fact]
  public async Task GetSuspectDeathsForVictim_FiltersBySuspectAndRound() {
    var store = NewStore();
    await store.AddDeath(SampleDeath("v", true, 1));
    await store.AddDeath(SampleDeath("v", false, 1)); // not suspect
    await store.AddDeath(SampleDeath("v", true, 2));  // wrong round
    await store.AddDeath(SampleDeath("w", true, 1));  // wrong victim

    var result = await store.GetSuspectDeathsForVictim("v", 1);
    Assert.Single(result);
  }

  [Fact]
  public async Task Cases_RoundTripAndOpenFilter() {
    var store  = NewStore();
    var deathId = await store.AddDeath(SampleDeath());
    var id = await store.AddCase(new RdmCase {
      DeathId = deathId, ReporterId = "v", CreatedAt = new DateTime(2026, 1, 1)
    });

    var open = await store.GetOpenCases();
    Assert.Single(open);

    await store.UpdateCase((await store.GetCase(id))! with {
      State = CaseState.Resolved, Verdict = Verdict.Forgiven
    });
    Assert.Empty(await store.GetOpenCases());
  }

  [Fact]
  public async Task HasReport_And_CountReportsByVictim() {
    var store   = NewStore();
    var deathId = await store.AddDeath(SampleDeath());
    Assert.False(await store.HasReport("v", deathId));

    await store.AddCase(new RdmCase {
      DeathId = deathId, ReporterId = "v", CreatedAt = new DateTime(2026, 1, 1)
    });
    Assert.True(await store.HasReport("v", deathId));
    Assert.Equal(1, await store.CountReportsByVictim("v", 1));
  }

  [Fact]
  public async Task SlayDebt_SetGetAndList() {
    var store = NewStore();
    Assert.Equal(0, await store.GetSlayDebt("p"));
    await store.SetSlayDebt("p", 3, 7);
    Assert.Equal(3, await store.GetSlayDebt("p"));
    Assert.Single(await store.GetAllSlayDebts());

    await store.SetSlayDebt("p", 0, 7);
    Assert.Empty(await store.GetAllSlayDebts());
  }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet build TTT/Test/Test.csproj -v q --nologo`
Expected: FAIL to compile — `InMemoryRdmStore` does not exist.

- [ ] **Step 5: Write `TTT/RDM/InMemoryRdmStore.cs`**

```csharp
using System.Collections.Concurrent;
using TTT.RDM.Models;

namespace TTT.RDM;

public sealed class InMemoryRdmStore : IRdmStore {
  private readonly ConcurrentDictionary<int, DeathRecord> deaths = new();
  private readonly ConcurrentDictionary<int, RdmCase> cases = new();
  private readonly ConcurrentDictionary<string, SlayDebt> slays = new();
  private int nextDeathId;
  private int nextCaseId;

  public Task<int> AddDeath(DeathRecord death) {
    var id = Interlocked.Increment(ref nextDeathId);
    deaths[id] = death with { Id = id };
    return Task.FromResult(id);
  }

  public Task<DeathRecord?> GetDeath(int id) {
    return Task.FromResult(deaths.GetValueOrDefault(id));
  }

  public Task<IReadOnlyList<DeathRecord>> GetSuspectDeathsForVictim(
    string victimId, int round) {
    IReadOnlyList<DeathRecord> result = deaths.Values
     .Where(d => d.IsSuspect && d.VictimId == victimId && d.Round == round)
     .OrderBy(d => d.Id)
     .ToList();
    return Task.FromResult(result);
  }

  public Task<int> AddCase(RdmCase rdmCase) {
    var id = Interlocked.Increment(ref nextCaseId);
    cases[id] = rdmCase with { Id = id };
    return Task.FromResult(id);
  }

  public Task<RdmCase?> GetCase(int id) {
    return Task.FromResult(cases.GetValueOrDefault(id));
  }

  public Task<IReadOnlyList<RdmCase>> GetOpenCases() {
    IReadOnlyList<RdmCase> result = cases.Values
     .Where(c => c.State != CaseState.Resolved)
     .OrderBy(c => c.Id)
     .ToList();
    return Task.FromResult(result);
  }

  public Task UpdateCase(RdmCase rdmCase) {
    cases[rdmCase.Id] = rdmCase;
    return Task.CompletedTask;
  }

  public Task<bool> HasReport(string reporterId, int deathId) {
    return Task.FromResult(cases.Values.Any(c
      => c.ReporterId == reporterId && c.DeathId == deathId));
  }

  public Task<int> CountReportsByVictim(string reporterId, int round) {
    var count = cases.Values.Count(c
      => c.ReporterId == reporterId
      && deaths.TryGetValue(c.DeathId, out var d) && d.Round == round);
    return Task.FromResult(count);
  }

  public Task SetSlayDebt(string playerId, int remaining, int sourceCaseId) {
    if (remaining <= 0)
      slays.TryRemove(playerId, out _);
    else
      slays[playerId] = new SlayDebt {
        PlayerId = playerId, RemainingSlays = remaining,
        SourceCaseId = sourceCaseId
      };
    return Task.CompletedTask;
  }

  public Task<int> GetSlayDebt(string playerId) {
    return Task.FromResult(
      slays.TryGetValue(playerId, out var d) ? d.RemainingSlays : 0);
  }

  public Task<IReadOnlyList<SlayDebt>> GetAllSlayDebts() {
    IReadOnlyList<SlayDebt> result =
      slays.Values.Where(s => s.RemainingSlays > 0).ToList();
    return Task.FromResult(result);
  }
}
```

- [ ] **Step 6: Build and run tests**

Run: `dotnet build TTT/Test/Test.csproj -v q --nologo && ./TTT/Test/bin/Debug/net10.0/Test`
Expected: PASS — all `InMemoryRdmStoreTests` pass. No NEW failures.

- [ ] **Step 7: Commit**

```bash
git add TTT/RDM/Models TTT/RDM/IRdmStore.cs TTT/RDM/InMemoryRdmStore.cs TTT/Test/RDM/InMemoryRdmStoreTests.cs
git commit -m "feat(rdm): domain models + IRdmStore + in-memory store"
```

---

### Task 5: SQLite store (`SqliteRdmStore`)

Production `IRdmStore` backed by `Microsoft.Data.Sqlite`. Tests run against `Data Source=:memory:` held open by a persistent connection.

**Files:**
- Create: `TTT/RDM/SqliteRdmStore.cs`
- Test: `TTT/Test/RDM/SqliteRdmStoreTests.cs`

**Interfaces:**
- Produces: `sealed class SqliteRdmStore : IRdmStore, IDisposable` with ctor `SqliteRdmStore(string connectionString)`. Opens one shared `SqliteConnection` (kept open so `:memory:` persists), creates tables on construction.

- [ ] **Step 1: Write the failing test** `TTT/Test/RDM/SqliteRdmStoreTests.cs`

```csharp
using TTT.Game.Damage;
using TTT.RDM;
using TTT.RDM.Models;
using Xunit;

namespace TTT.Test.RDM;

public class SqliteRdmStoreTests {
  private static DeathRecord SampleDeath(string victimId = "v",
    bool suspect = true, int round = 1) {
    return new DeathRecord {
      Round = round, VictimId = victimId, VictimName = "Victim",
      VictimRole = "Innocent", AttackerId = "k", AttackerName = "Killer",
      AttackerRole = "Innocent", Weapon = "ak47",
      Timestamp = new DateTime(2026, 1, 1), IsSuspect = suspect,
      Fault = KillFault.KillerGuilty
    };
  }

  // Unique in-memory DB per test (shared cache so the schema persists across
  // the store's internal connection lifetime).
  private static SqliteRdmStore NewStore() {
    return new SqliteRdmStore("Data Source=:memory:");
  }

  [Fact]
  public async Task Death_RoundTrip_PreservesFields() {
    using var store = NewStore();
    var id = await store.AddDeath(SampleDeath());
    var got = await store.GetDeath(id);
    Assert.NotNull(got);
    Assert.Equal("Killer", got!.AttackerName);
    Assert.Equal(KillFault.KillerGuilty, got.Fault);
    Assert.True(got.IsSuspect);
  }

  [Fact]
  public async Task SuspectDeaths_FilterByVictimRoundSuspect() {
    using var store = NewStore();
    await store.AddDeath(SampleDeath("v", true, 1));
    await store.AddDeath(SampleDeath("v", false, 1));
    await store.AddDeath(SampleDeath("v", true, 2));
    Assert.Single(await store.GetSuspectDeathsForVictim("v", 1));
  }

  [Fact]
  public async Task Cases_OpenFilter_And_Update() {
    using var store = NewStore();
    var deathId = await store.AddDeath(SampleDeath());
    var id = await store.AddCase(new RdmCase {
      DeathId = deathId, ReporterId = "v", CreatedAt = new DateTime(2026, 1, 1)
    });
    Assert.Single(await store.GetOpenCases());
    await store.UpdateCase((await store.GetCase(id))! with {
      State = CaseState.Resolved, Verdict = Verdict.Guilty,
      HandlerAdminId = "admin"
    });
    Assert.Empty(await store.GetOpenCases());
    Assert.Equal(Verdict.Guilty, (await store.GetCase(id))!.Verdict);
  }

  [Fact]
  public async Task SlayDebt_Persisted_AcrossNewStoreSameFile() {
    var file = $"Data Source=rdm-test-{Guid.NewGuid():N}.db";
    try {
      using (var store = new SqliteRdmStore(file))
        await store.SetSlayDebt("p", 4, 1);
      using (var store2 = new SqliteRdmStore(file))
        Assert.Equal(4, await store2.GetSlayDebt("p"));
    } finally {
      Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
      var path = file.Replace("Data Source=", "");
      if (File.Exists(path)) File.Delete(path);
    }
  }

  [Fact]
  public async Task HasReport_And_CountReportsByVictim() {
    using var store = NewStore();
    var deathId = await store.AddDeath(SampleDeath());
    Assert.False(await store.HasReport("v", deathId));
    await store.AddCase(new RdmCase {
      DeathId = deathId, ReporterId = "v", CreatedAt = new DateTime(2026, 1, 1)
    });
    Assert.True(await store.HasReport("v", deathId));
    Assert.Equal(1, await store.CountReportsByVictim("v", 1));
  }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build TTT/Test/Test.csproj -v q --nologo`
Expected: FAIL to compile — `SqliteRdmStore` does not exist.

- [ ] **Step 3: Write `TTT/RDM/SqliteRdmStore.cs`**

```csharp
using Microsoft.Data.Sqlite;
using TTT.Game.Damage;
using TTT.RDM.Models;

namespace TTT.RDM;

public sealed class SqliteRdmStore : IRdmStore, IDisposable {
  private readonly SqliteConnection connection;

  public SqliteRdmStore(string connectionString) {
    connection = new SqliteConnection(connectionString);
    connection.Open();
    CreateTables();
  }

  private void CreateTables() {
    using var cmd = connection.CreateCommand();
    cmd.CommandText =
      """
      CREATE TABLE IF NOT EXISTS deaths (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        round INTEGER NOT NULL,
        victim_id TEXT NOT NULL, victim_name TEXT NOT NULL, victim_role TEXT NOT NULL,
        attacker_id TEXT NOT NULL, attacker_name TEXT NOT NULL, attacker_role TEXT NOT NULL,
        weapon TEXT, timestamp TEXT NOT NULL, is_suspect INTEGER NOT NULL, fault INTEGER NOT NULL
      );
      CREATE TABLE IF NOT EXISTS cases (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        death_id INTEGER NOT NULL, reporter_id TEXT NOT NULL, reason TEXT,
        state INTEGER NOT NULL, handler_admin_id TEXT, verdict INTEGER NOT NULL,
        created_at TEXT NOT NULL
      );
      CREATE TABLE IF NOT EXISTS slays (
        player_id TEXT PRIMARY KEY, remaining INTEGER NOT NULL, source_case_id INTEGER NOT NULL
      );
      """;
    cmd.ExecuteNonQuery();
  }

  private static DeathRecord ReadDeath(SqliteDataReader r) {
    return new DeathRecord {
      Id = r.GetInt32(0), Round = r.GetInt32(1),
      VictimId = r.GetString(2), VictimName = r.GetString(3),
      VictimRole = r.GetString(4), AttackerId = r.GetString(5),
      AttackerName = r.GetString(6), AttackerRole = r.GetString(7),
      Weapon = r.IsDBNull(8) ? null : r.GetString(8),
      Timestamp = DateTime.Parse(r.GetString(9)),
      IsSuspect = r.GetInt32(10) != 0, Fault = (KillFault)r.GetInt32(11)
    };
  }

  private const string DeathCols =
    "id, round, victim_id, victim_name, victim_role, attacker_id, attacker_name, attacker_role, weapon, timestamp, is_suspect, fault";

  public async Task<int> AddDeath(DeathRecord d) {
    await using var cmd = connection.CreateCommand();
    cmd.CommandText =
      """
      INSERT INTO deaths (round, victim_id, victim_name, victim_role, attacker_id, attacker_name, attacker_role, weapon, timestamp, is_suspect, fault)
      VALUES ($round, $vid, $vname, $vrole, $aid, $aname, $arole, $weapon, $ts, $suspect, $fault);
      SELECT last_insert_rowid();
      """;
    cmd.Parameters.AddWithValue("$round", d.Round);
    cmd.Parameters.AddWithValue("$vid", d.VictimId);
    cmd.Parameters.AddWithValue("$vname", d.VictimName);
    cmd.Parameters.AddWithValue("$vrole", d.VictimRole);
    cmd.Parameters.AddWithValue("$aid", d.AttackerId);
    cmd.Parameters.AddWithValue("$aname", d.AttackerName);
    cmd.Parameters.AddWithValue("$arole", d.AttackerRole);
    cmd.Parameters.AddWithValue("$weapon", (object?)d.Weapon ?? DBNull.Value);
    cmd.Parameters.AddWithValue("$ts", d.Timestamp.ToString("o"));
    cmd.Parameters.AddWithValue("$suspect", d.IsSuspect ? 1 : 0);
    cmd.Parameters.AddWithValue("$fault", (int)d.Fault);
    return Convert.ToInt32(await cmd.ExecuteScalarAsync());
  }

  public async Task<DeathRecord?> GetDeath(int id) {
    await using var cmd = connection.CreateCommand();
    cmd.CommandText = $"SELECT {DeathCols} FROM deaths WHERE id = $id";
    cmd.Parameters.AddWithValue("$id", id);
    await using var r = await cmd.ExecuteReaderAsync();
    return await r.ReadAsync() ? ReadDeath(r) : null;
  }

  public async Task<IReadOnlyList<DeathRecord>> GetSuspectDeathsForVictim(
    string victimId, int round) {
    await using var cmd = connection.CreateCommand();
    cmd.CommandText =
      $"SELECT {DeathCols} FROM deaths WHERE victim_id = $v AND round = $r AND is_suspect = 1 ORDER BY id";
    cmd.Parameters.AddWithValue("$v", victimId);
    cmd.Parameters.AddWithValue("$r", round);
    var list = new List<DeathRecord>();
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync()) list.Add(ReadDeath(r));
    return list;
  }

  public async Task<int> AddCase(RdmCase c) {
    await using var cmd = connection.CreateCommand();
    cmd.CommandText =
      """
      INSERT INTO cases (death_id, reporter_id, reason, state, handler_admin_id, verdict, created_at)
      VALUES ($did, $rid, $reason, $state, $handler, $verdict, $created);
      SELECT last_insert_rowid();
      """;
    cmd.Parameters.AddWithValue("$did", c.DeathId);
    cmd.Parameters.AddWithValue("$rid", c.ReporterId);
    cmd.Parameters.AddWithValue("$reason", (object?)c.Reason ?? DBNull.Value);
    cmd.Parameters.AddWithValue("$state", (int)c.State);
    cmd.Parameters.AddWithValue("$handler",
      (object?)c.HandlerAdminId ?? DBNull.Value);
    cmd.Parameters.AddWithValue("$verdict", (int)c.Verdict);
    cmd.Parameters.AddWithValue("$created", c.CreatedAt.ToString("o"));
    return Convert.ToInt32(await cmd.ExecuteScalarAsync());
  }

  private static RdmCase ReadCase(SqliteDataReader r) {
    return new RdmCase {
      Id = r.GetInt32(0), DeathId = r.GetInt32(1), ReporterId = r.GetString(2),
      Reason = r.IsDBNull(3) ? null : r.GetString(3),
      State = (CaseState)r.GetInt32(4),
      HandlerAdminId = r.IsDBNull(5) ? null : r.GetString(5),
      Verdict = (Verdict)r.GetInt32(6),
      CreatedAt = DateTime.Parse(r.GetString(7))
    };
  }

  private const string CaseCols =
    "id, death_id, reporter_id, reason, state, handler_admin_id, verdict, created_at";

  public async Task<RdmCase?> GetCase(int id) {
    await using var cmd = connection.CreateCommand();
    cmd.CommandText = $"SELECT {CaseCols} FROM cases WHERE id = $id";
    cmd.Parameters.AddWithValue("$id", id);
    await using var r = await cmd.ExecuteReaderAsync();
    return await r.ReadAsync() ? ReadCase(r) : null;
  }

  public async Task<IReadOnlyList<RdmCase>> GetOpenCases() {
    await using var cmd = connection.CreateCommand();
    cmd.CommandText =
      $"SELECT {CaseCols} FROM cases WHERE state != $resolved ORDER BY id";
    cmd.Parameters.AddWithValue("$resolved", (int)CaseState.Resolved);
    var list = new List<RdmCase>();
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync()) list.Add(ReadCase(r));
    return list;
  }

  public async Task UpdateCase(RdmCase c) {
    await using var cmd = connection.CreateCommand();
    cmd.CommandText =
      """
      UPDATE cases SET death_id=$did, reporter_id=$rid, reason=$reason,
        state=$state, handler_admin_id=$handler, verdict=$verdict, created_at=$created
      WHERE id=$id
      """;
    cmd.Parameters.AddWithValue("$id", c.Id);
    cmd.Parameters.AddWithValue("$did", c.DeathId);
    cmd.Parameters.AddWithValue("$rid", c.ReporterId);
    cmd.Parameters.AddWithValue("$reason", (object?)c.Reason ?? DBNull.Value);
    cmd.Parameters.AddWithValue("$state", (int)c.State);
    cmd.Parameters.AddWithValue("$handler",
      (object?)c.HandlerAdminId ?? DBNull.Value);
    cmd.Parameters.AddWithValue("$verdict", (int)c.Verdict);
    cmd.Parameters.AddWithValue("$created", c.CreatedAt.ToString("o"));
    await cmd.ExecuteNonQueryAsync();
  }

  public async Task<bool> HasReport(string reporterId, int deathId) {
    await using var cmd = connection.CreateCommand();
    cmd.CommandText =
      "SELECT COUNT(*) FROM cases WHERE reporter_id = $r AND death_id = $d";
    cmd.Parameters.AddWithValue("$r", reporterId);
    cmd.Parameters.AddWithValue("$d", deathId);
    return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
  }

  public async Task<int> CountReportsByVictim(string reporterId, int round) {
    await using var cmd = connection.CreateCommand();
    cmd.CommandText =
      """
      SELECT COUNT(*) FROM cases c JOIN deaths d ON c.death_id = d.id
      WHERE c.reporter_id = $r AND d.round = $round
      """;
    cmd.Parameters.AddWithValue("$r", reporterId);
    cmd.Parameters.AddWithValue("$round", round);
    return Convert.ToInt32(await cmd.ExecuteScalarAsync());
  }

  public async Task SetSlayDebt(string playerId, int remaining,
    int sourceCaseId) {
    await using var cmd = connection.CreateCommand();
    if (remaining <= 0) {
      cmd.CommandText = "DELETE FROM slays WHERE player_id = $p";
      cmd.Parameters.AddWithValue("$p", playerId);
    } else {
      cmd.CommandText =
        """
        INSERT INTO slays (player_id, remaining, source_case_id)
        VALUES ($p, $rem, $src)
        ON CONFLICT(player_id) DO UPDATE SET remaining=$rem, source_case_id=$src
        """;
      cmd.Parameters.AddWithValue("$p", playerId);
      cmd.Parameters.AddWithValue("$rem", remaining);
      cmd.Parameters.AddWithValue("$src", sourceCaseId);
    }
    await cmd.ExecuteNonQueryAsync();
  }

  public async Task<int> GetSlayDebt(string playerId) {
    await using var cmd = connection.CreateCommand();
    cmd.CommandText = "SELECT remaining FROM slays WHERE player_id = $p";
    cmd.Parameters.AddWithValue("$p", playerId);
    var result = await cmd.ExecuteScalarAsync();
    return result == null ? 0 : Convert.ToInt32(result);
  }

  public async Task<IReadOnlyList<SlayDebt>> GetAllSlayDebts() {
    await using var cmd = connection.CreateCommand();
    cmd.CommandText =
      "SELECT player_id, remaining, source_case_id FROM slays WHERE remaining > 0";
    var list = new List<SlayDebt>();
    await using var r = await cmd.ExecuteReaderAsync();
    while (await r.ReadAsync())
      list.Add(new SlayDebt {
        PlayerId = r.GetString(0), RemainingSlays = r.GetInt32(1),
        SourceCaseId = r.GetInt32(2)
      });
    return list;
  }

  public void Dispose() { connection.Dispose(); }
}
```

- [ ] **Step 4: Build and run tests**

Run: `dotnet build TTT/Test/Test.csproj -v q --nologo && ./TTT/Test/bin/Debug/net10.0/Test`
Expected: PASS — all `SqliteRdmStoreTests` pass. No NEW failures.

- [ ] **Step 5: Commit**

```bash
git add TTT/RDM/SqliteRdmStore.cs TTT/Test/RDM/SqliteRdmStoreTests.cs
git commit -m "feat(rdm): SQLite-backed IRdmStore"
```

---

### Task 6: DeathLogListener — record deaths, round counter, suspect detect, auto-prompt

**Files:**
- Create: `TTT/RDM/lang/RdmMsgs.cs`
- Create: `TTT/RDM/lang/en.yml`
- Create: `TTT/RDM/DeathLogListener.cs`
- Test: `TTT/Test/RDM/DeathLogListenerTests.cs`

**Interfaces:**
- Consumes: `IRdmStore`, `IDamageTracker`, `RdmClassifier.IsSuspectKill`, `PlayerDeathEvent` (`.Victim`, `.Killer`, `.Weapon`), `GameStateUpdateEvent` (`.NewState == State.IN_PROGRESS`), `IRoleAssigner.GetRoles(...).First()`, `RdmConfig.AutoPromptOnSuspectKill`.
- Produces: `class DeathLogListener : BaseListener` exposing `int CurrentRound { get; }` (1-based; increments each `State.IN_PROGRESS`).

- [ ] **Step 1: Create localization** `TTT/RDM/lang/RdmMsgs.cs`

```csharp
using TTT.Locale;

namespace TTT.RDM.lang;

public static class RdmMsgs {
  public static IMsg RDM_PROMPT(string attacker) {
    return MsgFactory.Create(nameof(RDM_PROMPT), attacker);
  }
}
```

`TTT/RDM/lang/en.yml`:
```yaml
RDM_PROMPT: "%PREFIX%You were killed by {yellow}{0}{grey}. Type {yellow}!rdm{grey} to report it, or ignore (defaults to not RDM)."
```

- [ ] **Step 2: Write the failing test** `TTT/Test/RDM/DeathLogListenerTests.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using TTT.API.Events;
using TTT.API.Game;
using TTT.API.Player;
using TTT.API.Role;
using TTT.Game.Events.Player;
using TTT.Game.Roles;
using TTT.Locale;
using TTT.RDM;
using TTT.RDM.lang;
using Xunit;

namespace TTT.Test.RDM;

public class DeathLogListenerTests {
  private readonly IEventBus bus;
  private readonly IGameManager games;
  private readonly IPlayerFinder players;
  private readonly IRoleAssigner roles;
  private readonly IRdmStore store;
  private readonly IMsgLocalizer locale;
  private readonly IList<IRole> roleSet;

  public DeathLogListenerTests(IServiceProvider provider) {
    bus     = provider.GetRequiredService<IEventBus>();
    games   = provider.GetRequiredService<IGameManager>();
    players = provider.GetRequiredService<IPlayerFinder>();
    roles   = provider.GetRequiredService<IRoleAssigner>();
    store   = provider.GetRequiredService<IRdmStore>();
    locale  = provider.GetRequiredService<IMsgLocalizer>();
    roleSet = new List<IRole> {
      new InnocentRole(provider), new TraitorRole(provider),
      new DetectiveRole(provider)
    };
    bus.RegisterListener(provider.GetRequiredService<IDamageTracker>());
    bus.RegisterListener(new DeathLogListener(provider));
  }

  private (TestPlayer victim, TestPlayer killer) StartRoundWith(
    IRole victimRole, IRole killerRole) {
    var victim = TestPlayer.Random();
    var killer = TestPlayer.Random();
    players.AddPlayers(victim, killer);
    var game = games.CreateGame();
    game?.Start();
    // Drive a round-start so DeathLogListener.CurrentRound becomes 1 (the round
    // the deaths are stored under and that the tests query).
    bus.Dispatch(new TTT.Game.Events.Game.GameStateUpdateEvent(game!,
      State.IN_PROGRESS));
    roles.SetRole(victim, victimRole);
    roles.SetRole(killer, killerRole);
    return (victim, killer);
  }

  [Fact]
  public async Task SuspectKill_RecordsDeath_AndPromptsVictim() {
    var (victim, killer) =
      StartRoundWith(roleSet[0], roleSet[0]); // inno on inno
    bus.Dispatch(new PlayerDamagedEvent(victim, killer, 100));
    var death = new PlayerDeathEvent(victim).WithKiller(killer)
     .WithWeapon("ak47");
    bus.Dispatch(death);

    var recorded = await store.GetSuspectDeathsForVictim(victim.Id, 1);
    Assert.Single(recorded);
    Assert.Contains(locale[RdmMsgs.RDM_PROMPT(killer.Name)], victim.Messages);
  }

  [Fact]
  public async Task LegitKill_RecordsNonSuspect_NoPrompt() {
    var (victim, killer) =
      StartRoundWith(roleSet[0], roleSet[1]); // traitor kills inno
    bus.Dispatch(new PlayerDamagedEvent(victim, killer, 100));
    bus.Dispatch(new PlayerDeathEvent(victim).WithKiller(killer));

    Assert.Empty(await store.GetSuspectDeathsForVictim(victim.Id, 1));
    Assert.Empty(victim.Messages);
  }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet build TTT/Test/Test.csproj -v q --nologo`
Expected: FAIL to compile — `DeathLogListener` does not exist.

- [ ] **Step 4: Write `TTT/RDM/DeathLogListener.cs`**

```csharp
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using TTT.API.Events;
using TTT.API.Game;
using TTT.API.Storage;
using TTT.Game.Damage;
using TTT.Game.Events.Game;
using TTT.Game.Events.Player;
using TTT.Game.Listeners;
using TTT.RDM.lang;

namespace TTT.RDM;

public class DeathLogListener(IServiceProvider provider)
  : BaseListener(provider) {
  private readonly IRdmStore store = provider.GetRequiredService<IRdmStore>();

  private readonly IDamageTracker damage =
    provider.GetRequiredService<IDamageTracker>();

  public int CurrentRound { get; private set; }

  private RdmConfig config
    => Provider.GetService<IStorage<RdmConfig>>()?.Load().GetAwaiter()
      .GetResult() ?? new RdmConfig();

  [EventHandler]
  [UsedImplicitly]
  public void OnRoundStart(GameStateUpdateEvent ev) {
    if (ev.NewState == State.IN_PROGRESS) CurrentRound++;
  }

  [EventHandler]
  [UsedImplicitly]
  public void OnKill(PlayerDeathEvent ev) {
    if (Games.ActiveGame is not { State: State.IN_PROGRESS }) return;
    var victim = ev.Victim;
    var killer = ev.Killer;
    if (killer == null || victim.Id == killer.Id) return;

    var victimRole = Roles.GetRoles(victim).First();
    var killerRole = Roles.GetRoles(killer).First();
    var suspect    = RdmClassifier.IsSuspectKill(killerRole, victimRole);
    var fault      = damage.GetFault(killer.Id, victim.Id);

    var record = new Models.DeathRecord {
      Round = CurrentRound,
      VictimId = victim.Id, VictimName = victim.Name,
      VictimRole = victimRole.Name,
      AttackerId = killer.Id, AttackerName = killer.Name,
      AttackerRole = killerRole.Name, Weapon = ev.Weapon,
      Timestamp = DateTime.UtcNow, IsSuspect = suspect, Fault = fault
    };

    Task.Run(async () => await store.AddDeath(record));

    if (suspect && config.AutoPromptOnSuspectKill)
      Messenger.Message(victim, Locale[RdmMsgs.RDM_PROMPT(killer.Name)]);
  }
}
```

- [ ] **Step 5: Register `IRdmStore` (in-memory) for tests** in `TTT/Test/Startup.cs`

Add `using TTT.RDM;` and, near the other `AddScoped` lines, add:
```csharp
    services.AddScoped<IRdmStore, InMemoryRdmStore>();
```

- [ ] **Step 6: Build and run tests**

Run: `dotnet build TTT/Test/Test.csproj -v q --nologo && ./TTT/Test/bin/Debug/net10.0/Test`
Expected: PASS — `DeathLogListenerTests` pass (the rebuild regenerates `lang/en.json` with `RDM_PROMPT`). No NEW failures.

- [ ] **Step 7: Commit**

```bash
git add TTT/RDM/DeathLogListener.cs TTT/RDM/lang TTT/Test/Startup.cs TTT/Test/RDM/DeathLogListenerTests.cs
git commit -m "feat(rdm): death log listener with suspect detection + victim prompt"
```

---

### Task 7: Case manager (report lifecycle + staff notification)

**Files:**
- Create: `TTT/RDM/ICaseManager.cs`
- Create: `TTT/RDM/CaseManager.cs`
- Modify: `TTT/RDM/lang/RdmMsgs.cs` (add `RDM_STAFF_NEW_REPORT`)
- Modify: `TTT/RDM/lang/en.yml`
- Test: `TTT/Test/RDM/CaseManagerTests.cs`

**Interfaces:**
- Produces: `interface ICaseManager` with:
  - `Task<RdmCase?> FileReport(IOnlinePlayer reporter, int deathId, string? reason)` — null if rejected (no such death / not the victim / duplicate / over per-round cap / outside report window). On success persists a case (`Open`) and notifies staff.
  - `Task<RdmCase?> ClaimNext(IPlayer admin)` — claims the oldest open (`Open`) case for admin → `Claimed`; null if none.
  - `Task<RdmCase?> Claim(IPlayer admin, int caseId)` — claim a specific case; null if missing/already resolved.
  - `Task Resolve(int caseId, Verdict verdict, IPlayer admin)` — set `Resolved` + verdict + handler.
  - `Task<IReadOnlyList<RdmCase>> GetOpen()`.
- Produces: `class CaseManager : ICaseManager`.

- [ ] **Step 1: Add localization keys**

In `TTT/RDM/lang/RdmMsgs.cs` add:
```csharp
  public static IMsg RDM_STAFF_NEW_REPORT(string victim, string attacker,
    int caseId) {
    return MsgFactory.Create(nameof(RDM_STAFF_NEW_REPORT), victim, attacker,
      caseId);
  }
```

In `TTT/RDM/lang/en.yml` add:
```yaml
RDM_STAFF_NEW_REPORT: "%PREFIX%{red}[RDM]{grey} New report #{2}: {yellow}{0}{grey} reported {yellow}{1}{grey}."
```

- [ ] **Step 2: Write the failing test** `TTT/Test/RDM/CaseManagerTests.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using TTT.API.Player;
using TTT.Game.Damage;
using TTT.RDM;
using TTT.RDM.Models;
using Xunit;

namespace TTT.Test.RDM;

public class CaseManagerTests {
  private readonly ICaseManager manager;
  private readonly IRdmStore store;
  private readonly IPlayerFinder players;

  public CaseManagerTests(IServiceProvider provider) {
    manager = provider.GetRequiredService<ICaseManager>();
    store   = provider.GetRequiredService<IRdmStore>();
    players = provider.GetRequiredService<IPlayerFinder>();
  }

  private async Task<int> SeedSuspectDeath(IPlayer victim) {
    return await store.AddDeath(new DeathRecord {
      Round = 1, VictimId = victim.Id, VictimName = victim.Name,
      VictimRole = "Innocent", AttackerId = "k", AttackerName = "Killer",
      AttackerRole = "Innocent", Timestamp = DateTime.UtcNow,
      IsSuspect = true, Fault = KillFault.KillerGuilty
    });
  }

  [Fact]
  public async Task FileReport_CreatesOpenCase() {
    var victim  = TestPlayer.Random();
    players.AddPlayer(victim);
    var deathId = await SeedSuspectDeath(victim);

    var c = await manager.FileReport(victim, deathId, "shot me in spawn");
    Assert.NotNull(c);
    Assert.Equal(CaseState.Open, c!.State);
    Assert.Single(await manager.GetOpen());
  }

  [Fact]
  public async Task FileReport_ByNonVictim_Rejected() {
    var victim   = TestPlayer.Random();
    var other    = TestPlayer.Random();
    players.AddPlayers(victim, other);
    var deathId  = await SeedSuspectDeath(victim);
    Assert.Null(await manager.FileReport(other, deathId, null));
  }

  [Fact]
  public async Task FileReport_Duplicate_Rejected() {
    var victim  = TestPlayer.Random();
    players.AddPlayer(victim);
    var deathId = await SeedSuspectDeath(victim);
    Assert.NotNull(await manager.FileReport(victim, deathId, null));
    Assert.Null(await manager.FileReport(victim, deathId, null));
  }

  [Fact]
  public async Task ClaimNext_MovesOldestToClaimed() {
    var victim  = TestPlayer.Random();
    players.AddPlayer(victim);
    var deathId = await SeedSuspectDeath(victim);
    await manager.FileReport(victim, deathId, null);

    var admin = TestPlayer.Random();
    var c     = await manager.ClaimNext(admin);
    Assert.NotNull(c);
    Assert.Equal(CaseState.Claimed, c!.State);
    Assert.Equal(admin.Id, c.HandlerAdminId);
  }

  [Fact]
  public async Task Resolve_ClosesCase() {
    var victim  = TestPlayer.Random();
    players.AddPlayer(victim);
    var deathId = await SeedSuspectDeath(victim);
    var c       = await manager.FileReport(victim, deathId, null);
    var admin   = TestPlayer.Random();

    await manager.Resolve(c!.Id, Verdict.Forgiven, admin);
    Assert.Empty(await manager.GetOpen());
    Assert.Equal(Verdict.Forgiven, (await store.GetCase(c.Id))!.Verdict);
  }
}
```

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet build TTT/Test/Test.csproj -v q --nologo`
Expected: FAIL to compile — `ICaseManager`/`CaseManager` do not exist.

- [ ] **Step 4: Write `TTT/RDM/ICaseManager.cs`**

```csharp
using TTT.API.Player;
using TTT.RDM.Models;

namespace TTT.RDM;

public interface ICaseManager {
  Task<RdmCase?> FileReport(IOnlinePlayer reporter, int deathId,
    string? reason);
  Task<RdmCase?> ClaimNext(IPlayer admin);
  Task<RdmCase?> Claim(IPlayer admin, int caseId);
  Task Resolve(int caseId, Verdict verdict, IPlayer admin);
  Task<IReadOnlyList<RdmCase>> GetOpen();
}
```

- [ ] **Step 5: Write `TTT/RDM/CaseManager.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using TTT.API.Messages;
using TTT.API.Player;
using TTT.API.Storage;
using TTT.Locale;
using TTT.RDM.lang;
using TTT.RDM.Models;

namespace TTT.RDM;

public class CaseManager(IServiceProvider provider) : ICaseManager {
  private readonly IRdmStore store = provider.GetRequiredService<IRdmStore>();

  private readonly IPlayerFinder finder =
    provider.GetRequiredService<IPlayerFinder>();

  private readonly IPermissionManager perms =
    provider.GetRequiredService<IPermissionManager>();

  private readonly IMessenger messenger =
    provider.GetRequiredService<IMessenger>();

  private readonly IMsgLocalizer locale =
    provider.GetRequiredService<IMsgLocalizer>();

  private RdmConfig config
    => provider.GetService<IStorage<RdmConfig>>()?.Load().GetAwaiter()
      .GetResult() ?? new RdmConfig();

  public async Task<RdmCase?> FileReport(IOnlinePlayer reporter, int deathId,
    string? reason) {
    var death = await store.GetDeath(deathId);
    if (death == null) return null;
    if (death.VictimId != reporter.Id) return null;          // only the victim
    if (await store.HasReport(reporter.Id, deathId)) return null; // duplicate

    var cfg = config;
    if (await store.CountReportsByVictim(reporter.Id, death.Round)
      >= cfg.MaxReportsPerVictimPerRound) return null;       // per-round cap

    var age = DateTime.UtcNow - death.Timestamp;
    if (age.TotalSeconds > cfg.ReportWindowSeconds) return null; // window

    var id = await store.AddCase(new RdmCase {
      DeathId = deathId, ReporterId = reporter.Id, Reason = reason,
      State = CaseState.Open, CreatedAt = DateTime.UtcNow
    });
    var created = await store.GetCase(id);

    if (cfg.NotifyAdmins && created != null)
      NotifyStaff(death, created);
    return created;
  }

  private void NotifyStaff(DeathRecord death, RdmCase rdmCase) {
    var staffFlag = config.StaffFlag;
    foreach (var online in finder.GetOnline())
      if (perms.HasFlags(online, staffFlag))
        messenger.Message(online,
          locale[RdmMsgs.RDM_STAFF_NEW_REPORT(death.VictimName,
            death.AttackerName, rdmCase.Id)]);
  }

  public async Task<RdmCase?> ClaimNext(IPlayer admin) {
    var open = await store.GetOpenCases();
    var next = open.FirstOrDefault(c => c.State == CaseState.Open);
    if (next == null) return null;
    return await Claim(admin, next.Id);
  }

  public async Task<RdmCase?> Claim(IPlayer admin, int caseId) {
    var c = await store.GetCase(caseId);
    if (c == null || c.State == CaseState.Resolved) return null;
    var claimed = c with {
      State = CaseState.Claimed, HandlerAdminId = admin.Id
    };
    await store.UpdateCase(claimed);
    return claimed;
  }

  public async Task Resolve(int caseId, Verdict verdict, IPlayer admin) {
    var c = await store.GetCase(caseId);
    if (c == null) return;
    await store.UpdateCase(c with {
      State = CaseState.Resolved, Verdict = verdict,
      HandlerAdminId = admin.Id
    });
  }

  public Task<IReadOnlyList<RdmCase>> GetOpen() { return store.GetOpenCases(); }
}
```

- [ ] **Step 6: Register `ICaseManager` for tests** in `TTT/Test/Startup.cs`

Add near the other `AddScoped` lines:
```csharp
    services.AddScoped<ICaseManager, CaseManager>();
```

- [ ] **Step 7: Build and run tests**

Run: `dotnet build TTT/Test/Test.csproj -v q --nologo && ./TTT/Test/bin/Debug/net10.0/Test`
Expected: PASS — `CaseManagerTests` pass. No NEW failures.

- [ ] **Step 8: Commit**

```bash
git add TTT/RDM/ICaseManager.cs TTT/RDM/CaseManager.cs TTT/RDM/lang TTT/Test/Startup.cs TTT/Test/RDM/CaseManagerTests.cs
git commit -m "feat(rdm): case manager (report lifecycle + staff notify)"
```

---

### Task 8: `!rdm` victim command

Lists the victim's recent suspect deaths this round; `!rdm <n> [reason]` files a report for the n-th listed death.

**Files:**
- Modify: `TTT/RDM/lang/RdmMsgs.cs` (add `RDM_LIST_HEADER`, `RDM_LIST_ENTRY`, `RDM_LIST_EMPTY`, `RDM_REPORT_FILED`, `RDM_REPORT_REJECTED`)
- Modify: `TTT/RDM/lang/en.yml`
- Create: `TTT/RDM/Commands/RdmCommand.cs`
- Test: `TTT/Test/RDM/RdmCommandTests.cs`

**Interfaces:**
- Consumes: `ICaseManager`, `IRdmStore`, `DeathLogListener.CurrentRound`, `ICommandInfo.Args`.
- Produces: `class RdmCommand : ICommand` with `Id => "rdm"`.

- [ ] **Step 1: Add localization keys**

In `TTT/RDM/lang/RdmMsgs.cs` add:
```csharp
  public static IMsg RDM_LIST_HEADER() {
    return MsgFactory.Create(nameof(RDM_LIST_HEADER));
  }

  public static IMsg RDM_LIST_ENTRY(int index, string attacker) {
    return MsgFactory.Create(nameof(RDM_LIST_ENTRY), index, attacker);
  }

  public static IMsg RDM_LIST_EMPTY() {
    return MsgFactory.Create(nameof(RDM_LIST_EMPTY));
  }

  public static IMsg RDM_REPORT_FILED(int caseId) {
    return MsgFactory.Create(nameof(RDM_REPORT_FILED), caseId);
  }

  public static IMsg RDM_REPORT_REJECTED() {
    return MsgFactory.Create(nameof(RDM_REPORT_REJECTED));
  }
```

In `TTT/RDM/lang/en.yml` add:
```yaml
RDM_LIST_HEADER: "%PREFIX%Recent suspect deaths — type {yellow}!rdm <number>{grey} to report:"
RDM_LIST_ENTRY: "{yellow}{0}{grey}. Killed by {yellow}{1}"
RDM_LIST_EMPTY: "%PREFIX%You have no recent suspect deaths to report."
RDM_REPORT_FILED: "%PREFIX%Report {yellow}#{0}{grey} filed. Staff have been notified."
RDM_REPORT_REJECTED: "%PREFIX%{red}Could not file that report{grey} (too late, already reported, or invalid)."
```

- [ ] **Step 2: Write the failing test** `TTT/Test/RDM/RdmCommandTests.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using TTT.API.Command;
using TTT.API.Player;
using TTT.Game.Damage;
using TTT.Locale;
using TTT.RDM;
using TTT.RDM.Commands;
using TTT.RDM.lang;
using TTT.RDM.Models;
using TTT.Test.Game.Command;
using Xunit;

namespace TTT.Test.RDM;

public class RdmCommandTests : CommandTest {
  private readonly IRdmStore store;
  private readonly IMsgLocalizer locale;
  private readonly IPlayerFinder players;

  public RdmCommandTests(IServiceProvider provider) : base(provider,
    new RdmCommand(provider)) {
    store   = provider.GetRequiredService<IRdmStore>();
    locale  = provider.GetRequiredService<IMsgLocalizer>();
    players = provider.GetRequiredService<IPlayerFinder>();
  }

  private async Task<int> SeedSuspectDeath(IPlayer victim) {
    return await store.AddDeath(new DeathRecord {
      Round = 0, VictimId = victim.Id, VictimName = victim.Name,
      VictimRole = "Innocent", AttackerId = "k", AttackerName = "Killer",
      AttackerRole = "Innocent", Timestamp = DateTime.UtcNow,
      IsSuspect = true, Fault = KillFault.KillerGuilty
    });
  }

  [Fact]
  public async Task NoArgs_NoDeaths_ShowsEmpty() {
    var victim = TestPlayer.Random();
    players.AddPlayer(victim);
    var info = new TestCommandInfo(Provider, victim, "rdm");
    var result = await Commands.ProcessCommand(info);
    Assert.Equal(CommandResult.SUCCESS, result);
    Assert.Contains(locale[RdmMsgs.RDM_LIST_EMPTY()], victim.Messages);
  }

  [Fact]
  public async Task NoArgs_WithDeaths_ListsThem() {
    var victim = TestPlayer.Random();
    players.AddPlayer(victim);
    await SeedSuspectDeath(victim);
    var info = new TestCommandInfo(Provider, victim, "rdm");
    Assert.Equal(CommandResult.SUCCESS, await Commands.ProcessCommand(info));
    Assert.Contains(locale[RdmMsgs.RDM_LIST_ENTRY(1, "Killer")],
      victim.Messages);
  }

  [Fact]
  public async Task WithIndex_FilesReport() {
    var victim = TestPlayer.Random();
    players.AddPlayer(victim);
    await SeedSuspectDeath(victim);
    var info = new TestCommandInfo(Provider, victim, "rdm", "1");
    Assert.Equal(CommandResult.SUCCESS, await Commands.ProcessCommand(info));
    Assert.Single(await store.GetOpenCases());
    Assert.Contains(locale[RdmMsgs.RDM_REPORT_FILED(1)], victim.Messages);
  }
}
```

Note: `CurrentRound` starts at 0 before any round-start event, so the test seeds deaths with `Round = 0` to match what `RdmCommand` queries in a no-game test context.

- [ ] **Step 3: Run test to verify it fails**

Run: `dotnet build TTT/Test/Test.csproj -v q --nologo`
Expected: FAIL to compile — `RdmCommand` does not exist.

- [ ] **Step 4: Write `TTT/RDM/Commands/RdmCommand.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using TTT.API.Command;
using TTT.API.Player;
using TTT.Locale;
using TTT.RDM.lang;

namespace TTT.RDM.Commands;

public class RdmCommand(IServiceProvider provider) : ICommand {
  private readonly ICaseManager cases =
    provider.GetRequiredService<ICaseManager>();

  private readonly IRdmStore store = provider.GetRequiredService<IRdmStore>();

  private readonly IMsgLocalizer locale =
    provider.GetRequiredService<IMsgLocalizer>();

  private readonly DeathLogListener deathLog =
    provider.GetRequiredService<DeathLogListener>();

  public void Dispose() { }
  public void Start() { }
  public string Id => "rdm";
  public string? Description => "File or dismiss an RDM report for your death";
  public string[] Usage => ["[number] [reason]"];

  public async Task<CommandResult> Execute(IOnlinePlayer? executor,
    ICommandInfo info) {
    if (executor == null) return CommandResult.PLAYER_ONLY;

    var round  = deathLog.CurrentRound;
    var deaths = await store.GetSuspectDeathsForVictim(executor.Id, round);

    if (info.ArgCount == 0) {
      if (deaths.Count == 0) {
        info.ReplySync(locale[RdmMsgs.RDM_LIST_EMPTY()]);
        return CommandResult.SUCCESS;
      }

      info.ReplySync(locale[RdmMsgs.RDM_LIST_HEADER()]);
      for (var i = 0; i < deaths.Count; i++)
        info.ReplySync(
          locale[RdmMsgs.RDM_LIST_ENTRY(i + 1, deaths[i].AttackerName)]);
      return CommandResult.SUCCESS;
    }

    if (!int.TryParse(info.Args[0], out var index) || index < 1
      || index > deaths.Count) {
      info.ReplySync(locale[RdmMsgs.RDM_REPORT_REJECTED()]);
      return CommandResult.INVALID_ARGS;
    }

    var reason = info.ArgCount > 1
      ? string.Join(' ', info.Args.Skip(1))
      : null;
    var filed = await cases.FileReport(executor, deaths[index - 1].Id, reason);

    info.ReplySync(filed == null
      ? locale[RdmMsgs.RDM_REPORT_REJECTED()]
      : locale[RdmMsgs.RDM_REPORT_FILED(filed.Id)]);
    return filed == null ? CommandResult.ERROR : CommandResult.SUCCESS;
  }
}
```

- [ ] **Step 5: Register `DeathLogListener` as a resolvable service for tests**

The command resolves `DeathLogListener` directly, so it must be registerable. In `TTT/Test/Startup.cs`, add near the other `AddScoped` lines:
```csharp
    services.AddScoped<DeathLogListener>();
```

- [ ] **Step 6: Build and run tests**

Run: `dotnet build TTT/Test/Test.csproj -v q --nologo && ./TTT/Test/bin/Debug/net10.0/Test`
Expected: PASS — `RdmCommandTests` pass. No NEW failures.

- [ ] **Step 7: Commit**

```bash
git add TTT/RDM/Commands/RdmCommand.cs TTT/RDM/lang TTT/Test/Startup.cs TTT/Test/RDM/RdmCommandTests.cs
git commit -m "feat(rdm): !rdm victim report command"
```

---

### Task 9: Staff `!cases` and `!info` commands

**Files:**
- Modify: `TTT/RDM/lang/RdmMsgs.cs` (add `RDM_CASES_COUNT`, `RDM_CASES_ENTRY`, `RDM_INFO`, `RDM_CASE_NOT_FOUND`)
- Modify: `TTT/RDM/lang/en.yml`
- Create: `TTT/RDM/Commands/CasesCommand.cs`
- Create: `TTT/RDM/Commands/InfoCommand.cs`
- Test: `TTT/Test/RDM/StaffQueryCommandsTests.cs`

**Interfaces:**
- Consumes: `ICaseManager.GetOpen()`, `IRdmStore.GetCase`/`GetDeath`.
- Produces: `class CasesCommand : ICommand` (`Id => "cases"`, `RequiredFlags => [config.StaffFlag]`), `class InfoCommand : ICommand` (`Id => "info"`, staff-gated).

- [ ] **Step 1: Add localization keys**

In `TTT/RDM/lang/RdmMsgs.cs` add:
```csharp
  public static IMsg RDM_CASES_COUNT(int count) {
    return MsgFactory.Create(nameof(RDM_CASES_COUNT), count);
  }

  public static IMsg RDM_CASES_ENTRY(int caseId, string victim,
    string attacker) {
    return MsgFactory.Create(nameof(RDM_CASES_ENTRY), caseId, victim, attacker);
  }

  public static IMsg RDM_INFO(int caseId, string victim, string victimRole,
    string attacker, string attackerRole, string weapon, string reason) {
    return MsgFactory.Create(nameof(RDM_INFO), caseId, victim, victimRole,
      attacker, attackerRole, weapon, reason);
  }

  public static IMsg RDM_CASE_NOT_FOUND() {
    return MsgFactory.Create(nameof(RDM_CASE_NOT_FOUND));
  }
```

In `TTT/RDM/lang/en.yml` add:
```yaml
RDM_CASES_COUNT: "%PREFIX%There {yellow}{0}{grey} open RDM case%s%."
RDM_CASES_ENTRY: "{yellow}#{0}{grey}: {yellow}{1}{grey} vs {yellow}{2}"
RDM_INFO: "%PREFIX%Case {yellow}#{0}{grey}: {yellow}{1}{grey} ({3}) killed by {yellow}{2}{grey} ({4}) with {yellow}{5}{grey}. Reason: {6}"
RDM_CASE_NOT_FOUND: "%PREFIX%{red}No such case.{grey}"
```

Note: `RDM_INFO`'s `{1}` is the victim and `{2}` the attacker; placeholders `{3}`/`{4}` are their roles to keep the sentence readable.

- [ ] **Step 2: Write the failing test** `TTT/Test/RDM/StaffQueryCommandsTests.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using TTT.API.Command;
using TTT.API.Player;
using TTT.Game.Damage;
using TTT.Locale;
using TTT.RDM;
using TTT.RDM.Commands;
using TTT.RDM.lang;
using TTT.RDM.Models;
using TTT.Test.Fakes;
using TTT.Test.Game.Command;
using Xunit;

namespace TTT.Test.RDM;

public class StaffQueryCommandsTests {
  private readonly IServiceProvider provider;
  private readonly ICommandManager commands;
  private readonly ICaseManager manager;
  private readonly IRdmStore store;
  private readonly IMsgLocalizer locale;
  private readonly IPlayerFinder players;
  private readonly FakePermissionManager perms;

  public StaffQueryCommandsTests(IServiceProvider provider) {
    this.provider = provider;
    commands = provider.GetRequiredService<ICommandManager>();
    manager  = provider.GetRequiredService<ICaseManager>();
    store    = provider.GetRequiredService<IRdmStore>();
    locale   = provider.GetRequiredService<IMsgLocalizer>();
    players  = provider.GetRequiredService<IPlayerFinder>();
    perms    = (FakePermissionManager)provider
     .GetRequiredService<IPermissionManager>();
    commands.RegisterCommand(new CasesCommand(provider));
    commands.RegisterCommand(new InfoCommand(provider));
  }

  private async Task<RdmCase> SeedOpenCase(IOnlinePlayer victim) {
    var deathId = await store.AddDeath(new DeathRecord {
      Round = 0, VictimId = victim.Id, VictimName = victim.Name,
      VictimRole = "Innocent", AttackerId = "k", AttackerName = "Killer",
      AttackerRole = "Innocent", Weapon = "ak47", Timestamp = DateTime.UtcNow,
      IsSuspect = true, Fault = KillFault.KillerGuilty
    });
    return (await manager.FileReport(victim, deathId, "spawn kill"))!;
  }

  [Fact]
  public async Task Cases_AsStaff_ShowsCount() {
    var victim = TestPlayer.Random();
    var admin  = TestPlayer.Random();
    players.AddPlayers(victim, admin);
    perms.SetFlags(admin, "@ttt/admin");
    await SeedOpenCase(victim);

    var result = await commands.ProcessCommand(
      new TestCommandInfo(provider, admin, "cases"));
    Assert.Equal(CommandResult.SUCCESS, result);
    Assert.Contains(locale[RdmMsgs.RDM_CASES_COUNT(1)], admin.Messages);
  }

  [Fact]
  public async Task Cases_WithoutFlag_NoPermission() {
    var player = TestPlayer.Random();
    players.AddPlayer(player);
    var result = await commands.ProcessCommand(
      new TestCommandInfo(provider, player, "cases"));
    Assert.Equal(CommandResult.NO_PERMISSION, result);
  }

  [Fact]
  public async Task Info_ShowsCaseDetails() {
    var victim = TestPlayer.Random();
    var admin  = TestPlayer.Random();
    players.AddPlayers(victim, admin);
    perms.SetFlags(admin, "@ttt/admin");
    var c = await SeedOpenCase(victim);

    var result = await commands.ProcessCommand(
      new TestCommandInfo(provider, admin, "info", c.Id.ToString()));
    Assert.Equal(CommandResult.SUCCESS, result);
    Assert.Contains(admin.Messages,
      m => m.Contains("Killer") && m.Contains("spawn kill"));
  }
}
```

- [ ] **Step 3: Confirm `FakePermissionManager` supports `SetFlags`**

Open `TTT/Test/Fakes/FakePermissionManager.cs`. If it lacks a way to grant flags to a player, add:
```csharp
  private readonly Dictionary<string, HashSet<string>> flags = new();

  public void SetFlags(IPlayer player, params string[] grant) {
    flags[player.Id] = [..grant];
  }
```
and make `HasFlags` return `flags.TryGetValue(player.Id, out var f) && requested.All(f.Contains)` (preserve any existing behavior such as `@css/root` always-true if present). Keep the change minimal and consistent with the existing fake.

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet build TTT/Test/Test.csproj -v q --nologo`
Expected: FAIL to compile — `CasesCommand`/`InfoCommand` do not exist.

- [ ] **Step 5: Write `TTT/RDM/Commands/CasesCommand.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using TTT.API.Command;
using TTT.API.Player;
using TTT.API.Storage;
using TTT.Locale;
using TTT.RDM.lang;

namespace TTT.RDM.Commands;

public class CasesCommand(IServiceProvider provider) : ICommand {
  private readonly ICaseManager cases =
    provider.GetRequiredService<ICaseManager>();

  private readonly IMsgLocalizer locale =
    provider.GetRequiredService<IMsgLocalizer>();

  private RdmConfig config
    => provider.GetService<IStorage<RdmConfig>>()?.Load().GetAwaiter()
      .GetResult() ?? new RdmConfig();

  public void Dispose() { }
  public void Start() { }
  public string Id => "cases";
  public string? Description => "List open RDM cases";
  public string[] RequiredFlags => [config.StaffFlag];

  public async Task<CommandResult> Execute(IOnlinePlayer? executor,
    ICommandInfo info) {
    var open = await cases.GetOpen();
    info.ReplySync(locale[RdmMsgs.RDM_CASES_COUNT(open.Count)]);
    foreach (var c in open) {
      var death = await provider.GetRequiredService<IRdmStore>()
       .GetDeath(c.DeathId);
      if (death != null)
        info.ReplySync(locale[RdmMsgs.RDM_CASES_ENTRY(c.Id, death.VictimName,
          death.AttackerName)]);
    }

    return CommandResult.SUCCESS;
  }
}
```

- [ ] **Step 6: Write `TTT/RDM/Commands/InfoCommand.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using TTT.API.Command;
using TTT.API.Player;
using TTT.API.Storage;
using TTT.Locale;
using TTT.RDM.lang;

namespace TTT.RDM.Commands;

public class InfoCommand(IServiceProvider provider) : ICommand {
  private readonly IRdmStore store = provider.GetRequiredService<IRdmStore>();

  private readonly IMsgLocalizer locale =
    provider.GetRequiredService<IMsgLocalizer>();

  private RdmConfig config
    => provider.GetService<IStorage<RdmConfig>>()?.Load().GetAwaiter()
      .GetResult() ?? new RdmConfig();

  public void Dispose() { }
  public void Start() { }
  public string Id => "info";
  public string? Description => "Show details for an RDM case";
  public string[] Usage => ["<caseId>"];
  public string[] RequiredFlags => [config.StaffFlag];

  public async Task<CommandResult> Execute(IOnlinePlayer? executor,
    ICommandInfo info) {
    if (info.ArgCount < 1 || !int.TryParse(info.Args[0], out var caseId))
      return CommandResult.PRINT_USAGE;

    var c = await store.GetCase(caseId);
    var death = c == null ? null : await store.GetDeath(c.DeathId);
    if (c == null || death == null) {
      info.ReplySync(locale[RdmMsgs.RDM_CASE_NOT_FOUND()]);
      return CommandResult.ERROR;
    }

    info.ReplySync(locale[RdmMsgs.RDM_INFO(c.Id, death.VictimName,
      death.VictimRole, death.AttackerName, death.AttackerRole,
      death.Weapon ?? "unknown", c.Reason ?? "(none)")]);
    return CommandResult.SUCCESS;
  }
}
```

- [ ] **Step 7: Build and run tests**

Run: `dotnet build TTT/Test/Test.csproj -v q --nologo && ./TTT/Test/bin/Debug/net10.0/Test`
Expected: PASS — `StaffQueryCommandsTests` pass. No NEW failures.

- [ ] **Step 8: Commit**

```bash
git add TTT/RDM/Commands/CasesCommand.cs TTT/RDM/Commands/InfoCommand.cs TTT/RDM/lang TTT/Test/Fakes/FakePermissionManager.cs TTT/Test/RDM/StaffQueryCommandsTests.cs
git commit -m "feat(rdm): staff !cases and !info commands"
```

---

### Task 10: Slay service + slay-queue listener

**Files:**
- Create: `TTT/RDM/ISlayService.cs`
- Create: `TTT/RDM/SlayService.cs`
- Create: `TTT/RDM/SlayQueueListener.cs`
- Test: `TTT/Test/RDM/SlayServiceTests.cs`

**Interfaces:**
- Produces: `interface ISlayService { Task ApplyGuilty(IPlayer offender, string victimRole, int caseId); Task<int> PayRoundStart(); }`
  - `ApplyGuilty`: owed = `RdmConfig.SlaysForRole(victimRole)`; if offender is online + alive, slay immediately (`Health = 0`) and persist `owed - 1`, else persist `owed`.
  - `PayRoundStart`: for each slay debt, if the player is online + alive, slay once and decrement; returns the number of slays applied.
- Produces: `class SlayService : ISlayService`, `class SlayQueueListener : BaseListener` (calls `PayRoundStart` on `State.IN_PROGRESS`).

- [ ] **Step 1: Write the failing test** `TTT/Test/RDM/SlayServiceTests.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using TTT.API.Player;
using TTT.RDM;
using Xunit;

namespace TTT.Test.RDM;

public class SlayServiceTests {
  private readonly ISlayService slay;
  private readonly IRdmStore store;
  private readonly IPlayerFinder players;

  public SlayServiceTests(IServiceProvider provider) {
    slay    = provider.GetRequiredService<ISlayService>();
    store   = provider.GetRequiredService<IRdmStore>();
    players = provider.GetRequiredService<IPlayerFinder>();
  }

  [Fact]
  public async Task ApplyGuilty_AliveOffender_SlaysNowAndQueuesRest() {
    var offender = TestPlayer.Random();
    offender.IsAlive = true;
    offender.Health  = 100;
    players.AddPlayer(offender);

    await slay.ApplyGuilty(offender, "Innocent", caseId: 1); // 3 slays

    Assert.Equal(0, offender.Health);          // immediate slay applied
    Assert.Equal(2, await store.GetSlayDebt(offender.Id)); // 3 - 1
  }

  [Fact]
  public async Task ApplyGuilty_DeadOffender_QueuesAll() {
    var offender = TestPlayer.Random();
    offender.IsAlive = false;
    players.AddPlayer(offender);

    await slay.ApplyGuilty(offender, "Traitor", caseId: 1); // 5 slays
    Assert.Equal(5, await store.GetSlayDebt(offender.Id));
  }

  [Fact]
  public async Task PayRoundStart_SlaysAliveDebtorsOncePerRound() {
    var offender = TestPlayer.Random();
    offender.IsAlive = false;
    players.AddPlayer(offender);
    await slay.ApplyGuilty(offender, "Innocent", caseId: 1); // queues 3

    // Round 1: offender respawns alive, pays one slay.
    offender.IsAlive = true;
    offender.Health  = 100;
    var applied = await slay.PayRoundStart();
    Assert.Equal(1, applied);
    Assert.Equal(0, offender.Health);
    Assert.Equal(2, await store.GetSlayDebt(offender.Id));
  }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build TTT/Test/Test.csproj -v q --nologo`
Expected: FAIL to compile — `ISlayService` does not exist.

- [ ] **Step 3: Write `TTT/RDM/ISlayService.cs`**

```csharp
using TTT.API.Player;

namespace TTT.RDM;

public interface ISlayService {
  /// <summary>Apply a guilty verdict: slay now if alive, queue the remainder.</summary>
  Task ApplyGuilty(IPlayer offender, string victimRole, int caseId);

  /// <summary>Pay one slay for each alive debtor at round start. Returns slays applied.</summary>
  Task<int> PayRoundStart();
}
```

- [ ] **Step 4: Write `TTT/RDM/SlayService.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using TTT.API.Player;
using TTT.API.Storage;

namespace TTT.RDM;

public class SlayService(IServiceProvider provider) : ISlayService {
  private readonly IRdmStore store = provider.GetRequiredService<IRdmStore>();

  private readonly IPlayerFinder finder =
    provider.GetRequiredService<IPlayerFinder>();

  private RdmConfig config
    => provider.GetService<IStorage<RdmConfig>>()?.Load().GetAwaiter()
      .GetResult() ?? new RdmConfig();

  public async Task ApplyGuilty(IPlayer offender, string victimRole,
    int caseId) {
    var owed     = config.SlaysForRole(victimRole);
    var existing = await store.GetSlayDebt(offender.Id);
    var total    = existing + owed;

    var online = finder.GetPlayerById(offender.Id);
    if (online is { IsAlive: true }) {
      online.Health = 0; // immediate slay
      total--;
    }

    await store.SetSlayDebt(offender.Id, total, caseId);
  }

  public async Task<int> PayRoundStart() {
    var applied = 0;
    foreach (var debt in await store.GetAllSlayDebts()) {
      var online = finder.GetPlayerById(debt.PlayerId);
      if (online is not { IsAlive: true }) continue;
      online.Health = 0;
      await store.SetSlayDebt(debt.PlayerId, debt.RemainingSlays - 1,
        debt.SourceCaseId);
      applied++;
    }

    return applied;
  }
}
```

- [ ] **Step 5: Write `TTT/RDM/SlayQueueListener.cs`**

```csharp
using JetBrains.Annotations;
using Microsoft.Extensions.DependencyInjection;
using TTT.API.Events;
using TTT.API.Game;
using TTT.Game.Events.Game;
using TTT.Game.Listeners;

namespace TTT.RDM;

public class SlayQueueListener(IServiceProvider provider)
  : BaseListener(provider) {
  private readonly ISlayService slay =
    provider.GetRequiredService<ISlayService>();

  [EventHandler]
  [UsedImplicitly]
  public void OnRoundStart(GameStateUpdateEvent ev) {
    if (ev.NewState != State.IN_PROGRESS) return;
    Task.Run(async () => await slay.PayRoundStart());
  }
}
```

- [ ] **Step 6: Register `ISlayService` for tests** in `TTT/Test/Startup.cs`

Add near the other `AddScoped` lines:
```csharp
    services.AddScoped<ISlayService, SlayService>();
```

- [ ] **Step 7: Build and run tests**

Run: `dotnet build TTT/Test/Test.csproj -v q --nologo && ./TTT/Test/bin/Debug/net10.0/Test`
Expected: PASS — `SlayServiceTests` pass. No NEW failures.

- [ ] **Step 8: Commit**

```bash
git add TTT/RDM/ISlayService.cs TTT/RDM/SlayService.cs TTT/RDM/SlayQueueListener.cs TTT/Test/Startup.cs TTT/Test/RDM/SlayServiceTests.cs
git commit -m "feat(rdm): slay service + round-start slay queue"
```

---

### Task 11: Staff `!handle` and `!verdict` commands

`!handle [id]` claims the next open case (or a specific id) for the calling admin. `!verdict <forgive|guilty>` resolves the admin's currently-claimed case; guilty triggers slays via `ISlayService`.

**Files:**
- Modify: `TTT/RDM/lang/RdmMsgs.cs` (add `RDM_HANDLED`, `RDM_NO_OPEN_CASES`, `RDM_NO_CLAIMED_CASE`, `RDM_VERDICT_USAGE`, `RDM_VERDICT_GUILTY`, `RDM_VERDICT_FORGIVEN`)
- Modify: `TTT/RDM/lang/en.yml`
- Create: `TTT/RDM/Commands/HandleCommand.cs`
- Create: `TTT/RDM/Commands/VerdictCommand.cs`
- Test: `TTT/Test/RDM/VerdictFlowTests.cs`

**Interfaces:**
- Consumes: `ICaseManager.ClaimNext`/`Claim`/`Resolve`/`GetOpen`, `IRdmStore.GetDeath`/`GetCase`, `ISlayService.ApplyGuilty`, `IPlayerFinder.GetPlayerById`.
- Produces: `class HandleCommand : ICommand` (`Id => "handle"`, staff-gated), `class VerdictCommand : ICommand` (`Id => "verdict"`, staff-gated). Both track the admin's claimed case via a shared in-memory map kept on `CaseManager`. To avoid extra shared state, `VerdictCommand` resolves the admin's claimed case by querying open cases where `HandlerAdminId == admin.Id && State == Claimed`.

- [ ] **Step 1: Add localization keys**

In `TTT/RDM/lang/RdmMsgs.cs` add:
```csharp
  public static IMsg RDM_HANDLED(int caseId, string victim, string attacker) {
    return MsgFactory.Create(nameof(RDM_HANDLED), caseId, victim, attacker);
  }

  public static IMsg RDM_NO_OPEN_CASES() {
    return MsgFactory.Create(nameof(RDM_NO_OPEN_CASES));
  }

  public static IMsg RDM_NO_CLAIMED_CASE() {
    return MsgFactory.Create(nameof(RDM_NO_CLAIMED_CASE));
  }

  public static IMsg RDM_VERDICT_USAGE() {
    return MsgFactory.Create(nameof(RDM_VERDICT_USAGE));
  }

  public static IMsg RDM_VERDICT_GUILTY(int caseId, int slays) {
    return MsgFactory.Create(nameof(RDM_VERDICT_GUILTY), caseId, slays);
  }

  public static IMsg RDM_VERDICT_FORGIVEN(int caseId) {
    return MsgFactory.Create(nameof(RDM_VERDICT_FORGIVEN), caseId);
  }
```

In `TTT/RDM/lang/en.yml` add:
```yaml
RDM_HANDLED: "%PREFIX%Now handling case {yellow}#{0}{grey}: {yellow}{1}{grey} vs {yellow}{2}{grey}. Use {yellow}!verdict guilty|forgive{grey}."
RDM_NO_OPEN_CASES: "%PREFIX%There are no open RDM cases."
RDM_NO_CLAIMED_CASE: "%PREFIX%{red}You are not handling a case.{grey} Use {yellow}!handle{grey} first."
RDM_VERDICT_USAGE: "%PREFIX%Usage: {yellow}!verdict guilty{grey} or {yellow}!verdict forgive{grey}."
RDM_VERDICT_GUILTY: "%PREFIX%Case {yellow}#{0}{grey}: {red}GUILTY{grey} — {yellow}{1}{grey} slay%s% queued."
RDM_VERDICT_FORGIVEN: "%PREFIX%Case {yellow}#{0}{grey}: {green}forgiven{grey}."
```

- [ ] **Step 2: Add `ICaseManager.GetClaimedBy`**

In `TTT/RDM/ICaseManager.cs` add:
```csharp
  Task<RdmCase?> GetClaimedBy(string adminId);
```
In `TTT/RDM/CaseManager.cs` implement:
```csharp
  public async Task<RdmCase?> GetClaimedBy(string adminId) {
    var open = await store.GetOpenCases();
    return open.FirstOrDefault(c
      => c.State == CaseState.Claimed && c.HandlerAdminId == adminId);
  }
```

- [ ] **Step 3: Write the failing test** `TTT/Test/RDM/VerdictFlowTests.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using TTT.API.Command;
using TTT.API.Player;
using TTT.Game.Damage;
using TTT.Locale;
using TTT.RDM;
using TTT.RDM.Commands;
using TTT.RDM.lang;
using TTT.RDM.Models;
using TTT.Test.Fakes;
using TTT.Test.Game.Command;
using Xunit;

namespace TTT.Test.RDM;

public class VerdictFlowTests {
  private readonly IServiceProvider provider;
  private readonly ICommandManager commands;
  private readonly ICaseManager manager;
  private readonly IRdmStore store;
  private readonly IMsgLocalizer locale;
  private readonly IPlayerFinder players;
  private readonly FakePermissionManager perms;

  public VerdictFlowTests(IServiceProvider provider) {
    this.provider = provider;
    commands = provider.GetRequiredService<ICommandManager>();
    manager  = provider.GetRequiredService<ICaseManager>();
    store    = provider.GetRequiredService<IRdmStore>();
    locale   = provider.GetRequiredService<IMsgLocalizer>();
    players  = provider.GetRequiredService<IPlayerFinder>();
    perms    = (FakePermissionManager)provider
     .GetRequiredService<IPermissionManager>();
    commands.RegisterCommand(new HandleCommand(provider));
    commands.RegisterCommand(new VerdictCommand(provider));
  }

  private async Task<(TestPlayer offender, TestPlayer admin, RdmCase c)>
    SeedReportedCase() {
    var victim   = TestPlayer.Random();
    var offender = TestPlayer.Random();
    var admin    = TestPlayer.Random();
    offender.IsAlive = false;
    players.AddPlayers(victim, offender, admin);
    perms.SetFlags(admin, "@ttt/admin");

    var deathId = await store.AddDeath(new DeathRecord {
      Round = 0, VictimId = victim.Id, VictimName = victim.Name,
      VictimRole = "Innocent", AttackerId = offender.Id,
      AttackerName = offender.Name, AttackerRole = "Innocent",
      Weapon = "ak47", Timestamp = DateTime.UtcNow, IsSuspect = true,
      Fault = KillFault.KillerGuilty
    });
    var c = (await manager.FileReport(victim, deathId, "rdm"))!;
    return (offender, admin, c);
  }

  [Fact]
  public async Task Handle_ThenGuilty_QueuesSlays() {
    var (offender, admin, c) = await SeedReportedCase();

    Assert.Equal(CommandResult.SUCCESS, await commands.ProcessCommand(
      new TestCommandInfo(provider, admin, "handle")));
    Assert.Equal(CommandResult.SUCCESS, await commands.ProcessCommand(
      new TestCommandInfo(provider, admin, "verdict", "guilty")));

    Assert.Equal(3, await store.GetSlayDebt(offender.Id)); // innocent victim
    Assert.Empty(await manager.GetOpen());                 // case resolved
  }

  [Fact]
  public async Task Handle_ThenForgive_NoSlays() {
    var (offender, admin, _) = await SeedReportedCase();
    await commands.ProcessCommand(
      new TestCommandInfo(provider, admin, "handle"));
    Assert.Equal(CommandResult.SUCCESS, await commands.ProcessCommand(
      new TestCommandInfo(provider, admin, "verdict", "forgive")));

    Assert.Equal(0, await store.GetSlayDebt(offender.Id));
    Assert.Empty(await manager.GetOpen());
  }

  [Fact]
  public async Task Verdict_WithoutClaimedCase_Errors() {
    var admin = TestPlayer.Random();
    players.AddPlayer(admin);
    perms.SetFlags(admin, "@ttt/admin");
    var result = await commands.ProcessCommand(
      new TestCommandInfo(provider, admin, "verdict", "guilty"));
    Assert.Equal(CommandResult.ERROR, result);
    Assert.Contains(locale[RdmMsgs.RDM_NO_CLAIMED_CASE()], admin.Messages);
  }
}
```

- [ ] **Step 4: Run test to verify it fails**

Run: `dotnet build TTT/Test/Test.csproj -v q --nologo`
Expected: FAIL to compile — `HandleCommand`/`VerdictCommand` do not exist.

- [ ] **Step 5: Write `TTT/RDM/Commands/HandleCommand.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using TTT.API.Command;
using TTT.API.Player;
using TTT.API.Storage;
using TTT.Locale;
using TTT.RDM.lang;

namespace TTT.RDM.Commands;

public class HandleCommand(IServiceProvider provider) : ICommand {
  private readonly ICaseManager cases =
    provider.GetRequiredService<ICaseManager>();

  private readonly IRdmStore store = provider.GetRequiredService<IRdmStore>();

  private readonly IMsgLocalizer locale =
    provider.GetRequiredService<IMsgLocalizer>();

  private RdmConfig config
    => provider.GetService<IStorage<RdmConfig>>()?.Load().GetAwaiter()
      .GetResult() ?? new RdmConfig();

  public void Dispose() { }
  public void Start() { }
  public string Id => "handle";
  public string? Description => "Claim the next (or a specific) RDM case";
  public string[] Usage => ["[caseId]"];
  public string[] RequiredFlags => [config.StaffFlag];

  public async Task<CommandResult> Execute(IOnlinePlayer? executor,
    ICommandInfo info) {
    if (executor == null) return CommandResult.PLAYER_ONLY;

    var claimed = info.ArgCount >= 1 && int.TryParse(info.Args[0], out var id)
      ? await cases.Claim(executor, id)
      : await cases.ClaimNext(executor);

    if (claimed == null) {
      info.ReplySync(locale[RdmMsgs.RDM_NO_OPEN_CASES()]);
      return CommandResult.ERROR;
    }

    var death = await store.GetDeath(claimed.DeathId);
    info.ReplySync(locale[RdmMsgs.RDM_HANDLED(claimed.Id,
      death?.VictimName ?? "?", death?.AttackerName ?? "?")]);
    return CommandResult.SUCCESS;
  }
}
```

- [ ] **Step 6: Write `TTT/RDM/Commands/VerdictCommand.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using TTT.API.Command;
using TTT.API.Player;
using TTT.API.Storage;
using TTT.Locale;
using TTT.RDM.lang;
using TTT.RDM.Models;

namespace TTT.RDM.Commands;

public class VerdictCommand(IServiceProvider provider) : ICommand {
  private readonly ICaseManager cases =
    provider.GetRequiredService<ICaseManager>();

  private readonly IRdmStore store = provider.GetRequiredService<IRdmStore>();

  private readonly ISlayService slay =
    provider.GetRequiredService<ISlayService>();

  private readonly IPlayerFinder finder =
    provider.GetRequiredService<IPlayerFinder>();

  private readonly IMsgLocalizer locale =
    provider.GetRequiredService<IMsgLocalizer>();

  private RdmConfig config
    => provider.GetService<IStorage<RdmConfig>>()?.Load().GetAwaiter()
      .GetResult() ?? new RdmConfig();

  public void Dispose() { }
  public void Start() { }
  public string Id => "verdict";
  public string? Description => "Decide your claimed RDM case";
  public string[] Usage => ["<guilty|forgive>"];
  public string[] RequiredFlags => [config.StaffFlag];

  public async Task<CommandResult> Execute(IOnlinePlayer? executor,
    ICommandInfo info) {
    if (executor == null) return CommandResult.PLAYER_ONLY;

    var claimed = await cases.GetClaimedBy(executor.Id);
    if (claimed == null) {
      info.ReplySync(locale[RdmMsgs.RDM_NO_CLAIMED_CASE()]);
      return CommandResult.ERROR;
    }

    if (info.ArgCount < 1) {
      info.ReplySync(locale[RdmMsgs.RDM_VERDICT_USAGE()]);
      return CommandResult.PRINT_USAGE;
    }

    var choice = info.Args[0].ToLowerInvariant();
    var death  = await store.GetDeath(claimed.DeathId);

    switch (choice) {
      case "forgive":
      case "forgiven":
        await cases.Resolve(claimed.Id, Verdict.Forgiven, executor);
        info.ReplySync(locale[RdmMsgs.RDM_VERDICT_FORGIVEN(claimed.Id)]);
        return CommandResult.SUCCESS;
      case "guilty":
        await cases.Resolve(claimed.Id, Verdict.Guilty, executor);
        if (death != null) {
          var offender = finder.GetPlayerById(death.AttackerId);
          var slays    = config.SlaysForRole(death.VictimRole);
          await slay.ApplyGuilty(
            offender ?? new OfflineRef(death.AttackerId), death.VictimRole,
            claimed.Id);
          info.ReplySync(
            locale[RdmMsgs.RDM_VERDICT_GUILTY(claimed.Id, slays)]);
        }

        return CommandResult.SUCCESS;
      default:
        info.ReplySync(locale[RdmMsgs.RDM_VERDICT_USAGE()]);
        return CommandResult.INVALID_ARGS;
    }
  }

  // Minimal IPlayer for an offline offender so slay debt can be persisted by id.
  private sealed record OfflineRef(string Id) : IPlayer {
    public string Name { get; set; } = Id;
  }
}
```

- [ ] **Step 7: Build and run tests**

Run: `dotnet build TTT/Test/Test.csproj -v q --nologo && ./TTT/Test/bin/Debug/net10.0/Test`
Expected: PASS — `VerdictFlowTests` pass. No NEW failures.

- [ ] **Step 8: Commit**

```bash
git add TTT/RDM/Commands/HandleCommand.cs TTT/RDM/Commands/VerdictCommand.cs TTT/RDM/ICaseManager.cs TTT/RDM/CaseManager.cs TTT/RDM/lang TTT/Test/RDM/VerdictFlowTests.cs
git commit -m "feat(rdm): staff !handle and !verdict commands"
```

---

### Task 12: Production wiring + CS2 config + end-to-end test

Wire every RDM service into `RdmServiceCollection` (production), provide a CS2 cvar-backed `RdmConfig`, and add one end-to-end test proving the full path.

**Files:**
- Modify: `TTT/RDM/RdmServiceCollection.cs` (register everything via `AddModBehavior`/`AddSingleton`)
- Create: `TTT/CS2/Configs/CS2RdmConfig.cs`
- Modify: `TTT/CS2/CS2ServiceCollection.cs` (register `IStorage<RdmConfig>`)
- Test: `TTT/Test/RDM/RdmEndToEndTests.cs`

**Interfaces:**
- Consumes: all RDM services from prior tasks.
- Produces: fully registered RDM module + `CS2RdmConfig : IStorage<RdmConfig>, IPluginModule`.

- [ ] **Step 1: Wire production registration** `TTT/RDM/RdmServiceCollection.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using TTT.API.Extensions;
using TTT.RDM.Commands;

namespace TTT.RDM;

public static class RdmServiceCollection {
  public static void AddRdmService(this IServiceCollection collection) {
    // Persistence (production: SQLite). Config is resolved lazily so the
    // connection string comes from IStorage<RdmConfig> when present.
    collection.AddSingleton<IRdmStore>(provider => {
      var config = provider.GetService<TTT.API.Storage.IStorage<RdmConfig>>()
       ?.Load().GetAwaiter().GetResult() ?? new RdmConfig();
      return new SqliteRdmStore(config.DbString);
    });

    collection.AddSingleton<ICaseManager, CaseManager>();
    collection.AddSingleton<ISlayService, SlayService>();

    // Listeners
    collection.AddModBehavior<DeathLogListener>();
    collection.AddModBehavior<SlayQueueListener>();

    // Commands
    collection.AddModBehavior<RdmCommand>();
    collection.AddModBehavior<CasesCommand>();
    collection.AddModBehavior<InfoCommand>();
    collection.AddModBehavior<HandleCommand>();
    collection.AddModBehavior<VerdictCommand>();
  }
}
```

- [ ] **Step 2: Create `TTT/CS2/Configs/CS2RdmConfig.cs`** (mirrors `CS2KarmaConfig`)

```csharp
using CounterStrikeSharp.API.Modules.Cvars;
using CounterStrikeSharp.API.Modules.Cvars.Validators;
using TTT.API;
using TTT.API.Storage;
using TTT.RDM;

namespace TTT.CS2.Configs;

public class CS2RdmConfig : IStorage<RdmConfig>, IPluginModule {
  public static readonly FakeConVar<string> CV_DB_STRING = new(
    "css_ttt_rdm_db_string", "Database connection string for RDM storage",
    "Data Source=rdm.db");

  public static readonly FakeConVar<int> CV_TRAITOR_SLAYS = new(
    "css_ttt_rdm_traitor_slays", "Slays when the RDM victim was a Traitor", 5,
    ConVarFlags.FCVAR_NONE, new RangeValidator<int>(0, 100));

  public static readonly FakeConVar<int> CV_DETECTIVE_SLAYS = new(
    "css_ttt_rdm_detective_slays", "Slays when the RDM victim was a Detective",
    5, ConVarFlags.FCVAR_NONE, new RangeValidator<int>(0, 100));

  public static readonly FakeConVar<int> CV_INNOCENT_SLAYS = new(
    "css_ttt_rdm_innocent_slays", "Slays when the RDM victim was an Innocent",
    3, ConVarFlags.FCVAR_NONE, new RangeValidator<int>(0, 100));

  public static readonly FakeConVar<bool> CV_NOTIFY_ADMINS = new(
    "css_ttt_rdm_notify_admins", "Message online staff when a report is filed",
    true);

  public static readonly FakeConVar<bool> CV_AUTO_PROMPT = new(
    "css_ttt_rdm_auto_prompt", "Prompt victims to report after a suspect kill",
    true);

  public static readonly FakeConVar<int> CV_REPORT_WINDOW = new(
    "css_ttt_rdm_report_window_seconds",
    "Seconds after a death during which a victim may report it", 60,
    ConVarFlags.FCVAR_NONE, new RangeValidator<int>(1, 3600));

  public static readonly FakeConVar<int> CV_MAX_REPORTS = new(
    "css_ttt_rdm_max_reports_per_round",
    "Max reports a single victim may file per round", 3,
    ConVarFlags.FCVAR_NONE, new RangeValidator<int>(1, 100));

  public static readonly FakeConVar<string> CV_STAFF_FLAG = new(
    "css_ttt_rdm_staff_flag", "Admin flag required for RDM staff commands",
    "@ttt/admin");

  public void Dispose() { }
  public void Start() { }

  public Task<RdmConfig?> Load() {
    return Task.FromResult<RdmConfig?>(new RdmConfig {
      DbString = CV_DB_STRING.Value,
      TraitorSlays = CV_TRAITOR_SLAYS.Value,
      DetectiveSlays = CV_DETECTIVE_SLAYS.Value,
      InnocentSlays = CV_INNOCENT_SLAYS.Value,
      NotifyAdmins = CV_NOTIFY_ADMINS.Value,
      AutoPromptOnSuspectKill = CV_AUTO_PROMPT.Value,
      ReportWindowSeconds = CV_REPORT_WINDOW.Value,
      MaxReportsPerVictimPerRound = CV_MAX_REPORTS.Value,
      StaffFlag = CV_STAFF_FLAG.Value
    });
  }
}
```

- [ ] **Step 3: Register the CS2 config** in `TTT/CS2/CS2ServiceCollection.cs`

Add `using TTT.RDM;` if needed and, next to the `CS2KarmaConfig` registration line, add:
```csharp
    collection.AddModBehavior<IStorage<RdmConfig>, CS2RdmConfig>();
```

- [ ] **Step 4: Write the end-to-end test** `TTT/Test/RDM/RdmEndToEndTests.cs`

```csharp
using Microsoft.Extensions.DependencyInjection;
using TTT.API.Command;
using TTT.API.Events;
using TTT.API.Game;
using TTT.API.Player;
using TTT.API.Role;
using TTT.Game.Damage;
using TTT.Game.Events.Player;
using TTT.Game.Roles;
using TTT.RDM;
using TTT.RDM.Commands;
using TTT.Test.Fakes;
using TTT.Test.Game.Command;
using Xunit;

namespace TTT.Test.RDM;

public class RdmEndToEndTests {
  private readonly IServiceProvider provider;
  private readonly IEventBus bus;
  private readonly IGameManager games;
  private readonly IPlayerFinder players;
  private readonly IRoleAssigner roles;
  private readonly ICommandManager commands;
  private readonly IRdmStore store;
  private readonly FakePermissionManager perms;
  private readonly IList<IRole> roleSet;

  public RdmEndToEndTests(IServiceProvider provider) {
    this.provider = provider;
    bus      = provider.GetRequiredService<IEventBus>();
    games    = provider.GetRequiredService<IGameManager>();
    players  = provider.GetRequiredService<IPlayerFinder>();
    roles    = provider.GetRequiredService<IRoleAssigner>();
    commands = provider.GetRequiredService<ICommandManager>();
    store    = provider.GetRequiredService<IRdmStore>();
    perms    = (FakePermissionManager)provider
     .GetRequiredService<IPermissionManager>();
    roleSet  = new List<IRole> {
      new InnocentRole(provider), new TraitorRole(provider),
      new DetectiveRole(provider)
    };
    bus.RegisterListener(provider.GetRequiredService<IDamageTracker>());
    bus.RegisterListener(provider.GetRequiredService<DeathLogListener>());
    commands.RegisterCommand(new RdmCommand(provider));
    commands.RegisterCommand(new HandleCommand(provider));
    commands.RegisterCommand(new VerdictCommand(provider));
  }

  [Fact]
  public async Task SuspectKill_Report_Handle_Guilty_QueuesSlays() {
    var victim   = TestPlayer.Random();
    var offender = TestPlayer.Random();
    var admin    = TestPlayer.Random();
    players.AddPlayers(victim, offender, admin);
    perms.SetFlags(admin, "@ttt/admin");

    var game = games.CreateGame();
    bus.Dispatch(new TTT.Game.Events.Game.GameStateUpdateEvent(game!,
      State.IN_PROGRESS)); // round 1
    game!.Start();
    roles.SetRole(victim, roleSet[0]);   // innocent
    roles.SetRole(offender, roleSet[0]); // innocent -> suspect

    bus.Dispatch(new PlayerDamagedEvent(victim, offender, 100));
    bus.Dispatch(new PlayerDeathEvent(victim).WithKiller(offender)
     .WithWeapon("ak47"));

    // Give the fire-and-forget AddDeath a beat to land.
    await Task.Delay(50, TestContext.Current.CancellationToken);

    // Victim reports the only listed suspect death.
    Assert.Equal(CommandResult.SUCCESS, await commands.ProcessCommand(
      new TestCommandInfo(provider, victim, "rdm", "1")));

    // Staff handles + rules guilty.
    Assert.Equal(CommandResult.SUCCESS, await commands.ProcessCommand(
      new TestCommandInfo(provider, admin, "handle")));
    Assert.Equal(CommandResult.SUCCESS, await commands.ProcessCommand(
      new TestCommandInfo(provider, admin, "verdict", "guilty")));

    Assert.Equal(3, await store.GetSlayDebt(offender.Id));
  }
}
```

- [ ] **Step 5: Build and run the full suite**

Run: `dotnet build TTT/Test/Test.csproj -v q --nologo && ./TTT/Test/bin/Debug/net10.0/Test`
Expected: PASS — `RdmEndToEndTests` passes; summary shows only the single pre-existing `ModuleInitializationTest` flake (if present) and no other failures.

- [ ] **Step 6: Verify the Plugin project still builds (production registration path)**

Run: `dotnet build TTT/Plugin/Plugin.csproj -v q --nologo`
Expected: `Build succeeded` (0 errors).

- [ ] **Step 7: Commit**

```bash
git add TTT/RDM/RdmServiceCollection.cs TTT/CS2/Configs/CS2RdmConfig.cs TTT/CS2/CS2ServiceCollection.cs TTT/Test/RDM/RdmEndToEndTests.cs
git commit -m "feat(rdm): production wiring + CS2 cvar config + e2e test"
```

---

## Post-Implementation

- [ ] **Update the README feature list** (optional): add `- [X] RDM Manager` under Features in `README.md`.
- [ ] Run the full suite once more and confirm the regression gate: **no NEW failing tests** vs the `185 passed / 1 known flake` baseline.
- [ ] Use `superpowers:requesting-code-review` before merging `feat/rdm-manager` back to `dev`.

## Self-Review Notes (spec coverage)

- Victim-driven, opt-in reporting with non-response = not RDM → Tasks 6 (prompt) + 8 (`!rdm`) + 7 (no auto-case).
- Auto-prompt on suspect kills + `!rdm` fallback → Tasks 6 + 8.
- Staff chat commands `!cases`/`!handle`/`!info`/`!verdict` → Tasks 9 + 11.
- Slay queue scaled by victim role + immediate slay + forgive → Task 10 + 11.
- SQLite persistence (deaths, cases, slays) surviving restarts/map changes → Task 5 (+ persistence test) + 12 (production store).
- Shared `IDamageTracker` extracted from `KarmaListener` (option A) → Task 2.
- Staff notification toggle → Task 7 (`NotifyAdmins`).
- Config (`RdmConfig` + CS2 cvars) → Tasks 1 + 12.
- Localization → keys added across Tasks 6–11; `lang/en.json` regenerated on build.
