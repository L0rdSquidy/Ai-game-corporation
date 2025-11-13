
using System;
using System.Collections.Generic;
using System.Linq;

public static class ElementPicker
{
	private static readonly Dictionary<string, int> weights = new()
	{
		{"earth", 12},
		{"wind", 12},
		{"water", 12},
		{"ice", 10},
		{"fire", 12},
		{"plant", 10},
		{"lightning", 12},
		{"light", 6},
		{"darkness", 6},
		{"No-Attribute", 6}
	};

	public static string PickRandomElement(System.Random rng = null)
	{
		rng ??= new System.Random();
		int total = weights.Values.Sum();
		int r = rng.Next(total);
		int cumulative = 0;
		foreach (var kv in weights)
		{
			cumulative += kv.Value;
			if (r < cumulative) return kv.Key;
		}
		return weights.Keys.First();
	}
}
