using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace Character_Stats.Patches;

[HarmonyPatch]
internal static class StatReaderPatch
{
    // ── Hook: GameDirector.Start — read stats 1 second after level loads ──

    private static Coroutine? _refreshLoop;

    [HarmonyPatch(typeof(GameDirector), nameof(GameDirector.Start))]
    [HarmonyPostfix]
    private static void GameDirector_Start_Postfix()
    {
        if (_refreshLoop != null)
            Character_Stats.Instance.StopCoroutine(_refreshLoop);
        _refreshLoop = Character_Stats.Instance.StartCoroutine(ReadStatsDelayed());
    }

    private static IEnumerator ReadStatsDelayed()
    {
        // Wait for level generation
        while (!SemiFunc.LevelGenDone())
        {
            yield return new WaitForSeconds(0.5f);
        }

        if (!SemiFunc.RunIsLevel())
            yield break;

        // Wait 1 second after level starts to let all mods settle
        yield return new WaitForSeconds(1f);

        // Refresh periodically. Other mods (e.g. Improve) apply AND keep enforcing
        // stats via their own watchdogs AFTER level gen, so a single read can latch a
        // stale value (e.g. a transient 0 during the load hand-off) and never recover.
        // Re-reading keeps the cache in sync with the live StatsManager values for
        // every consumer (Agility, Armor, Constitution, Increase Tumble Damage, UI).
        while (SemiFunc.RunIsLevel())
        {
            Character_Stats.RefreshStats();
            yield return new WaitForSeconds(1f);
        }

        _refreshLoop = null;
    }

    // ── Hook: After sync data, re-read stats ──

    [HarmonyPatch(typeof(PunManager), "ReceiveSyncData")]
    [HarmonyPostfix]
    private static void ReceiveSyncData_Postfix(bool finalChunk)
    {
        if (finalChunk)
        {
            // Wait a few frames for other mods to process, then re-read
            Character_Stats.Instance.StartCoroutine(ReadStatsAfterSync());
        }
    }

    private static IEnumerator ReadStatsAfterSync()
    {
        // Wait for other mods' ReceiveSyncData postfixes to finish
        yield return null;
        yield return null;
        yield return null;

        // Then wait 1 second for upgrade mods to apply
        yield return new WaitForSeconds(1f);

        Character_Stats.RefreshStats();
    }

    // ── Hook: Clear stats on progress reset ──

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.ResetProgress))]
    [HarmonyPostfix]
    private static void RunManager_ResetProgress_Postfix()
    {
        Character_Stats.ClearStats();
    }
}
