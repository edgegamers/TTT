using TTT.Locale;

namespace TTT.RDM.lang;

public static class RdmMsgs {
  public static IMsg RDM_PROMPT(string attacker) {
    return MsgFactory.Create(nameof(RDM_PROMPT), attacker);
  }

  public static IMsg RDM_STAFF_NEW_REPORT(string victim, string attacker,
    int caseId) {
    return MsgFactory.Create(nameof(RDM_STAFF_NEW_REPORT), victim, attacker,
      caseId);
  }

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
}
