using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public static class PassiveGenerator
{
  
	private static readonly bool DisableDescriptionConversion = true;
	private const string systemPrompt = @"
	You are a STRICT JSON generator. For every request output EXACTLY one JSON array (an array of Passive objects) and NOTHING else — absolutely no commentary, no headings, no markdown, no code fences, no stray characters, no emojis, no extra text, and no partial JSON. Use only ASCII double quote character for strings. Output must be compact (single line, no unnecessary whitespace or line breaks) so it is easy to parse. If you cannot produce valid JSON for the requested schema, output EXACTLY the string:
	ERROR_JSON_INVALID
	NO MARKDOWNS!!
	WRITE EVERYTHING IN ENGLISH!

	Passive schema fields (every passive MUST include these): id (string), name (string), description (string), element (string), trigger (string), target (string), effects (array of exactly 2 EffectInstance), stacks, scaling, balance, notes (nullable).
	EffectInstance parameters must always be present (use null for not-applicable numeric fields). Percent values are numbers (20.0..60.0), chance is a decimal between 0.7 and 1.0. Use the exact effectType names from the allowed list.

	Return only the JSON array or the exact string ERROR_JSON_INVALID — nothing else.

	Passive schema fields: id(string), name(string), description(string), element(string - one of earth|wind|water|ice|fire|lightning|light|darkness|No-Attribute), trigger(string - one of on_hit|on_damage|on_Skilluse|on_block|on_critical_hit|on_heal), target(string - one of self|ally|enemy|area), effects (array of exactly 2 EffectInstance objects), stacks { type: 'none'|'linear'|'exponential', max:int }, scaling { stat: 'strength'|'intelligence'|'agility'|'level'|'none', ratio:number (0.1-0.5) }, balance { min_value:number, max_value:number, notes:string|null }, notes:string|null.
	Use a diverse set of effects. For each generated passive (which must contain exactly 2 different effects), avoid always using the same 4 types (healing, damage reduction, stat boost, crit chance). Across many generations, the distribution should include other effect types from the allowed list. For this single passive: prefer at least one less-common effect (for example: Pierce, SplashOnDeath, ManaRegen, ShieldOnHit, Stun, Disarm, ResourceOnKill, ReflectDamage, RangeIncrease). Do NOT choose only healing/damage-reduction/stat-boost/crit chance unless the character's description explicitly demands it.


	EffectInstance shape:
	{ 
	""effectType"": ""<one of the approved EffectType names>"",
	""parameters"": {
		""valueType"": ""percent""|""flat"",
		""value"": number (if percent: between 20 and 60),
		""duration"": number(between 0 and 6),
		""cooldown"": number(between 1 and 300, it must never be 0),
		""chance"": number (0.7..1.0), 
		""radius"": number|null,
		""piercePercent"": number|null,
		""stat"": ""strength|intelligence|agility|level|none""|null,
		""stackType"": ""none""|""linear""|""exponential"",
		""maxStacks"": integer (if stackType != 'none': between 1 and 3)
	}
	}

	ALLOWED effectType names (exact strings): PhysicalDamage, ElementalDamage, DamageOverTime, CriticalChance, CriticalDamage, Pierce, SplashDamage, ReflectDamage, TrueDamage, SplashOnDeath (splash effect when you kill an enemy),
	DamageReduction, ArmorBoost, ResistanceBoost, ShieldOnHit, BlockChance, DodgeChance, HealthRegen, LifeLeech, MaxHealth, Revive, DamageCap,
	InstantHeal, HealOverTime, ManaRegen, ResourceOnKill, CooldownRefund, StatusCure,
	Slow, Stun, Root, Silence, Fear, Disarm, Knockback, Taunt,
	AttackSpeed, MovementSpeed, RangeIncrease, Accuracy, CriticalResist, StatBoost
	Do use effectTypes that are stated here and dont always use the same basic effects

	Requirements:
	- Must pick at least one effect from the set of less-common effects (Pierce, ElementalDamage, LifeLeech, SplashOnDeath, Revive, DamageCap, ManaRegen, ShieldOnHit, Slow, Stun, Root, Silence, Fear, Knockback, Taunt, Disarm, ResourceOnKill, ReflectDamage, RangeIncrease, Taunt, Knockback, Resistances) unless character explicitly requests otherwise.
	- Avoid repeating the same pair of effectTypes across multiple requests for the same character.
	- Every passive must include exactly 2 or more -- never 1 or 0 -- effect instances and the two or more effectTypes must be different.
	- Description must be filled out and should mention or clearly correspond to the two(or more) effect types, if you talk about a effectType in the desciption that effectType should be used in your description.
	- Scaling ratio must be between 0.1 and 0.5.
	- Make sure the description matches the effects provided. If the description talks about a heal over time, the effects must include HealOverTime with matching parameters. include both effects in the passive effects list. 
	- suit the description to the effects and the personality.
	- If stackType != 'none', maxStacks must be 1..3.
	- value (percent) must be 20..100.
	- do not forget to properly add enters in proper places.
	- the format you send must be in the exact format of the examples.
	- value can never ever be 0.
	- chance must be 0.7..1.0.
	- duration must be filled from 1..5.
	- cooldown must be filled from 1..6.
	- If a parameter is not applicable, set it to null.
	- do not create / on the effects it will give errors.
	- remove any and all / that you put in the Passives.
	- CHANCE MUST NEVER BE 0.
	- cooldown must always be above 0.
	- Do not forget the duration of the effects, if the effect should not have a duration put in null, if it should have a duration the ratio of duration between 1..6
	Return only the JSON (a single object or an array).";


	
	private const int DefaultMaxAttempts = 11;
	private const int RetryDelayMs = 500;

	
	private static double inversionChance = 0.20; 
	public static void SetInversionChance(double v) => inversionChance = Math.Max(0.0, Math.Min(1.0, v));
	public static double GetInversionChance() => inversionChance;

	
	private static readonly Dictionary<string, string> OppositeElementMap = new()
	{
		{"fire", "water"},
		{"water", "fire"}, //lightning = yellow, fire = orange, darkness = dark purple with pure black, earth brown, water = deep blue, light = white with gold acsent, no atribute = pure white, ice = icy blue, wind = gray with white. 
		{"earth", "wind"},
		{"wind", "earth"},
		{"ice", "lightning"},
		{"lightning", "ice"},
		{"light", "darkness"},
		{"darkness", "light"},
		{"No-Attribute", "No-Attribute"}
	};

	// Description conversion removed: keep assistant-provided descriptions intact.

	private static readonly HashSet<string> AllowedTriggers = new(StringComparer.OrdinalIgnoreCase)
	{
		"on_hit", "on_damage", "on_Skilluse", "on_block", "on_critical_hit", "on_heal"
	};

  
	private static readonly Dictionary<string, string> TriggerSynonyms = new(StringComparer.OrdinalIgnoreCase)
	{
		{"on_damage_taken", "on_hit"},
		{"on_damaged", "on_hit"},
		{"on_skill_use", "on_Skilluse"},
		{"on_skill", "on_Skilluse"},
		{"on_ability_use", "on_Skilluse"},
		{"on_blocked", "on_block"},
		{"on_parry", "on_block"},
		{"on_crit", "on_critical_hit"},
		{"on_critical", "on_critical_hit"},
		{"on_healed", "on_heal"},
		{"always_on", "on_damage"}
	};

   
	private static readonly HashSet<string> AllowedEffectNames = new(StringComparer.OrdinalIgnoreCase)
	{
		"PhysicalDamage","ElementalDamage","DamageOverTime","CriticalChance","CriticalDamage","Pierce","SplashDamage","ReflectDamage","TrueDamage","SplashOnDeath",
		"DamageReduction","ArmorBoost","ResistanceBoost","ShieldOnHit","BlockChance","DodgeChance","HealthRegen","LifeLeech","MaxHealth","Revive","DamageCap",
		"InstantHeal","HealOverTime","ManaRegen","ResourceOnKill","CooldownRefund","StatusCure",
		"Slow","Stun","Root","Silence","Fear","Disarm","Knockback","Taunt",
		"AttackSpeed","MovementSpeed","RangeIncrease","Accuracy","CriticalResist","StatBoost"
	};

   
	public static async Task<List<PassiveDefinition>> GenerateForCharacter(
		string characterId,
		string personality,
		int count = 2,
		string correction = "",
		string model = null)
	{

		string element = CharacterStore.GetElementForCharacter(characterId);
		if (string.IsNullOrEmpty(element))
		{
			element = ElementPicker.PickRandomElement();
			CharacterStore.SetElementForCharacter(characterId, element);
			Debug.Log($"Assigned element '{element}' to character {characterId}");
		}


		var messages = new List<OllamaNetwork.ChatMessage>
		{
			new OllamaNetwork.ChatMessage { role = "system", content = systemPrompt }
		};

		messages.Add(new OllamaNetwork.ChatMessage { role = "user", content = "Example: a fiery melee character; element: fire; generate 1 passive." });
		messages.Add(new OllamaNetwork.ChatMessage { role = "assistant", content =
	@"[
  {
	""id"": ""passive_example_001"",
	""name"": ""Searing Strike"",
	""description"": ""On dealing damage: apply a burn that deals 25% of attack damage as fire DoT over 3s, and refund 20% cooldown on skill use."",
	""element"": ""fire"",
	""trigger"": ""on_damage"",
	""target"": ""enemy"",
	""effects"": [
	  {
		""effectType"": ""DamageOverTime"",
		""parameters"": {
		  ""valueType"": ""percent"",
		  ""value"": 25.0,
		  ""duration"": 3.0,
		  ""cooldown"": 2.0,
		  ""chance"": 1.0,
		  ""radius"": null,
		  ""piercePercent"": null,
		  ""stat"": null,
		  ""stackType"": ""none"",
		  ""maxStacks"": 0
		}
	  },
	  {
		""effectType"": ""CooldownRefund"",
		""parameters"": {
		  ""valueType"": ""percent"",
		  ""value"": 40.0,
		  ""duration"": 0,
		  ""cooldown"": 1.0,
		  ""chance"": 1.0,
		  ""radius"": null,
		  ""piercePercent"": null,
		  ""stat"": null,
		  ""stackType"": ""linear"",
		  ""maxStacks"": 2
		}
	  }
	],
	[
		{
		""id"": ""passive_phys_dot_001"",
		""name"": ""Bleeding Edge"",
		""description"": ""On hit: deal +30% physical damage and apply a bleed dealing 25% of attack damage over 4s."",
		""element"": ""earth"",
		""trigger"": ""on_hit"",
		""target"": ""enemy"",
		""effects"": [
		{
		""effectType"": ""PhysicalDamage"",
		""parameters"": {
		""valueType"": ""percent"",
		""value"": 30.0,
		""duration"": 0,
		""cooldown"": 1.0,
		""chance"": 0.9,
		""radius"": null,
		""piercePercent"": null,
		""stat"": null,
		""stackType"": ""none"",
		""maxStacks"": 0
		}
		},
		{
		""effectType"": ""DamageOverTime"",
		""parameters"": {
		""valueType"": ""percent"",
		""value"": 25.0,
		""duration"": 4.0,
		""cooldown"": 1.0,
		""chance"": 0.85,
		""radius"": null,
		""piercePercent"": null,
		""stat"": null,
		""stackType"": ""linear"",
		""maxStacks"": 2
		}
		}
		],
		""stacks"": { ""type"": ""none"", ""max"": 0 },
		""scaling"": { ""stat"": ""strength"", ""ratio"": 0.20 },
		""balance"": { ""min_value"": 25.0, ""max_value"": 35.0, ""notes"": ""Physical burst with bleed"" },
		""notes"": null
		},
		{
		""id"": ""passive_elem_splash_002"",
		""name"": ""Volcanic Arc"",
		""description"": ""On skill use: add 40% fire damage to the skill and cause attacks to splash for 35% to nearby enemies in 3m radius."",
		""element"": ""fire"",
		""trigger"": ""on_Skilluse"",
		""target"": ""area"",
		""effects"": [
		{
		""effectType"": ""ElementalDamage"",
		""parameters"": {
		""valueType"": ""percent"",
		""value"": 40.0,
		""duration"": 0,
		""cooldown"": 3.0,
		""chance"": 0.8,
		""radius"": null,
		""piercePercent"": null,
		""stat"": null,
		""stackType"": ""none"",
		""maxStacks"": 0
		}
		},
		{
		""effectType"": ""SplashDamage"",
		""parameters"": {
		""valueType"": ""percent"",
		""value"": 35.0,
		""duration"": 0.1,
		""cooldown"": 0.5,
		""chance"": 0.85,
		""radius"": 3.0,
		""piercePercent"": null,
		""stat"": null,
		""stackType"": ""none"",
		""maxStacks"": 0
		}
		}
		],
		""stacks"": { ""type"": ""none"", ""max"": 0 },
		""scaling"": { ""stat"": ""intelligence"", ""ratio"": 0.25 },
		""balance"": { ""min_value"": 35.0, ""max_value"": 45.0, ""notes"": ""Elemental add + splash"" },
		""notes"": null
		},
		{
		""id"": ""passive_shield_leech_003"",
		""name"": ""Guarded Hunger"",
		""description"": ""When hit: gain a 180 flat shield for 3s and heal for 30% of damage dealt as life leech."",
		""element"": ""No-Attribute"",
		""trigger"": ""on_hit"",
		""target"": ""self"",
		""effects"": [
		{
		""effectType"": ""ShieldOnHit"",
		""parameters"": {
		""valueType"": ""flat"",
		""value"": 100.0,
		""duration"": 3.0,
		""cooldown"": 10.0,
		""chance"": 0.95,
		""radius"": null,
		""piercePercent"": null,
		""stat"": null,
		""stackType"": ""linear"",
		""maxStacks"": 2
		}
		},
		{
		""effectType"": ""LifeLeech"",
		""parameters"": {
		""valueType"": ""percent"",
		""value"": 30.0,
		""duration"": 0.5,
		""cooldown"": 0.1,
		""chance"": 0.9,
		""radius"": null,
		""piercePercent"": null,
		""stat"": ""strength"",
		""stackType"": ""linear"",
		""maxStacks"": 3
		}
		}
		],
		""stacks"": { ""type"": ""linear"", ""max"": 2 },
		""scaling"": { ""stat"": ""strength"", ""ratio"": 0.30 },
		""balance"": { ""min_value"": 20.0, ""max_value"": 40.0, ""notes"": ""Defensive shield + sustain"" },
		""notes"": null
		},
		{
		""id"": ""passive_pierce_true_004"",
		""name"": ""Armor Bypass"",
		""description"": ""On dealing damage: ignore 25% of enemy armor and deal 25% true damage that bypasses defenses."",
		""element"": ""lightning"",
		""trigger"": ""on_damage"",
		""target"": ""enemy"",
		""effects"": [
		{
		""effectType"": ""Pierce"",
		""parameters"": {
		""valueType"": ""percent"",
		""value"": 25.0,
		""duration"": 0,
		""cooldown"": 2.0,
		""chance"": 0.9,
		""radius"": null,
		""piercePercent"": 25.0,
		""stat"": null,
		""stackType"": ""none"",
		""maxStacks"": 0
		}
		},
		{
		""effectType"": ""TrueDamage"",
		""parameters"": {
		""valueType"": ""percent"",
		""value"": 25.0,
		""duration"": 0,
		""cooldown"": 2.0,
		""chance"": 0.85,
		""radius"": null,
		""piercePercent"": null,
		""stat"": null,
		""stackType"": ""none"",
		""maxStacks"": 0
		}
		}
		],
		""stacks"": { ""type"": ""none"", ""max"": 0 },
		""scaling"": { ""stat"": ""strength"", ""ratio"": 0.18 },
		""balance"": { ""min_value"": 20.0, ""max_value"": 30.0, ""notes"": ""Direct bypass and true damage"" },
		""notes"": null
		},
		{
		""id"": ""passive_survivor_revive_005"",
		""name"": ""Last Stand"",
		""description"": ""When healed: reduce incoming damage by 30% and revive once with a long cooldown."",
		""element"": ""earth"",
		""trigger"": ""on_heal"",
		""target"": ""self"",
		""effects"": [
		{
		""effectType"": ""DamageReduction"",
		""parameters"": {
		""valueType"": ""percent"",
		""value"": 30.0,
		""duration"": 6.0,
		""cooldown"": 2.0,
		""chance"": 0.9,
		""radius"": null,
		""piercePercent"": null,
		""stat"": null,
		""stackType"": ""none"",
		""maxStacks"": 0
		}
		},
		{
		""effectType"": ""Revive"",
		""parameters"": {
		""valueType"": ""percent"",
		""value"": 20.0,
		""duration"": 1,
		""cooldown"": 300.0,
		""chance"": 0.8,
		""radius"": null,
		""piercePercent"": null,
		""stat"": null,
		""stackType"": ""none"",
		""maxStacks"": 0
		}
		}
		],
		""stacks"": { ""type"": ""none"", ""max"": 0 },
		""scaling"": { ""stat"": ""none"", ""ratio"": 0.12 },
		""balance"": { ""min_value"": 25.0, ""max_value"": 35.0, ""notes"": ""High-survivability passive"" },
		""notes"": null
		},
		{
		""id"": ""passive_mana_kill_006"",
		""name"": ""Soul Harvest"",
		""description"": ""On critical hit: regenerate 30 flat mana over time and restore 25 resource on killing an enemy."",
		""element"": ""No-Attribute"",
		""trigger"": ""on_critical_hit"",
		""target"": ""self"",
		""effects"": [
		{
		""effectType"": ""ManaRegen"",
		""parameters"": {
		""valueType"": ""flat"",
		""value"": 30.0,
		""duration"": 5.0,
		""cooldown"": 2.0,
		""chance"": 0.9,
		""radius"": null,
		""piercePercent"": null,
		""stat"": null,
		""stackType"": ""none"",
		""maxStacks"": 0
		}
		},
		{
		""effectType"": ""ResourceOnKill"",
		""parameters"": {
		""valueType"": ""flat"",
		""value"": 25.0,
		""duration"": 0,
		""cooldown"": 3.0,
		""chance"": 0.85,
		""radius"": null,
		""piercePercent"": null,
		""stat"": null,
		""stackType"": ""none"",
		""maxStacks"": 0
		}
		}
		],
		""stacks"": { ""type"": ""none"", ""max"": 0 },
		""scaling"": { ""stat"": ""intelligence"", ""ratio"": 0.22 },
		""balance"": { ""min_value"": 20.0, ""max_value"": 35.0, ""notes"": ""Resource sustain passive"" },
		""notes"": null
		},
		{
		""id"": ""passive_cc_refund_007"",
		""name"": ""Entrapping Momentum"",
		""description"": ""On skill use: 75% chance to stun enemies in 2m radius for 1.2s and refund 30% cooldown on triggered skills."",
		""element"": ""wind"",
		""trigger"": ""on_Skilluse"",
		""target"": ""area"",
		""effects"": [
		{
		""effectType"": ""Stun"",
		""parameters"": {
		""valueType"": ""percent"",
		""value"": 20.0,
		""duration"": 1.2,
		""cooldown"": 6.0,
		""chance"": 0.75,
		""radius"": 2.0,
		""piercePercent"": null,
		""stat"": null,
		""stackType"": ""none"",
		""maxStacks"": 0
		}
		},
		{
		""effectType"": ""CooldownRefund"",
		""parameters"": {
		""valueType"": ""percent"",
		""value"": 30.0,
		""duration"": 0,
		""cooldown"": 0,
		""chance"": 0.8,
		""radius"": null,
		""piercePercent"": null,
		""stat"": null,
		""stackType"": ""none"",
		""maxStacks"": 0
		}
		}
		],
		""stacks"": { ""type"": ""none"", ""max"": 0 },
		""scaling"": { ""stat"": ""none"", ""ratio"": 0.14 },
		""balance"": { ""min_value"": 25.0, ""max_value"": 35.0, ""notes"": ""CC with cooldown reward"" },
		""notes"": null
		},
		{
		""id"": ""passive_speed_range_008"",
		""name"": ""Swift Reach"",
		""description"": ""On skill use: increase attack speed by 30% and increase range by 25%."",
		""element"": ""wind"",
		""trigger"": ""on_Skilluse"",
		""target"": ""self"",
		""effects"": [
		{
		""effectType"": ""AttackSpeed"",
		""parameters"": {
		""valueType"": ""percent"",
		""value"": 30.0,
		""duration"": 6.0,
		""cooldown"": 4.0,
		""chance"": 0.9,
		""radius"": null,
		""piercePercent"": null,
		""stat"": null,
		""stackType"": ""exponential"",
		""maxStacks"": 3
		}
		},
		{
		""effectType"": ""RangeIncrease"",
		""parameters"": {
		""valueType"": ""percent"",
		""value"": 25.0,
		""duration"": 6.0,
		""cooldown"": 5.0,
		""chance"": 0.85,
		""radius"": null,
		""piercePercent"": null,
		""stat"": null,
		""stackType"": ""none"",
		""maxStacks"": 0
		}
		}
		],
		""stacks"": { ""type"": ""exponential"", ""max"": 3 },
		""scaling"": { ""stat"": ""agility"", ""ratio"": 0.28 },
		""balance"": { ""min_value"": 25.0, ""max_value"": 35.0, ""notes"": ""Speed + reach buff"" },
		""notes"": null
		},
		{
		""id"": ""passive_res_reflect_009"",
		""name"": ""Aegis Return"",
		""description"": ""On block: increase resistances by 30% and reflect 20% of received damage back to the attacker."",
		""element"": ""light"",
		""trigger"": ""on_block"",
		""target"": ""self"",
		""effects"": [
		{
		""effectType"": ""ResistanceBoost"",
		""parameters"": {
		""valueType"": ""percent"",
		""value"": 30.0,
		""duration"": 8.0,
		""cooldown"": 3.0,
		""chance"": 0.9,
		""radius"": null,
		""piercePercent"": null,
		""stat"": null,
		""stackType"": ""none"",
		""maxStacks"": 0
		}
		},
		{
		""effectType"": ""ReflectDamage"",
		""parameters"": {
		""valueType"": ""percent"",
		""value"": 20.0,
		""duration"": 0,
		""cooldown"": 3.0,
		""chance"": 0.8,
		""radius"": null,
		""piercePercent"": null,
		""stat"": null,
		""stackType"": ""none"",
		""maxStacks"": 0
		}
		}
		],
		""stacks"": { ""type"": ""none"", ""max"": 0 },
		""scaling"": { ""stat"": ""none"", ""ratio"": 0.16 },
		""balance"": { ""min_value"": 20.0, ""max_value"": 35.0, ""notes"": ""Defensive resist + reflect"" },
		""notes"": null
		},
		{
		""id"": ""passive_hot_crit_010"",
		""name"": ""Renewing Fury"",
		""description"": ""On dealing damage: heal for 30% over 5s and increase critical damage by 35%."",
		""element"": ""water"",
		""trigger"": ""on_damage"",
		""target"": ""self"",
		""effects"": [
		{
		""effectType"": ""HealOverTime"",
		""parameters"": {
		""valueType"": ""percent"",
		""value"": 30.0,
		""duration"": 5.0,
		""cooldown"": 2.0,
		""chance"": 0.9,
		""radius"": null,
		""piercePercent"": null,
		""stat"": null,
		""stackType"": ""none"",
		""maxStacks"": 0
		}
		},
		{
		""effectType"": ""CriticalDamage"",
		""parameters"": {
		""valueType"": ""percent"",
		""value"": 35.0,
		""duration"": 3.0,
		""cooldown"": 0.5,
		""chance"": 0.85,
		""radius"": null,
		""piercePercent"": null,
		""stat"": null,
		""stackType"": ""none"",
		""maxStacks"": 0
		}
		}
		],
		""stacks"": { ""type"": ""none"", ""max"": 0 },
		""scaling"": { ""stat"": ""none"", ""ratio"": 0.20 },
		""balance"": { ""min_value"": 25.0, ""max_value"": 40.0, ""notes"": ""Heal + crit synergy"" },
		""notes"": null
		}
		]

	""stacks"": { ""type"": ""none"", ""max"": 0 },
	""scaling"": { ""stat"": ""strength"", ""ratio"": 0.15 },
	""balance"": { ""min_value"": 20.0, ""max_value"": 25.0, ""notes"": ""Example values"" },
	""notes"": ""Example: DoT + cooldown refund as two effects.""
  }
]" });


		messages.Add(new OllamaNetwork.ChatMessage { role = "user", content = $"Generate {count} passive abilities for this character:\n\"{personality}\"\n\nElement for this character (fixed): \"{element}\"\n\nConstraints:\n- Use only the allowed effect names and the parameter schema exactly as shown in the system prompt.\n- Allowed triggers: on_hit, on_damage, on_Skilluse, on_block, on_critical_hit, on_heal.\n- Percent values 0..100, chance 0..1.\nReturn the JSON array or single object only." });

		
		string assistantText = await OllamaNetwork.SendAndGetAssistantTextAsync(messages, model);
		if (string.IsNullOrWhiteSpace(assistantText))
		{
			Debug.LogError("[PassiveGenerator] Empty assistant response");
			return new List<PassiveDefinition>();
		}

		
		List<PassiveDefinition> parsed = ParseAssistantJsonRobust(assistantText);

		if (parsed.Count == 0)
		{
			Debug.LogWarning("[PassiveGenerator] No passives parsed. Raw response:\n" + assistantText);
			return new List<PassiveDefinition>();
		}

		
		for (int i = parsed.Count - 1; i >= 0; i--)
		{
			var pass = parsed[i];

			
			pass.trigger = NormalizeTrigger(pass.trigger);

			if (!ValidateAndFixPassive(pass, element, out string err))
			{
				Debug.LogWarning($"[PassiveGenerator] Removing invalid passive: {err}\n{JsonConvert.SerializeObject(pass)}");
				parsed.RemoveAt(i);
			}
		}

		
		bool inverted = MaybeInvertPassives(parsed, characterId);
		

		if (!DisableDescriptionConversion)
		{
			RegenerateDescriptions(parsed, inverted);
		}
		// Persist passives
		CharacterStore.AddPassivesToCharacter(characterId, parsed);
		if (inverted)
			Debug.Log($"[PassiveGenerator] Inversion applied and persisted for character {characterId}.");

		return parsed;
		
	}
	
	// ---------- Add these helper methods inside PassiveGenerator ----------

private static void RegenerateDescriptions(List<PassiveDefinition> passives, bool invertedFlag)
{
	if (passives == null) return;
	foreach (var p in passives)
	{
		string desc = BuildDescriptionFromEffects(p);
		if (invertedFlag)
			desc = "(Inverted) " + desc;
		// Always set description to the generated one (ensures no mismatch)
		p.description = desc;
	}
}

private static string BuildDescriptionFromEffects(PassiveDefinition p)
{
	if (p == null || p.effects == null || p.effects.Count == 0) return p?.description ?? "";

	// Trigger prefix
	string triggerPrefix = p.trigger switch
	{
		"on_damage" => "On dealing damage: ",
		"on_hit" => "When hit: ",
		"on_Skilluse" => "When using a skill: ",
		"on_block" => "On block: ",
		"on_critical_hit" => "On critical hit: ",
		"on_heal" => "When healed: ",
		_ => ""
	};

	var phrases = new List<string>();
	foreach (var e in p.effects)
	{
		phrases.Add(EffectPhrase(e, p.element, p.target));
	}

	// Join with " and " (two effects guaranteed by your validator)
	string joined = string.Join(" and ", phrases);
	if (string.IsNullOrWhiteSpace(joined)) joined = p.description ?? "";

	// Optionally ensure element mention (useful for elemental damage)
	string elementNote = "";
	// if any effect is elemental (ElementalDamage, DamageOverTime maybe element-specific), include element
	bool mentionsElementAlready = joined.IndexOf(p.element ?? "", StringComparison.OrdinalIgnoreCase) >= 0;
	if (!string.IsNullOrEmpty(p.element) && !mentionsElementAlready && p.element != "No-Attribute")
	{
		// only append element if it makes sense; keep minimal
		elementNote = $" Element: {p.element}.";
	}

	return triggerPrefix + joined + elementNote;
}

private static string EffectPhrase(EffectInstance e, string element, string target)
{
	if (e == null || e.parameters == null) return "";

	string val = FormatValue(e.parameters);
	string chance = e.parameters.chance < 0.999 ? $" ({Math.Round(e.parameters.chance * 100)}% chance)" : "";
	string dur = e.parameters.duration.HasValue ? $" over {e.parameters.duration.Value}s" : "";
	string radius = e.parameters.radius.HasValue ? $" in {e.parameters.radius.Value}m radius" : "";
	string t = string.IsNullOrEmpty(target) ? "" : (target == "enemy" ? "target" : target);

	// Each effect type gets a template. Keep it explicit and player-friendly.
	switch (e.effectType)
	{
		case "DamageOverTime":
			// show element if present
			return $"apply a {(string.IsNullOrEmpty(element) || element == "No-Attribute" ? "" : element + " ")}damage over time dealing {val}{dur}{chance}";

		case "HealOverTime":
			return $"heal for {val}{dur}{chance}";

		case "InstantHeal":
			return $"instantly heal for {val}{chance}";

		case "PhysicalDamage":
			return $"deal additional physical damage of {val}{chance}";

		case "ElementalDamage":
			return $"add {element} damage of {val}{chance}";

		case "CriticalChance":
			return $"increase critical chance by {val}{chance}";

		case "CriticalDamage":
			return $"increase critical damage by {val}{chance}";

		case "Pierce":
			return $"ignore {Math.Round(e.parameters.piercePercent ?? 0)}% of enemy armor";

		case "SplashDamage":
			return $"also hit nearby enemies for {val}{radius}{chance}";

		case "ReflectDamage":
			return $"reflect {val} of next received damage back to attacker{chance}";

		case "TrueDamage":
			return $"deal {val} true damage that bypasses defenses{chance}";

		case "SplashOnDeath":
			return $"explode enemies on death dealing {val}{radius} damage";

		case "DamageReduction":
			return $"reduce incoming damage by {val}{chance}";

		case "ArmorBoost":
			return $"increase armor by {val}{chance}";

		case "ResistanceBoost":
			return $"increase resistances by {val}{chance}";

		case "ShieldOnHit":
			return $"grant a shield of {val}{dur} when triggered{chance}";

		case "BlockChance":
			return $"gain {val} automatic block chance{chance}";

		case "DodgeChance":
			return $"gain {val} automatic dodge chance{chance}";

		case "HealthRegen":
			return $"regenerate {val} health{dur}{chance}";

		case "LifeLeech":
			return $"heal for {val} of damage dealt{chance}";

		case "MaxHealth":
			return $"increase max health by {val}{chance}";

		case "Revive":
			return $"revive once the next time you die (chance {Math.Round(e.parameters.chance * 100)}%) with cooldown {e.parameters.cooldown ?? 0}s";

		case "DamageCap":
			return $"cap next damage taken from a single hit at {val}{chance}";

		case "ManaRegen":
			return $"regenerate {val} mana over time{dur}{chance}";

		case "ResourceOnKill":
			return $"Gain {val} resource on killing an enemy{chance}";

		case "CooldownRefund":
			return $"refund {val}% cooldown when triggered{chance}";

		case "StatusCure":
			return $"remove negative status effects{chance}";

		case "Slow":
			return $"apply a slow of {val}% for {e.parameters.duration ?? 0}s{chance}";

		case "Stun":
			return $"stun for {e.parameters.duration ?? 0}s{chance}";

		case "Root":
			return $"root target for {e.parameters.duration ?? 0}s{chance}";

		case "Silence":
			return $"silence targets for {e.parameters.duration ?? 0}s{chance}";

		case "Fear":
			return $"fear targets for {e.parameters.duration ?? 0}s{chance}";

		case "Disarm":
			return $"disarm targets for {e.parameters.duration ?? 0}s{chance}";

		case "Knockback":
			return $"knockback targets{radius}{chance}";

		case "Taunt":
			return $"taunt enemies to attack you for {e.parameters.duration ?? 0}s{chance}";

		case "AttackSpeed":
			return $"increase attack speed by {val}{chance}";

		case "MovementSpeed":
			return $"increase movement speed by {val}{chance}";

		case "RangeIncrease":
			return $"increase attack/ability range by {val}{chance}";

		case "Accuracy":
			return $"increase accuracy by {val}{chance}";

		case "CriticalResist":
			return $"reduce chance of being crit by {val}{chance}";

		case "StatBoost":
			string stat = string.IsNullOrEmpty(e.parameters.stat) || e.parameters.stat == "none" ? "" : e.parameters.stat + " ";
			return $"increase {stat}by {val}{chance}";

		default:
			// fallback: be conservative
			return $"{e.effectType} {val}{dur}{chance}";
	}
}

private static string FormatValue(EffectParameters p)
{
	if (p == null) return "0";
	if (string.Equals(p.valueType, "percent", StringComparison.OrdinalIgnoreCase))
		return $"{Math.Round(p.value, 1)}%";
	else
		return $"{Math.Round(p.value, 1)}";
}


	/// <summary>
	/// Retry wrapper that will attempt multiple times if parsing fails.
	/// </summary>
	public static async Task<List<PassiveDefinition>> GenerateWithRetries(
		string characterId,
		string personality,
		int count = 2,
		int maxAttempts = 20,
		string model = null)
	{
		for (int attempt = 1; attempt <= maxAttempts; attempt++)
		{
			Debug.Log($"[PassiveGenerator] Attempt {attempt}/{maxAttempts} for {characterId}");

			string correction = "";
			if (attempt > 1)
			{
				correction = "⚠️ Your last response was NOT valid JSON. Return ONLY valid JSON that matches the Passive schema exactly. No markdown, no commentary, only JSON and everything filled out!, make sure no / are present in the JSON!.\n\n";
			}

			var list = await GenerateForCharacter(characterId, personality, count, correction, model);

			if (list != null && list.Count > 0)
			{
				Debug.Log($"[PassiveGenerator] Success on attempt {attempt}, got {list.Count} passives.");
				return list;
			}

			Debug.LogWarning($"[PassiveGenerator] Attempt {attempt} failed (invalid or empty). Retrying...");
			await Task.Delay(RetryDelayMs);
		}

		Debug.LogError($"[PassiveGenerator] Failed after {maxAttempts} attempts for {characterId}");
		return new List<PassiveDefinition>();
	}

	// -----------------------
	// Parsing helpers (robust)
	// -----------------------

private static List<PassiveDefinition> ParseAssistantJsonRobust(string assistantText)
{
	var result = new List<PassiveDefinition>();
	if (string.IsNullOrWhiteSpace(assistantText)) return result;

	Debug.Log($"[PassiveGenerator] Raw assistantText:\n{assistantText}");

	// 1) Try direct parse and tolerant handling
	try
	{
		var root = JToken.Parse(assistantText);

		if (root.Type == JTokenType.Array)
		{
			foreach (var item in root.Children())
			{
				if (item.Type == JTokenType.Object)
				{
					TryAddPassiveFromToken(item, result);
				}
				else if (item.Type == JTokenType.String)
				{
					var s = item.ToString();
						// the string may be escaped JSON; try to unescape and parse
						if (TryParseJsonStringToPassivesV2(s, out var parsedFromString))
						{ 
						result.AddRange(parsedFromString);
					 }

						
				}
			}
			if (result.Count > 0) return result;
		}
		else if (root.Type == JTokenType.Object)
		{
			TryAddPassiveFromToken(root, result);
			if (result.Count > 0) return result;
		}
		else if (root.Type == JTokenType.String)
		{
			var s = root.ToString();
			if (TryParseJsonStringToPassivesV2(s, out var listFromString))
				result.AddRange(listFromString);
			if (result.Count > 0) return result;
		}
	}
	catch { /* fallthrough to cleaning */ }

	// 2) Try cleaning substring (existing function)
	string candidate = ExtractJsonSubstring(assistantText);
	if (!string.IsNullOrWhiteSpace(candidate))
	{
		try
		{
			var candRoot = JToken.Parse(candidate);
			if (candRoot.Type == JTokenType.Array)
			{
				foreach (var item in candRoot.Children())
				{
					if (item.Type == JTokenType.Object) TryAddPassiveFromToken(item, result);
					else if (item.Type == JTokenType.String)
					{
						if (TryParseJsonStringToPassivesV2(item.ToString(), out var listFromString))
							result.AddRange(listFromString);
					}
				}
				if (result.Count > 0) return result;
			}
			else if (candRoot.Type == JTokenType.Object)
			{
				TryAddPassiveFromToken(candRoot, result);
				if (result.Count > 0) return result;
			}
			else if (candRoot.Type == JTokenType.String)
			{
				if (TryParseJsonStringToPassivesV2(candRoot.ToString(), out var listFromString))
					result.AddRange(listFromString);
				if (result.Count > 0) return result;
			}
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[PassiveGenerator] Parse failed after cleaning: " + ex.Message + "\nCandidate:\n" + candidate);
		}
	}

	// 3) Last-resort: extract all {...} blocks and parse individually
	var matches = Regex.Matches(assistantText, @"\{(?:[^{}]|(?<open>\{)|(?<-open>\}))*\}(?(open)(?!))", RegexOptions.Singleline);
	foreach (Match m in matches)
	{
		try { var tok = JToken.Parse(m.Value); TryAddPassiveFromToken(tok, result); }
		catch { /* ignore */ }
	}

	return result;
}

// helper to attempt converting a JToken->PassiveDefinition and add to result if valid
private static void TryAddPassiveFromToken(JToken tok, List<PassiveDefinition> outList)
{
	try
	{
		var pd = tok.ToObject<PassiveDefinition>();
		if (pd != null) outList.Add(pd);
	}
	catch
	{
		// ignore individual failures
	}
}

// robust parser for a JSON string that may be escaped; returns list of passive objects
// Robust parser for a JSON string that may be escaped; returns list of passive objects
private static bool TryParseJsonStringToPassivesV2(string jsonString, out List<PassiveDefinition> parsed)
{
	parsed = new List<PassiveDefinition>();
	if (string.IsNullOrWhiteSpace(jsonString)) return false;

	string s = jsonString.Trim();

	// If the string looks like a quoted JSON literal, try to deserialize it once to unescape
	try
	{
		if ((s.StartsWith("\"") && s.EndsWith("\"")) || s.StartsWith("\\\""))
		{
			try
			{
				string unq = JsonConvert.DeserializeObject<string>(s);
				if (!string.IsNullOrEmpty(unq)) s = unq;
			}
			catch { /* not a single-quoted JSON string; continue */ }
		}
	}
	catch { /* ignore */ }

	// Direct parse attempt
	try
	{
		var root = JToken.Parse(s);
		if (root.Type == JTokenType.Array)
		{
			foreach (var item in root.Children())
			{
				if (item.Type == JTokenType.Object)
					TryAddPassiveFromToken(item, parsed);
				else if (item.Type == JTokenType.String)
				{
					if (TryParseJsonStringToPassivesV2(item.ToString(), out var nested))
						parsed.AddRange(nested);
				}
			}
			return parsed.Count > 0;
		}
		else if (root.Type == JTokenType.Object)
		{
			TryAddPassiveFromToken(root, parsed);
			return parsed.Count > 0;
		}
	}
	catch
	{
		// fallback below
	}

	// Unescape common escaping and try again
	string unescaped = s.Replace("\\\"", "\"").Replace("\\\\\"", "\\\"");
	int firstBrace = unescaped.IndexOf('{');
	int firstBracket = unescaped.IndexOf('[');
	int start = (firstBracket >= 0 && (firstBracket < firstBrace || firstBrace == -1)) ? firstBracket : firstBrace;
	if (start >= 0)
	{
		string sub = unescaped.Substring(start);
		// trim trailing junk
		int lastBrace = sub.LastIndexOf('}');
		int lastBracket = sub.LastIndexOf(']');
		int end = Math.Max(lastBrace, lastBracket);
		if (end > 0) sub = sub.Substring(0, end + 1);

		try
		{
			var root2 = JToken.Parse(sub);
			if (root2.Type == JTokenType.Array)
			{
				foreach (var item in root2.Children())
					if (item.Type == JTokenType.Object) TryAddPassiveFromToken(item, parsed);
				return parsed.Count > 0;
			}
			else if (root2.Type == JTokenType.Object)
			{
				TryAddPassiveFromToken(root2, parsed);
				return parsed.Count > 0;
			}
		}
		catch { /* ignore */ }
	}

	return false;
}



	// Helper: if a string contains JSON (object or array), parse it into PassiveDefinition(s)
	private static bool TryParseJsonStringToPassives(string jsonString, out List<PassiveDefinition> parsed)
	{
		parsed = new List<PassiveDefinition>();
		if (string.IsNullOrWhiteSpace(jsonString)) return false;

		// First, trim surrounding quotes or whitespace
		string s = jsonString.Trim();

		// If string is itself a quoted JSON (e.g. starts with "[" or "{" but wrapped with quotes and escaped),
		// try unescaping common escape sequences. Replace '\"' with '"' etc.
		// We'll try a few tolerant attempts.

		// Attempt raw parse
		try
		{
			var token = JToken.Parse(s);
			if (token.Type == JTokenType.Object)
			{
				var pd = token.ToObject<PassiveDefinition>();
				if (pd != null) parsed.Add(pd);
				return parsed.Count > 0;
			}
			if (token.Type == JTokenType.Array)
			{
				foreach (var item in token.Children())
				{
					if (item.Type == JTokenType.Object)
					{
						try { var pd = item.ToObject<PassiveDefinition>(); if (pd != null) parsed.Add(pd); }
						catch { }
					}
					else if (item.Type == JTokenType.String)
					{
						// nested string - recursive attempt
						if (TryParseJsonStringToPassivesV2(item.ToString(), out var nested))
							parsed.AddRange(nested);
					}
				}
				return parsed.Count > 0;
			}
		}
		catch { /* fall through to unescape attempts */ }

		// Unescape common escaping (\" -> "), also handle double-escaped content
		string unescaped = s.Replace("\\\"", "\"").Replace("\\\\\"", "\\\"");

		// If the string itself contains an array/object after unescaping, try parse again
		int firstBrace = unescaped.IndexOf('{');
		int firstBracket = unescaped.IndexOf('[');
		if (firstBrace >= 0 || firstBracket >= 0)
		{
			int start = (firstBracket >= 0 && (firstBracket < firstBrace || firstBrace == -1)) ? firstBracket : firstBrace;
			string sub = unescaped.Substring(start).Trim();

			// remove trailing junk after last bracket
			int lastBrace = sub.LastIndexOf('}');
			int lastBracket = sub.LastIndexOf(']');
			int end = Math.Max(lastBrace, lastBracket);
			if (end > 0 && end + 1 <= sub.Length)
				sub = sub.Substring(0, end + 1);

			try
			{
				var token2 = JToken.Parse(sub);
				if (token2.Type == JTokenType.Object)
				{
					var pd = token2.ToObject<PassiveDefinition>();
					if (pd != null) parsed.Add(pd);
					return parsed.Count > 0;
				}
				else if (token2.Type == JTokenType.Array)
				{
					foreach (var item in token2.Children())
					{
						if (item.Type == JTokenType.Object)
						{
							try { var pd = item.ToObject<PassiveDefinition>(); if (pd != null) parsed.Add(pd); }
							catch { }
						}
					}
					return parsed.Count > 0;
				}
			}
			catch { /* ignore */ }
		}

		return false;
	}
private static void RepairParsedPassives(List<PassiveDefinition> parsed)
{
	if (parsed == null) return;
	var rng = new System.Random();

	// synonyms -> mapping to allowed effectType (and optional extra adjustments)
	var effectNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		{"LightningDamage", "ElementalDamage"},
		{"FireDamage", "ElementalDamage"},
		{"WaterDamage", "ElementalDamage"},
		{"IntelligenceBoost", "StatBoost"},
		{"StrengthBoost", "StatBoost"},
		{"AgilityBoost", "StatBoost"},
		{"Heal", "InstantHeal"},
		{"HPRegen", "HealthRegen"},
		{"ManaRegenFlat", "ManaRegen"},
		// add more as you find them
	};

	foreach (var p in parsed)
	{
		if (p.effects == null) continue;
		foreach (var e in p.effects)
		{
			// 1) map effectType synonyms
			if (!string.IsNullOrEmpty(e.effectType) && !AllowedEffectNames.Contains(e.effectType))
			{
				if (effectNameMap.TryGetValue(e.effectType, out var mapped))
				{
					e.effectType = mapped;
					// if mapped to ElementalDamage, ensure passive.element is set to the original element if available
				}
			}

			// 2) ensure parameters object exists
			if (e.parameters == null) e.parameters = new EffectParameters();

			var ep = e.parameters;

			// 3) normalize valueType
			if (string.IsNullOrWhiteSpace(ep.valueType) || ep.valueType.Equals("none", StringComparison.OrdinalIgnoreCase))
				ep.valueType = "percent"; // default

			// 4) fill missing numeric values with safe defaults
			if (double.IsNaN(ep.value) || ep.value == 0)
			{
				// choose defaults based on effectType
				switch (e.effectType)
				{
					case "DamageOverTime":
					case "HealOverTime":
						ep.value = 25.0; break;
					case "ElementalDamage":
					case "PhysicalDamage":
					case "TrueDamage":
					case "CriticalDamage":
						ep.value = 30.0; break;
					case "Pierce":
						ep.value = 20.0; ep.piercePercent = 20.0; break;
					case "Stun":
					case "Slow":
						ep.value = 0.0; break;
					case "ManaRegen":
					case "ResourceOnKill":
						ep.value = 30.0; break;
					default:
						ep.value = 25.0; break;
				}
			}

			// 5) chance default
			if (double.IsNaN(ep.chance) || ep.chance <= 0.0) ep.chance = 1.0;

			// 6) duration / cooldown defaults: keep as provided; if required for the effectType and null assign safe default
			if ((e.effectType == "DamageOverTime" || e.effectType == "HealOverTime" || e.effectType == "Stun" || e.effectType == "Slow") && !ep.duration.HasValue)
				ep.duration = 3.0;

			// 7) stackType default + maxStacks
			if (string.IsNullOrWhiteSpace(ep.stackType)) ep.stackType = "none";
			if (ep.maxStacks < 0 || ep.maxStacks > 10) ep.maxStacks = 0;
			if (!ep.stackType.Equals("none", StringComparison.OrdinalIgnoreCase) && ep.maxStacks == 0) ep.maxStacks = 1;

			// 8) ensure permitted stat strings or null
			if (string.IsNullOrWhiteSpace(ep.stat) || ep.stat == "null") ep.stat = "none";
			if (!(ep.stat == "strength" || ep.stat == "intelligence" || ep.stat == "agility" || ep.stat == "level" || ep.stat == "none"))
				ep.stat = "none";
		}

		// 9) If effectTypes like StatBoost exist, set parameters.stat from the effectType hint if possible
		foreach (var e in p.effects)
		{
			if (e.effectType.Equals("StatBoost", StringComparison.OrdinalIgnoreCase) && (string.IsNullOrEmpty(e.parameters.stat) || e.parameters.stat == "none"))
			{
				// try infer from name or passive name
				if (p.name.IndexOf("intelligence", StringComparison.OrdinalIgnoreCase) >= 0) e.parameters.stat = "intelligence";
				else if (p.name.IndexOf("strength", StringComparison.OrdinalIgnoreCase) >= 0) e.parameters.stat = "strength";
				else if (p.name.IndexOf("agility", StringComparison.OrdinalIgnoreCase) >= 0) e.parameters.stat = "agility";
				else e.parameters.stat = "strength"; // fallback
			}
		}
	}
}



	private static string ExtractJsonSubstring(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw)) return null;

		raw = raw.Trim();

		// Remove Markdown code fences
		raw = Regex.Replace(raw, @"^```(?:json)?\s*", "", RegexOptions.IgnoreCase);
		raw = Regex.Replace(raw, @"\s*```$", "", RegexOptions.IgnoreCase);

		// Remove leading phrases
		raw = Regex.Replace(raw, @"^(?:Here is the JSON:|Response:|Result:)\s*", "", RegexOptions.IgnoreCase);

		// Find first brace/bracket
		int firstBrace = raw.IndexOf('{');
		int firstBracket = raw.IndexOf('[');
		int startIndex;
		char startChar;
		if (firstBracket >= 0 && (firstBracket < firstBrace || firstBrace == -1))
		{
			startIndex = firstBracket;
			startChar = '[';
		}
		else if (firstBrace >= 0)
		{
			startIndex = firstBrace;
			startChar = '{';
		}
		else
		{
			return null;
		}

		// Find matching closing by scanning and skipping strings
		char open = startChar;
		char close = (open == '{') ? '}' : ']';
		int depth = 0;
		int endIndex = -1;
		for (int i = startIndex; i < raw.Length; i++)
		{
			char c = raw[i];
			if (c == open) depth++;
			else if (c == close)
			{
				depth--;
				if (depth == 0)
				{
					endIndex = i;
					break;
				}
			}
			else if (c == '"')
			{
				i = SkipJsonString(raw, i);
			}
		}

		if (endIndex == -1)
		{
			int lastBrace = raw.LastIndexOf('}');
			int lastBracket = raw.LastIndexOf(']');
			endIndex = Math.Max(lastBrace, lastBracket);
			if (endIndex <= startIndex) return null;
		}

		string candidate = raw.Substring(startIndex, endIndex - startIndex + 1).Trim();

		// Heuristic repairs: convert single quotes, remove trailing commas
		if (candidate.Contains('\''))
			candidate = Regex.Replace(candidate, @"'([^']*)'", @"""$1""");

		candidate = Regex.Replace(candidate, @",\s*(?=[}\]])", "");

		return candidate;
	}
	

	private static int SkipJsonString(string s, int startIndex)
	{
		int i = startIndex + 1;
		while (i < s.Length)
		{
			if (s[i] == '\\') i += 2;
			else if (s[i] == '"') return i;
			else i++;
		}
		return s.Length - 1;
	}

	// -----------------------
	// Trigger normalization
	// -----------------------
	private static string NormalizeTrigger(string rawTrigger)
	{
		if (string.IsNullOrWhiteSpace(rawTrigger)) return "on_damage";

		string t = rawTrigger.Trim();

		// if already allowed, return canonical
		foreach (var a in AllowedTriggers)
			if (string.Equals(a, t, StringComparison.OrdinalIgnoreCase))
				return a;

		if (TriggerSynonyms.TryGetValue(t, out var mapped))
			return mapped;

		string lowered = t.ToLowerInvariant();
		if (lowered.Contains("damage") || lowered.Contains("deal")) return "on_damage";
		if (lowered.Contains("hit") && lowered.Contains("take")) return "on_hit";
		if (lowered.Contains("skill") || lowered.Contains("ability")) return "on_Skilluse";
		if (lowered.Contains("block") || lowered.Contains("parry")) return "on_block";
		if (lowered.Contains("crit")) return "on_critical_hit";
		if (lowered.Contains("heal")) return "on_heal";

		return "on_damage";
	}

	// -----------------------
	// Validation & clamping (passive + effects)
	// -----------------------

	private static bool ValidateAndFixPassive(PassiveDefinition p, string forcedElement, out string error)
	{
		error = null;
		if (p == null) { error = "passive null"; return false; }
		if (string.IsNullOrWhiteSpace(p.name)) { error = "missing name"; return false; }
		if (string.IsNullOrWhiteSpace(p.id)) p.id = Guid.NewGuid().ToString();
		if (string.IsNullOrWhiteSpace(p.element)) p.element = forcedElement;
		else p.element = forcedElement; // enforce engine-chosen element

		if (string.IsNullOrWhiteSpace(p.trigger)) p.trigger = "on_damage";
		else p.trigger = NormalizeTrigger(p.trigger);
		if (!AllowedTriggers.Contains(p.trigger)) p.trigger = "on_damage";

		var validTargets = new HashSet<string> { "self", "ally", "enemy", "area" };
		if (string.IsNullOrEmpty(p.target) || !validTargets.Contains(p.target)) p.target = "self";

		if (p.effects == null || p.effects.Count == 0) { error = "no effects provided"; return false; }

		// Validate each effect instance
		for (int i = p.effects.Count - 1; i >= 0; i--)
		{
			var e = p.effects[i];
			if (!ValidateEffectInstance(e, out string eErr))
			{
				Debug.LogWarning($"[PassiveGenerator] Removing invalid effect in passive {p.id}: {eErr}");
				p.effects.RemoveAt(i);
			}
		}

		if (p.effects.Count == 0) { error = "all effects invalid"; return false; }

		// stacks/scaling defaults

		if (p.scaling == null) p.scaling = new Scaling { stat = "none", ratio = 0.0 };
		if (p.balance == null) p.balance = new Balance { min_value = 0, max_value = 0, notes = "" };

		return true;
	}

	private static bool ValidateEffectInstance(EffectInstance e, out string error)
	{
		error = null;
		if (e == null) { error = "effect is null"; return false; }
		if (string.IsNullOrWhiteSpace(e.effectType)) { error = "effectType missing or empty"; return false; }
		if (!AllowedEffectNames.Contains(e.effectType)) { error = $"disallowed effect type '{e.effectType}'"; return false; }
		if (e.parameters == null) { error = "parameters missing"; return false; }

		// clamp chance
		if (double.IsNaN(e.parameters.chance)) e.parameters.chance = 1.0;
		e.parameters.chance = Math.Max(0.0, Math.Min(1.0, e.parameters.chance));

		// default valueType
		if (string.IsNullOrWhiteSpace(e.parameters.valueType)) e.parameters.valueType = "percent";
		if (e.parameters.valueType != "percent" && e.parameters.valueType != "flat") e.parameters.valueType = "percent";

		// clamp percent values 0..100
		if (e.parameters.valueType == "percent")
		{
			if (double.IsNaN(e.parameters.value)) e.parameters.value = 0;
			e.parameters.value = Math.Max(0.0, Math.Min(100.0, e.parameters.value));
		}
		else
		{
			if (double.IsNaN(e.parameters.value)) e.parameters.value = 0;
			e.parameters.value = Math.Max(0.0, e.parameters.value); // flat non-negative
		}

		// durations & cooldowns
		if (e.parameters.duration.HasValue && e.parameters.duration.Value < 0) e.parameters.duration = null;
		if (e.parameters.cooldown.HasValue && e.parameters.cooldown.Value < 0) e.parameters.cooldown = null;

		// stack defaults
		if (string.IsNullOrWhiteSpace(e.parameters.stackType)) e.parameters.stackType = "none";
		if (e.parameters.maxStacks < 0) e.parameters.maxStacks = 0;

		// effect-specific bounds (examples; tune to your game)
		switch (e.effectType)
		{
			case "PhysicalDamage":
				if (e.parameters.valueType == "percent")
					e.parameters.value = Math.Min(e.parameters.value, 50.0); // up to +50% physical damage
				else
					e.parameters.value = Math.Min(e.parameters.value, 2000.0);
				break;

			case "ElementalDamage":
				if (e.parameters.valueType == "percent")
					e.parameters.value = Math.Min(e.parameters.value, 50.0);
				break;

			case "DamageOverTime":
				e.parameters.value = Math.Min(e.parameters.value, 40.0);
				if (e.parameters.duration.HasValue)
					e.parameters.duration = Math.Max(0.5, Math.Min(60.0, e.parameters.duration.Value));
				break;

			case "CriticalChance":
				e.parameters.value = Math.Min(e.parameters.value, 50.0); // +crit chance
				break;

			case "CriticalDamage":
				e.parameters.value = Math.Min(e.parameters.value, 200.0); // +crit damage (percent)
				break;

			case "Pierce":
				e.parameters.piercePercent = Math.Max(0.0, Math.Min(100.0, e.parameters.piercePercent ?? 0.0));
				break;

			case "SplashDamage":
				e.parameters.radius = Math.Max(0.0, e.parameters.radius ?? 0.0);
				if (e.parameters.valueType == "percent") e.parameters.value = Math.Min(e.parameters.value, 80.0);
				break;

			case "ReflectDamage":
				e.parameters.value = Math.Min(e.parameters.value, 50.0); // reflect up to 50%
				break;

			case "TrueDamage":
				e.parameters.value = Math.Min(e.parameters.value, 100.0);
				break;

			case "SplashOnDeath":
				e.parameters.radius = Math.Max(0.0, e.parameters.radius ?? 2.0);
				break;

			case "DamageReduction":
				e.parameters.value = Math.Min(e.parameters.value, 80.0); // up to 80% DR
				break;

			case "ArmorBoost":
			case "ResistanceBoost":
				e.parameters.value = Math.Min(e.parameters.value, 200.0); // flat or percent
				break;

			case "ShieldOnHit":
				if (!e.parameters.duration.HasValue) e.parameters.duration = 3.0;
				break;

			case "BlockChance":
			case "DodgeChance":
				e.parameters.value = Math.Min(e.parameters.value, 75.0);
				break;

			case "HealthRegen":
			case "HealOverTime":
				e.parameters.value = Math.Min(e.parameters.value, 50.0);
				break;

			case "LifeLeech":
				e.parameters.value = Math.Min(e.parameters.value, 50.0);
				break;

			case "MaxHealth":
				e.parameters.value = Math.Min(e.parameters.value, 100000.0);
				break;

			case "Revive":
				if (!e.parameters.cooldown.HasValue) e.parameters.cooldown = 999.0; // fallback high cooldown
				e.parameters.chance = Math.Max(0.0, Math.Min(1.0, e.parameters.chance));
				break;

			case "InstantHeal":
				e.parameters.value = Math.Min(e.parameters.value, 100000.0);
				break;

			case "ManaRegen":
				e.parameters.value = Math.Min(e.parameters.value, 100.0);
				break;

			case "ResourceOnKill":
				e.parameters.value = Math.Min(e.parameters.value, 10000.0);
				break;

			case "CooldownRefund":
				e.parameters.value = Math.Min(e.parameters.value, 100.0);
				break;

			case "StatusCure":
				// mostly boolean-like: treat chance
				e.parameters.chance = Math.Max(0.0, Math.Min(1.0, e.parameters.chance));
				break;

			case "Slow":
			case "Stun":
			case "Root":
			case "Silence":
			case "Fear":
			case "Disarm":
			case "Knockback":
			case "Taunt":
				if (e.parameters.duration.HasValue)
					e.parameters.duration = Math.Max(0.1, Math.Min(10.0, e.parameters.duration.Value));
				e.parameters.chance = Math.Max(0.0, Math.Min(1.0, e.parameters.chance));
				break;

			case "AttackSpeed":
			case "MovementSpeed":
			case "RangeIncrease":
				if (e.parameters.valueType == "percent") e.parameters.value = Math.Min(e.parameters.value, 100.0);
				break;

			case "Accuracy":
			case "CriticalResist":
			case "StatBoost":
				e.parameters.value = Math.Min(e.parameters.value, 100.0);
				break;

			default:
				// unknown effect - should not happen since we validated allowed names
				break;
		}

		return true;
	}

	// -----------------------
	// Inversion logic
	// -----------------------

	private static bool MaybeInvertPassives(List<PassiveDefinition> passives, string characterId)
	{
		if (passives == null || passives.Count == 0) return false;

		var rng = new System.Random();
		double roll = rng.NextDouble();
		if (roll >= inversionChance) return false;

		string currentElement = CharacterStore.GetElementForCharacter(characterId);
		if (string.IsNullOrEmpty(currentElement)) currentElement = passives[0].element ?? "No-Attribute";

		string opposite = OppositeElementMap.ContainsKey(currentElement) ? OppositeElementMap[currentElement] : "No-Attribute";

		for (int i = 0; i < passives.Count; i++)
		{
			passives[i] = InvertPassive(passives[i], opposite);
		}

		CharacterStore.SetElementForCharacter(characterId, opposite);
		Debug.Log($"[PassiveGenerator] Passive inversion occurred for {characterId}: element changed {currentElement} -> {opposite}");
		return true;
	}

	private static PassiveDefinition InvertPassive(PassiveDefinition orig, string newElement)
	{
		var p = new PassiveDefinition
		{
			id = orig.id ?? Guid.NewGuid().ToString(),
			name = orig.name,
			description = orig.description,
			element = newElement,
			trigger = orig.trigger,
			target = orig.target,
			effects = new List<EffectInstance>(),
			scaling = orig.scaling ?? new Scaling { stat = "none", ratio = 0.0 },
			balance = orig.balance ?? new Balance { min_value = 0, max_value = 0, notes = "" },
			notes = orig.notes
		};

		// Transform each effect: try to invert offensive -> supportive and vice-versa.
		foreach (var e in orig.effects)
		{
			var inv = new EffectInstance
			{
				effectType = e.effectType,
				parameters = e.parameters != null ? JsonConvert.DeserializeObject<EffectParameters>(JsonConvert.SerializeObject(e.parameters)) : new EffectParameters()
			};

			// Simple heuristic inversions: (examples)
			// - DamageOverTime -> HealOverTime
			// - PhysicalDamage/ElementalDamage/TrueDamage -> InstantHeal or LifeLeech
			// - DamageReduction -> AttackSpeed? (we'll convert damage-reduction to Healing over time)
			// - Crowd control -> keep same (or leave) — this is game-design dependent
			switch (e.effectType)
			{
				case "DamageOverTime":
					inv.effectType = "HealOverTime";
					// keep value and duration but ensure percent semantics
					inv.parameters.valueType = e.parameters.valueType;
					inv.parameters.value = e.parameters.value;
					break;

				case "PhysicalDamage":
				case "ElementalDamage":
				case "TrueDamage":
					inv.effectType = "InstantHeal";
					inv.parameters.valueType = "percent";
					inv.parameters.value = e.parameters.value; // same magnitude
					inv.parameters.duration = null;
					break;

				case "LifeLeech":
					// life leech stays but we could invert to DamageOverTime on enemy? For safety, keep it
					inv.effectType = "LifeLeech";
					break;

				case "DamageReduction":
					inv.effectType = "HealthRegen";
					inv.parameters.valueType = "percent";
					inv.parameters.value = Math.Min(e.parameters.value, 50.0);
					break;

				case "HealOverTime":
				case "InstantHeal":
					// invert heal -> small damage over time
					inv.effectType = "DamageOverTime";
					inv.parameters.valueType = "percent";
					inv.parameters.value = e.parameters.value;
					break;

				case "SplashDamage":
					inv.effectType = "HealOverTime";
					break;

				default:
					// default: for many effects, keep them but change element
					inv.effectType = e.effectType;
					break;
			}

			// ensure parameters are within bounds for the new type
			ValidateEffectInstance(inv, out _);
			p.effects.Add(inv);
		}

		// update descriptive fields
		p.name = $"Inverted {orig.name}";
		p.description = $"(Inverted) {orig.description} Element is now {newElement}.";
		p.notes = string.IsNullOrEmpty(p.notes) ? $"Inverted to {newElement}" : p.notes + $" Inverted to {newElement}";

		// Force element and final validate
		p.element = newElement;
		ValidateAndFixPassive(p, newElement, out _);

		return p;
	}

   
}
