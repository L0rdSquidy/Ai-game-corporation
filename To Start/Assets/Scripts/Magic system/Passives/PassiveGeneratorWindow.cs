#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Threading.Tasks;

public class PassiveGeneratorWindow : EditorWindow
{
	string characterId = "char_01";
	string personality = "A brave hot-headed brawler who loves chaos.";
	int count = 2;
	Vector2 scroll;

	[MenuItem("AI Tools/Passive Generator")]
	static void Open() => GetWindow<PassiveGeneratorWindow>().Show();

	void OnGUI()
	{
		GUILayout.Label("Passive Generator", EditorStyles.boldLabel);
		characterId = EditorGUILayout.TextField("Character ID", characterId);
		count = EditorGUILayout.IntField("Number to generate", count);
		GUILayout.Label("Personality (description):");
		scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(120));
		personality = EditorGUILayout.TextArea(personality, GUILayout.ExpandHeight(true));
		EditorGUILayout.EndScrollView();

		if (GUILayout.Button("Generate Passives"))
		{
			_ = Generate(); // fire-and-forget in editor; results are printed to console
		}
	}

	async Task Generate()
	{
		var list = await PassiveGenerator.GenerateWithRetries(characterId, personality, count, 10);
		Debug.Log($"Generated {list.Count} passives for {characterId}");
		// Optionally create ScriptableObject assets
		foreach (var p in list)
		{
			var asset = ScriptableObject.CreateInstance<PassiveAsset>();
			asset.passive = p;
			string safeName = p.id.Replace(" ", "_");
			AssetDatabase.CreateAsset(asset, $"Assets/GeneratedPassives/{safeName}.asset");
		}
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
	}
}
#endif
