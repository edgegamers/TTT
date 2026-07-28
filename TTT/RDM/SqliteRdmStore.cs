using Microsoft.Data.Sqlite;
using TTT.Game.Damage;
using TTT.RDM.Models;

namespace TTT.RDM;

public sealed class SqliteRdmStore : IRdmStore, IDisposable {
  private readonly SqliteConnection connection;
  private readonly SemaphoreSlim gate = new(1, 1);

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
    await gate.WaitAsync();
    try {
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
    } finally {
      gate.Release();
    }
  }

  public async Task<DeathRecord?> GetDeath(int id) {
    await gate.WaitAsync();
    try {
      await using var cmd = connection.CreateCommand();
      cmd.CommandText = $"SELECT {DeathCols} FROM deaths WHERE id = $id";
      cmd.Parameters.AddWithValue("$id", id);
      await using var r = await cmd.ExecuteReaderAsync();
      return await r.ReadAsync() ? ReadDeath(r) : null;
    } finally {
      gate.Release();
    }
  }

  public async Task<IReadOnlyList<DeathRecord>> GetSuspectDeathsForVictim(
    string victimId, int round) {
    await gate.WaitAsync();
    try {
      await using var cmd = connection.CreateCommand();
      cmd.CommandText =
        $"SELECT {DeathCols} FROM deaths WHERE victim_id = $v AND round = $r AND is_suspect = 1 ORDER BY id";
      cmd.Parameters.AddWithValue("$v", victimId);
      cmd.Parameters.AddWithValue("$r", round);
      var list = new List<DeathRecord>();
      await using var r = await cmd.ExecuteReaderAsync();
      while (await r.ReadAsync()) list.Add(ReadDeath(r));
      return list;
    } finally {
      gate.Release();
    }
  }

  public async Task<int> AddCase(RdmCase c) {
    await gate.WaitAsync();
    try {
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
    } finally {
      gate.Release();
    }
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
    await gate.WaitAsync();
    try {
      await using var cmd = connection.CreateCommand();
      cmd.CommandText = $"SELECT {CaseCols} FROM cases WHERE id = $id";
      cmd.Parameters.AddWithValue("$id", id);
      await using var r = await cmd.ExecuteReaderAsync();
      return await r.ReadAsync() ? ReadCase(r) : null;
    } finally {
      gate.Release();
    }
  }

  public async Task<IReadOnlyList<RdmCase>> GetOpenCases() {
    await gate.WaitAsync();
    try {
      await using var cmd = connection.CreateCommand();
      cmd.CommandText =
        $"SELECT {CaseCols} FROM cases WHERE state != $resolved ORDER BY id";
      cmd.Parameters.AddWithValue("$resolved", (int)CaseState.Resolved);
      var list = new List<RdmCase>();
      await using var r = await cmd.ExecuteReaderAsync();
      while (await r.ReadAsync()) list.Add(ReadCase(r));
      return list;
    } finally {
      gate.Release();
    }
  }

  public async Task UpdateCase(RdmCase c) {
    await gate.WaitAsync();
    try {
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
    } finally {
      gate.Release();
    }
  }

  public async Task<bool> HasReport(string reporterId, int deathId) {
    await gate.WaitAsync();
    try {
      await using var cmd = connection.CreateCommand();
      cmd.CommandText =
        "SELECT COUNT(*) FROM cases WHERE reporter_id = $r AND death_id = $d";
      cmd.Parameters.AddWithValue("$r", reporterId);
      cmd.Parameters.AddWithValue("$d", deathId);
      return Convert.ToInt32(await cmd.ExecuteScalarAsync()) > 0;
    } finally {
      gate.Release();
    }
  }

  public async Task<int> CountReportsByVictim(string reporterId, int round) {
    await gate.WaitAsync();
    try {
      await using var cmd = connection.CreateCommand();
      cmd.CommandText =
        """
        SELECT COUNT(*) FROM cases c JOIN deaths d ON c.death_id = d.id
        WHERE c.reporter_id = $r AND d.round = $round
        """;
      cmd.Parameters.AddWithValue("$r", reporterId);
      cmd.Parameters.AddWithValue("$round", round);
      return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    } finally {
      gate.Release();
    }
  }

  public async Task SetSlayDebt(string playerId, int remaining,
    int sourceCaseId) {
    await gate.WaitAsync();
    try {
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
    } finally {
      gate.Release();
    }
  }

  public async Task<int> GetSlayDebt(string playerId) {
    await gate.WaitAsync();
    try {
      await using var cmd = connection.CreateCommand();
      cmd.CommandText = "SELECT remaining FROM slays WHERE player_id = $p";
      cmd.Parameters.AddWithValue("$p", playerId);
      var result = await cmd.ExecuteScalarAsync();
      return result == null ? 0 : Convert.ToInt32(result);
    } finally {
      gate.Release();
    }
  }

  public async Task<IReadOnlyList<SlayDebt>> GetAllSlayDebts() {
    await gate.WaitAsync();
    try {
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
    } finally {
      gate.Release();
    }
  }

  public void Dispose() {
    connection.Dispose();
    gate.Dispose();
  }
}
