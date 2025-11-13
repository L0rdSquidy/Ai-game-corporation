using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;

public static class CharacterStore
{
    // In-memory map of characterId -> list of passives
    private static Dictionary<string, List<PassiveDefinition>> passivesMap = new Dictionary<string, List<PassiveDefinition>>();

    // Lazy-load guard
    private static bool _loadedFromDisk = false;

    // File used to persist all character passives
    private static string SaveFilePath => Path.Combine(Application.persistentDataPath, "character_passives.json");

    // ----------------------
    // Public element access (unchanged)
    // ----------------------
    public static string GetElementForCharacter(string characterId)
    {
        return PlayerPrefs.GetString($"char_{characterId}_element", null);
    }

    public static void SetElementForCharacter(string characterId, string element)
    {
        PlayerPrefs.SetString($"char_{characterId}_element", element);
        PlayerPrefs.Save();
    }

    // ----------------------
    // Public passives API (Add / Get)
    // ----------------------
    public static void AddPassivesToCharacter(string characterId, List<PassiveDefinition> passives)
    {
        EnsureLoaded();

        if (passives == null || passives.Count == 0) return;

        if (!passivesMap.ContainsKey(characterId)) passivesMap[characterId] = new List<PassiveDefinition>();
        passivesMap[characterId].AddRange(passives);

        Debug.Log($"[CharacterStore] Added {passives.Count} passive(s) to '{characterId}'. Now has {passivesMap[characterId].Count} passive(s).");

        // persist immediately
        SaveToDisk();
    }

    public static List<PassiveDefinition> GetPassives(string characterId)
    {
        EnsureLoaded();

        if (passivesMap.TryGetValue(characterId, out var list))
        {
            return list;
        }
        return new List<PassiveDefinition>();
    }

    // Replace the stored passives for the character with the provided list (deep-copied)
    public static void SetPassivesForCharacter(string characterId, List<PassiveDefinition> passives)
    {
        EnsureLoaded();

        if (passives == null) passives = new List<PassiveDefinition>();
        // deep clone to avoid external references being mutated
        try
        {
            var json = JsonConvert.SerializeObject(passives);
            var clone = JsonConvert.DeserializeObject<List<PassiveDefinition>>(json) ?? new List<PassiveDefinition>();
            passivesMap[characterId] = clone;
        }
        catch
        {
            passivesMap[characterId] = new List<PassiveDefinition>(passives);
        }

        Debug.Log($"[CharacterStore] Set passives for '{characterId}' (count {passivesMap[characterId].Count}).");
        SaveToDisk();
    }

    // ----------------------
    // Utilities / Debug helpers
    // ----------------------
    public static List<string> GetAllCharacterIds()
    {
        EnsureLoaded();
        return new List<string>(passivesMap.Keys);
    }

    public static string GetSummary(string characterId)
    {
        var list = GetPassives(characterId);
        return JsonConvert.SerializeObject(list, Formatting.Indented);
    }

    public static List<PassiveDefinition> GetPassivesDeepCopy(string characterId)
    {
        var original = GetPassives(characterId) ?? new List<PassiveDefinition>();
        try
        {
            var json = JsonConvert.SerializeObject(original);
            var clone = JsonConvert.DeserializeObject<List<PassiveDefinition>>(json);
            return clone ?? new List<PassiveDefinition>();
        }
        catch
        {
            return new List<PassiveDefinition>(original);
        }
    }

    // Clear all passives (useful for tests). Be careful: this persists immediately.
    public static void ClearAllPassives()
    {
        passivesMap.Clear();
        SaveToDisk();
    }

    // ----------------------
    // Persistence (disk) — private helpers
    // ----------------------
    private static void EnsureLoaded()
    {
        if (_loadedFromDisk) return;
        LoadFromDisk();
    }

    private static void LoadFromDisk()
    {
        _loadedFromDisk = true;
        try
        {
            if (!File.Exists(SaveFilePath))
            {
                Debug.Log($"[CharacterStore] No passives save file found at {SaveFilePath}. Starting fresh.");
                passivesMap = new Dictionary<string, List<PassiveDefinition>>();
                return;
            }

            var json = File.ReadAllText(SaveFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                passivesMap = new Dictionary<string, List<PassiveDefinition>>();
                return;
            }

            // try deserialize to dictionary
            var dict = JsonConvert.DeserializeObject<Dictionary<string, List<PassiveDefinition>>>(json);
            if (dict != null)
            {
                passivesMap = dict;
                Debug.Log($"[CharacterStore] Loaded passives for {passivesMap.Count} character(s) from {SaveFilePath}.");
            }
            else
            {
                passivesMap = new Dictionary<string, List<PassiveDefinition>>();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CharacterStore] Failed to load passives from disk: {ex.Message}\nPath: {SaveFilePath}");
            passivesMap = new Dictionary<string, List<PassiveDefinition>>();
        }
    }

    private static void SaveToDisk()
    {
        try
        {
            // ensure directory exists
            var dir = Path.GetDirectoryName(SaveFilePath);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            // serialize
            var json = JsonConvert.SerializeObject(passivesMap, Formatting.Indented);

            // write atomically: write to temp file then replace
            var tmp = SaveFilePath + ".tmp";
            File.WriteAllText(tmp, json);
            // Replace or move - prefer overwrite
            if (File.Exists(SaveFilePath)) File.Delete(SaveFilePath);
            File.Move(tmp, SaveFilePath);

            Debug.Log($"[CharacterStore] Saved passives for {passivesMap.Count} character(s) to {SaveFilePath}.");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[CharacterStore] Failed to save passives to disk: {ex.Message}\nPath: {SaveFilePath}");
        }
    }
}
