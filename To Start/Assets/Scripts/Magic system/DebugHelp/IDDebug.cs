using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class IDDebug : MonoBehaviour
{
    [SerializeField] private string characterId;
    [Tooltip("If true, wait 1s before printing (useful if passives are added after Start).")]
    public bool delayBeforeLog = true;

    IEnumerator Start()
    {
        if (delayBeforeLog)
            yield return new WaitForSeconds(1f); // small delay to allow other startup code to run

        LogAllCharacterIdsAndPassives();

        // keep running so you can press R to refresh during play
        while (true)
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                Debug.Log("[IDDebug] Manual refresh (R pressed).");
                LogAllCharacterIdsAndPassives();
            }
            yield return null;
        }
    }

    private void LogAllCharacterIdsAndPassives()
    {
        var ids = CharacterStore.GetAllCharacterIds();
        if (ids == null || ids.Count == 0)
        {
            Debug.Log("[IDDebug] CharacterStore contains NO character ids (passivesMap empty).");
        }
        else
        {
            Debug.Log($"[IDDebug] CharacterStore contains {ids.Count} character id(s): {string.Join(", ", ids)}");
            foreach (var id in ids)
            {
                Debug.Log($"--- Passives for '{id}' ---\n{CharacterStore.GetSummary(id)}");
            }
        }

        // also log the inspector-specified id for convenience
        if (!string.IsNullOrEmpty(characterId))
        {
            Debug.Log($"[IDDebug] Inspector characterId '{characterId}' summary:\n{CharacterStore.GetSummary(characterId)}");
        }
    }
}
