using Newtonsoft.Json;
using UnityEngine;

public static class DebugHelper
{
    public static void LogCharacterPassives(string characterId)
    {
        var list = CharacterStore.GetPassives(characterId);
        Debug.Log($"Passives for '{characterId}':\n" + JsonConvert.SerializeObject(list, Formatting.Indented));
    }
}
