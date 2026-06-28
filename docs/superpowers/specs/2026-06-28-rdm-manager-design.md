# RDM Manager — Design

**Date:** 2026-06-28
**Status:** Approved (pending implementation plan)
**Source inspiration:** SourceMod/CS:GO TTT `ttt_rdm.sp`
(<https://github.com/TroubleInTerroristTown/Public/blob/aa9699cbc403023a4947e6d60a263cb3f95919b7/addons/sourcemod/scripting/ttt/ttt_rdm.sp>)

## Summary

Add a human-in-the-loop RDM (Random Death Match) management system to the CS2 TTT
port: victims opt-in to report suspect kills, staff review a persistent case queue
via chat commands, and guilty verdicts apply a slay-based punishment queue. The
system sits **alongside** the existing automatic karma penalties (Karma is left
functionally intact); RDM verdicts do **not** adjust karma.

## Goals

- Let the **victim** of a suspect kill decide whether it was RDM. Non-response
  always defaults to "not RDM" (no case is created).
- Give staff a faithful SourceMod-style chat workflow: `!cases`, `!handle`,
  `!info`, `!verdict`.
- Punish guilty offenders with a **slay queue** scaled by victim role, paid down
  over upcoming rounds, plus an immediate slay if the offender is currently alive.
- Persist death logs, cases, and slay debts to SQLite so they survive round
  changes, map changes, and server restarts (offenders cannot dodge slays by
  forcing a map change).

## Non-Goals

- No changes to the automatic karma scoring in `KarmaListener` beyond the shared
  detection extraction (§ Detection).
- RDM verdicts do not modify karma or issue bans (the existing `KarmaBanner`
  continues to handle karma-threshold bans independently).
- SourceMod's "weapon last-fired" evidence timing is dropped as YAGNI. (Easy to
  add later via `DeathRecord` if desired.)

## Decisions (resolved during brainstorming)

| Question | Decision |
|----------|----------|
| Relationship to auto-karma | Parallel staff layer; auto-karma untouched. |
| Verdict effects | Slay queue + immediate slay + forgive/dismiss. **No** karma change. |
| Persistence | SQLite (`rdm.db`), like `karma.db`. |
| Victim report trigger | Auto-prompt on suspect kills **and** `!rdm` fallback. Non-response = not RDM. |
| Staff UX | Chat commands (faithful port). |
| Detection reuse | **Option A**: extract shared `IDamageTracker` from `KarmaListener`. |
| Staff flag | Reuse existing `@ttt/admin` (same flag `!logs` uses); configurable. |

## Architecture

New engine-agnostic module **`TTT/RDM`**, mirroring `TTT/Karma`:

- `RDM.csproj` targets `net10.0`, references `Game.csproj`, and adds
  `Microsoft.Data.Sqlite` (already used by `Plugin.csproj`).
- Registered via `RdmServiceCollection.AddRdmService()`, called from
  `Plugin/TTTServiceCollection.cs` alongside `AddKarmaService()`.
- All modules register through the existing `AddModBehavior` pattern.

Integration points (all existing):

- Events: `PlayerDamagedEvent`, `PlayerDeathEvent`, `GameStateUpdateEvent` via
  `IEventBus` + `[EventHandler]`.
- Slaying: `IOnlinePlayer.Health = 0` (triggers suicide in `CS2Player`) — no
  CS2-specific code needed.
- Staff gating: `IPermissionManager.HasFlags` + `ICommand.RequiredFlags`.
- Player lookup: `IPlayerFinder`. Roles: `IRoleAssigner`. Messaging: `IMessenger`.
- Localization: `RDM/lang/RdmMsgs.cs` + `lang/en.yml`.

## Detection (shared `IDamageTracker`)

The "who damaged whom first / who is at fault" logic currently lives privately in
`KarmaListener.firstDamage`. Extract it into a shared service so Karma and RDM
have one source of truth.

- New `IDamageTracker` (and impl) lives in **`Game`** (both `Karma` and `RDM`
  reference `Game`).
- Responsibilities: maintain the per-round first-damage pairing map (cleared on
  round start) and expose:
  - `void RecordDamage(attackerId, victimId)` — first-damage bookkeeping.
  - `KillFault GetFault(killerId, victimId)` → `KillerGuilty | VictimGuilty | Unknown`.
- `KarmaListener` is refactored to consume `IDamageTracker` instead of its private
  `firstDamage` list. Behavior must remain identical (covered by existing +
  added tests).
- A small classification helper decides `IsSuspect` for a kill: a kill is
  **suspect** when it is a "bad kill" between non-Traitor parties (same logic the
  Karma role-delta switch already encodes — e.g. inno-on-inno, inno-on-detective;
  Traitor-involved legitimate kills are not suspect).

## Data Model (records)

- **`DeathRecord`**: `Id`, `Round`, victim (`Id`/`Name`/`Role`), attacker
  (`Id`/`Name`/`Role`), `Weapon`, `Timestamp`, `IsSuspect`, `Fault`.
