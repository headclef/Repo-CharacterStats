using System.Collections;
using HarmonyLib;
using UnityEngine;

namespace Character_Stats.Patches;

[HarmonyPatch]
internal static class StatReaderPatch
{
    // ── Hook: GameDirector.Start — read stats 1 second after level loads ──

    [HarmonyPatch(typeof(GameDirector), nameof(GameDirector.Start))]
    [HarmonyPostfix]
    private static void GameDirector_Start_Postfix()
    {
        Object.FindObjectOfType<MonoBehaviour>().StartCoroutine(ReadStatsDelayed());
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

        Character_Stats.RefreshStats();
    }

    // ── Hook: After sync data, re-read stats ──

    [HarmonyPatch(typeof(PunManager), "ReceiveSyncData")]
    [HarmonyPostfix]
    private static void ReceiveSyncData_Postfix(bool finalChunk)
    {
        if (finalChunk)
        {
            // Wait a few frames for other mods to process, then re-read
            Object.FindObjectOfType<MonoBehaviour>().StartCoroutine(ReadStatsAfterSync());
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
