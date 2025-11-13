using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;


[Serializable]
public class PassiveDefinition
{
	public string id;
	public string name;
	public string description;
	public string element;
	public string trigger;
	public string target; // self|ally|enemy|area
	public List<EffectInstance> effects;
	public Scaling scaling;
	public Balance balance;
	public string notes;
}

[Serializable]
public class EffectInstance
{
	// Keep as string (exact names enforced elsewhere) to avoid enum parsing issues
	public string effectType;
	public EffectParameters parameters;
}

// Place this in your runtime assembly (not Editor folder)

[Serializable]
public class EffectParameters
{
	// ---------- Inspector-visible backing fields (Unity serializes these) ----------
	[SerializeField, Tooltip("percent or flat")]
	private string valueTypeInspector = "percent";

	[SerializeField, Tooltip("Value (percent or flat)")]
	private float valueInspector = 25f;

	[SerializeField, Tooltip("Duration in seconds. Use -1 to indicate 'none' (will be serialized as null).")]
	private float durationInspector = -1f;

	[SerializeField, Tooltip("Cooldown in seconds. Use -1 to indicate 'none' (will be serialized as null).")]
	private float cooldownInspector = -1f;

	[SerializeField, Range(0f, 1f), Tooltip("Chance 0..1")]
	private float chanceInspector = 1f;

	[SerializeField, Tooltip("Radius in meters. Use -1 to indicate 'none'.")]
	private float radiusInspector = -1f;

	[SerializeField, Tooltip("Pierce percent. Use -1 to indicate 'none'.")]
	private float piercePercentInspector = -1f;

	[SerializeField, Tooltip("stat (strength|intelligence|agility|level|none)")]
	private string statInspector = "none";

	[SerializeField, Tooltip("stackType (none|linear|exponential)")]
	private string stackTypeInspector = "none";

	[SerializeField, Tooltip("max stacks (if stackType != none)")]
	private int maxStacksInspector = 0;

	// ---------- JSON properties (used by Newtonsoft / your runtime code) ----------
	// Newtonsoft will serialize these public properties. Your existing generator code
	// that uses double/double? will continue to work with these.

	[JsonProperty("valueType")]
	public string valueType
	{
		get => valueTypeInspector;
		set => valueTypeInspector = value ?? "percent";
	}

	[JsonProperty("value")]
	public double value
	{
		get => valueInspector;
		set => valueInspector = (float)value;
	}

	// Duration serialized as null if durationInspector < 0
	[JsonProperty("duration")]
	public double? duration
	{
		get => durationInspector >= 0f ? (double?)durationInspector : null;
		set => durationInspector = value.HasValue ? (float)value.Value : -1f;
	}

	[JsonProperty("cooldown")]
	public double? cooldown
	{
		get => cooldownInspector >= 0f ? (double?)cooldownInspector : null;
		set => cooldownInspector = value.HasValue ? (float)value.Value : -1f;
	}

	[JsonProperty("chance")]
	public double chance
	{
		get => chanceInspector;
		set => chanceInspector = (float)value;
	}

	[JsonProperty("radius")]
	public double? radius
	{
		get => radiusInspector >= 0f ? (double?)radiusInspector : null;
		set => radiusInspector = value.HasValue ? (float)value.Value : -1f;
	}

	[JsonProperty("piercePercent")]
	public double? piercePercent
	{
		get => piercePercentInspector >= 0f ? (double?)piercePercentInspector : null;
		set => piercePercentInspector = value.HasValue ? (float)value.Value : -1f;
	}

	[JsonProperty("stat")]
	public string stat
	{
		get => string.IsNullOrEmpty(statInspector) ? "none" : statInspector;
		set => statInspector = string.IsNullOrEmpty(value) ? "none" : value;
	}

	[JsonProperty("stackType")]
	public string stackType
	{
		get => stackTypeInspector;
		set => stackTypeInspector = string.IsNullOrEmpty(value) ? "none" : value;
	}

	[JsonProperty("maxStacks")]
	public int maxStacks
	{
		get => maxStacksInspector;
		set => maxStacksInspector = value;
	}
}



[Serializable]
public class Scaling { public string stat; public double ratio; }
[Serializable]
public class Balance { public double min_value; public double max_value; public string notes; }