- **`RdmCase`**: `Id`, `DeathId`, `ReporterId`, `Reason?`, `State`
  (`Open`/`Claimed`/`Resolved`), `HandlerAdminId?`, `Verdict`
  (`None`/`Forgiven`/`Guilty`), `CreatedAt`.
- **`SlayDebt`**: `PlayerId`, `RemainingSlays`, `SourceCaseId`.

## Persistence (SQLite `rdm.db`)

- `IRdmStore` interface with two implementations:
  - **In-memory** impl — used by unit tests.
  - **SQLite** impl — production, registered in `Plugin` (swappable-backend
    pattern matching `KarmaStorageKV` vs `KarmaStorageAPI`).
- Tables: `deaths`, `cases`, `slays`.
- Slay debts are keyed by steam id, so they persist across disconnect/reconnect,
  map change, and restart.

## Components & Data Flow

1. **`DeathLogListener`** — on `PlayerDeathEvent`, classify via `IDamageTracker`
   and write a `DeathRecord`. Provides "recent suspect deaths for victim X" (for
   `!rdm`) and "death by id" (for `!info`). On a suspect kill (and if
   `AutoPromptOnSuspectKill`), prompt the victim (chat + screen): *"You were
   killed by &lt;attacker&gt;. Type `!rdm` to report, or ignore."*
2. **`RdmCommand` (`!rdm`)** — victim-side. Lists the victim's recent suspect
   deaths (numbered); `!rdm <n> [reason]` files a report within the configurable
   report window → creates an `RdmCase` (Open) → notifies staff. No report ⇒ no
   case. Double-reports and out-of-window reports are rejected with a message.
3. **`ICaseManager` + store** — case lifecycle. Staff commands
   (`RequiredFlags = [StaffFlag]`):
   - `!cases` — open-case count + brief list.
   - `!handle [id]` — claim next open case (or specific id); becomes handler;
     echoes case info + verdict options.
   - `!info <id>` — full details: victim/attacker names, roles, weapon, time,
     reason, and both players' karma as evidence.
   - `!verdict <forgive|guilty>` — applies the verdict to the handler's claimed
     case.
4. **`ISlayService` + `SlayQueueListener`** — punishment:
   - **Guilty** ⇒ owed slays = `RdmConfig` by victim role (traitor 5 / detective 5
     / innocent 3). If offender alive now → immediate slay (`Health = 0`) and
     decrement; remainder stored as `SlayDebt`.
   - On round start (`State.IN_PROGRESS`): for each alive player with debt, slay +
     decrement until paid.
   - **Forgive** ⇒ close case, no punishment.
5. **Notifications** — `NotifyAdmins` (default on): when a report is filed, message
   online staff (admins among `IPlayerFinder.GetOnline()` via
   `IPermissionManager.HasFlags`) with both names + karma.

## Configuration (`RdmConfig` record)

- `DbString` (default `"Data Source=rdm.db"`)
- `TraitorSlays` = 5, `DetectiveSlays` = 5, `InnocentSlays` = 3
- `NotifyAdmins` = true
- `AutoPromptOnSuspectKill` = true
- `ReportWindowSeconds` (e.g. 60)
- `MaxReportsPerVictimPerRound`
- `StaffFlag` = `"@ttt/admin"`

## Error Handling & Edge Cases

- **Offender disconnected**: slay debt persists in SQLite; applied on reconnect at
  the next round start while alive.
- **Victim disconnects before reporting**: no case (default not RDM).
- **Report after window / duplicate report**: rejected with a localized message.
- **Self-kills / world kills**: not suspect; no prompt, no case.
- **Map change / restart**: SQLite persists cases + slay debts.
- **`!handle`/`!verdict` with no claimed case or bad id**: localized error.

## Testing (`TTT/Test/RDM`)

- Slay-debt payoff across multiple rounds (incl. immediate slay when alive).
- Case lifecycle: report → handle → verdict (guilty and forgive).
- Suspect classification correctness (Traitor-involved kills not suspect).
- "No report ⇒ no case" (default not RDM).
- Report-window expiry and duplicate-report rejection.
- Disconnect/reconnect slay persistence.
- `KarmaListener` parity after `IDamageTracker` extraction (no behavior change).
- Reuses existing fakes (`FakePermissionManager`, etc.).

## Module file sketch

```
TTT/RDM/
  RDM.csproj
  RdmServiceCollection.cs
  RdmConfig.cs
  IRdmStore.cs                 # + InMemoryRdmStore, SqliteRdmStore
  ICaseManager.cs / CaseManager.cs
  ISlayService.cs / SlayService.cs
  DeathLogListener.cs
  SlayQueueListener.cs
  RdmCommand.cs                # !rdm (victim)
  CasesCommand.cs              # !cases
  HandleCommand.cs             # !handle
  InfoCommand.cs               # !info
  VerdictCommand.cs            # !verdict
  Models/ (DeathRecord, RdmCase, SlayDebt, enums)
  lang/RdmMsgs.cs
  lang/en.yml

TTT/Game/  (shared detection)
  IDamageTracker.cs / DamageTracker.cs   # extracted from KarmaListener
```
