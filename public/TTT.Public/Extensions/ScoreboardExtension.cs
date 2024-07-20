﻿using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using TTT.Player;
using TTT.Public.Extensions;

namespace TTT.Round;

public static class ScoreboardExtension {
  public static void ModifyScoreBoard(this CCSPlayerController? player) {
    if (player == null) return;
    if (!player.IsReal()) return;
    var actionService = player.ActionTrackingServices;
    if (actionService == null) return;


    RemoveKills(player);
    removeDamage(player);
    removeDeaths(player);
    removeUtilityDamage(player);
    removeAssists(player);
    removeEnemiesFlashed(player);
    removeHeadshotKills(player);
    killIconTest(player);
  }

  public static void ModifyKarma(this GamePlayer gamePlayer) {
    var player = gamePlayer.Player();
    if (player == null) return;
    setScore(gamePlayer);
  }

  private static void setScore(GamePlayer gamePlayer) {
    var player = gamePlayer.Player();
    if (player == null) return;

    if (player.Score == gamePlayer.Karma()) return;
    player.Score = gamePlayer.Karma();
  }

  private static void killIconTest(CCSPlayerController player) {
    var matchStats = player.ActionTrackingServices;
    if (matchStats.NumRoundKills == 0 && matchStats.NumRoundKillsHeadshots == 0)
      return;
    matchStats.NumRoundKills          = 0;
    matchStats.NumRoundKillsHeadshots = 0;
    Utilities.SetStateChanged(player,
      "CCSPlayerController_ActionTrackingServices", "m_iNumRoundKills");
    Utilities.SetStateChanged(player,
      "CCSPlayerController_ActionTrackingServices",
      "m_iNumRoundKillsHeadshots");
  }

  private static void RemoveKills(CCSPlayerController player) {
    var matchStats = player.ActionTrackingServices!.MatchStats;
    if (matchStats.Kills == 0) return;
    matchStats.Kills = 0;
    Utilities.SetStateChanged(player, "CSPerRoundStats_t", "m_iKills");
  }

  private static void removeDamage(CCSPlayerController player) {
    var matchStats = player.ActionTrackingServices!.MatchStats;
    if (matchStats.Damage == 0) return;
    matchStats.Damage = 0;
    Utilities.SetStateChanged(player, "CSPerRoundStats_t", "m_iDamage");
  }

  private static void removeUtilityDamage(CCSPlayerController player) {
    var matchStats = player.ActionTrackingServices!.MatchStats;
    if (matchStats.UtilityDamage == 0) return;
    matchStats.UtilityDamage = 0;
    Utilities.SetStateChanged(player, "CSPerRoundStats_t", "m_iUtilityDamage");
  }

  private static void removeDeaths(CCSPlayerController player) {
    var matchStats = player.ActionTrackingServices!.MatchStats;
    if (matchStats.Deaths == 0) return;
    matchStats.Deaths = 0;
    Utilities.SetStateChanged(player, "CSPerRoundStats_t", "m_iDeaths");
  }

  private static void removeAssists(CCSPlayerController player) {
    var matchStats = player.ActionTrackingServices!.MatchStats;
    if (matchStats.Assists == 0) return;
    matchStats.Assists = 0;
    Utilities.SetStateChanged(player, "CSPerRoundStats_t", "m_iAssists");
  }

  private static void removeEnemiesFlashed(CCSPlayerController player) {
    var matchStats = player.ActionTrackingServices!.MatchStats;
    if (matchStats.EnemiesFlashed == 0) return;
    matchStats.EnemiesFlashed = 0;
    Utilities.SetStateChanged(player, "CSPerRoundStats_t", "m_iEnemiesFlashed");
  }

  private static void removeHeadshotKills(CCSPlayerController player) {
    var matchStats = player.ActionTrackingServices!.MatchStats;
    if (matchStats.HeadShotKills == 0) return;
    matchStats.HeadShotKills = 0;
    Utilities.SetStateChanged(player, "CSPerRoundStats_t", "m_iHeadShotKills");
  }
}