using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Threading.Tasks;

public class PassivePopupController : MonoBehaviour
{
    [Header("UI References")]
    public TMP_InputField personalityInput;
    public Button generateButton;
    public TextMeshProUGUI statusText;
    public TextMeshProUGUI previewText;

    [Header("Card List UI")]
    public Transform cardsContainer;
    public GameObject cardPrefab;
    public Sprite cardBackground;
    public GameObject CardPanel;

    [Header("Card Style")]
    public Vector2 cardSize = new Vector2(280, 380);

    [Header("Settings")]
    public string characterId = "player_01";
    public int generateCount = 1;
    public int retryAttempts = 8;

    private List<PassiveDefinition> snapshotBeforeGen;
    private List<PassiveDefinition> lastGenerated;

    void Awake()
    {
        if (generateButton != null) generateButton.onClick.AddListener(OnGenerateClicked);
    }

    void Start()
    {
        statusText.text = "Enter a short personality and press Generate.";
        previewText.text = "";
        if (cardsContainer != null) ClearCards();
    }

    async void OnGenerateClicked()
    {
        CardPanel.SetActive(true);
        string personality = personalityInput?.text?.Trim();
        if (string.IsNullOrEmpty(personality))
        {
            statusText.text = "Please enter a brief personality description.";
            return;
        }

        try
        {
            snapshotBeforeGen = CharacterStore.GetPassivesDeepCopy(characterId);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[PassivePopup] Snapshot failed: " + ex);
            snapshotBeforeGen = new List<PassiveDefinition>();
        }

        generateButton.interactable = false;
        statusText.text = "Generating passive... (this may take a few seconds)";
        previewText.text = "";

        try
        {
            lastGenerated = await PassiveGenerator.GenerateWithRetries(characterId, personality, generateCount, retryAttempts);

            if (lastGenerated != null && lastGenerated.Count > 0)
            {
                if (cardsContainer != null)
                {
                    ClearCards();
                    BuildCards(lastGenerated);
                    if (previewText != null) previewText.text = "";
                }
                else if (previewText != null)
                {
                    previewText.text = FormatPreview(lastGenerated);
                }
                statusText.text = "Preview generated. Choose one of the above";
            }
            else
            {
                if (previewText != null) previewText.text = "";
                if (cardsContainer != null) ClearCards();
                statusText.text = "No valid passive generated. Try again.";
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[PassivePopup] Generation error: " + ex);
            statusText.text = "Error during generation. See console for details.";
        }
        finally
        {
            generateButton.interactable = true;
        }
    }

    private void BuildCards(List<PassiveDefinition> passives)
    {
        if (cardsContainer == null || passives == null) return;

        foreach (var p in passives)
        {
            if (p == null) continue;

            GameObject card = null;
            if (cardPrefab != null)
            {
                card = Instantiate(cardPrefab, cardsContainer);
                card.name = $"PassiveCard_{p.name}";
                var le = card.GetComponent<LayoutElement>();
                if (le == null) le = card.AddComponent<LayoutElement>();
                le.preferredWidth = cardSize.x;
                le.preferredHeight = cardSize.y;
                le.minWidth = cardSize.x;
                le.minHeight = cardSize.y;
                le.flexibleWidth = 0;
                le.flexibleHeight = 0;
                var rect = card.GetComponent<RectTransform>();
                if (rect != null)
                {
                    rect.anchorMin = new Vector2(0, 15);
                    rect.anchorMax = new Vector2(0, 20);
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.sizeDelta = cardSize;
                }
                var rt = card.GetComponent<RectTransform>();
                if (rt != null) rt.sizeDelta = cardSize;
            }
            else
            {
                card = CreateRuntimeCard();
                if (card == null) continue;
                card.transform.SetParent(cardsContainer, false);
                card.name = $"PassiveCard_{p.name}";
            }

            var cardText = $"Name: {p.name ?? "Unknown"}    Element: {p.element ?? "Unknown"}\n" +
                           $"{p.description ?? ""}\n" +
                           $"Trigger: {p.trigger ?? "Unknown"}  Target: {p.target ?? "Unknown"}\n" +
                           "Effects:\n";
            if (p.effects != null)
            {
                foreach (var e in p.effects)
                {
                    if (e == null) continue;
                    var val = (e.parameters != null) ? (e.parameters.valueType == "percent" ? $"{e.parameters.value}%" : $"{e.parameters.value}") : "n/a";
                    cardText += $" - {e.effectType}: {val}  dur:{(e.parameters?.duration?.ToString() ?? "null")}  cd:{(e.parameters?.cooldown?.ToString() ?? "null")}  chance:{(e.parameters?.chance.ToString() ?? "n/a")}\n";
                }
            }
            var textComp = AddText(card, cardText, 19, false, true, TextOverflowModes.Ellipsis, null, cardSize.y - 60);
            if (textComp != null)
            {
                var textRT = textComp.GetComponent<RectTransform>();
                if (textRT != null)
                {
                    textRT.anchorMin = new Vector2(0, 0);
                    textRT.anchorMax = new Vector2(1, 1);
                    textRT.offsetMin = new Vector2(8, 8);
                    textRT.offsetMax = new Vector2(-8, -8);
                }
                textComp.alignment = TextAlignmentOptions.TopLeft;
                textComp.enableWordWrapping = true;
                textComp.overflowMode = TextOverflowModes.Ellipsis;
                var textRect = textComp.GetComponent<RectTransform>();
                if (textRect != null)
                {
                    textRect.offsetMin = new Vector2(227, 20);
                    textRect.offsetMax = new Vector2(-227, -110);
                }
            }

            if (cardsContainer != null)
            {
                var hlg = cardsContainer.GetComponent<HorizontalLayoutGroup>();
                if (hlg == null)
                {
                    var vlg = cardsContainer.GetComponent<VerticalLayoutGroup>();
                    if (vlg != null) DestroyImmediate(vlg);

                    hlg = cardsContainer.gameObject.AddComponent<HorizontalLayoutGroup>();
                    hlg.childControlHeight = true;
                    hlg.childForceExpandHeight = false;
                    hlg.childControlWidth = false;
                    hlg.childForceExpandWidth = false;
                    hlg.spacing = 12f;
                    hlg.padding = new RectOffset(8, 8, 8, 8);
                }
            }
        }
    }

    private void ClearCards()
    {
        if (cardsContainer == null) return;
        for (int i = cardsContainer.childCount - 1; i >= 0; i--)
        {
            Destroy(cardsContainer.GetChild(i).gameObject);
        }
    }

    private GameObject CreateRuntimeCard()
    {
        var go = new GameObject("PassiveCard", typeof(RectTransform), typeof(Image), typeof(HorizontalLayoutGroup), typeof(RectMask2D), typeof(LayoutElement));
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = cardSize; 

        var img = go.GetComponent<Image>();
        if (cardBackground != null)
        {
            img.sprite = cardBackground;
            img.type = Image.Type.Sliced;
        }
        img.color = new Color(1f, 1f, 1f, 0.2f);

        var vlg = go.GetComponent<HorizontalLayoutGroup>();
        vlg.childControlHeight = true;
        vlg.childForceExpandHeight = false;
        vlg.childControlWidth = true;
        vlg.childForceExpandWidth = true;
        vlg.spacing = 6f;
        vlg.padding = new RectOffset(12, 12, 12, 12);

        var le = go.GetComponent<LayoutElement>();
        le.preferredWidth = cardSize.x;
        le.preferredHeight = cardSize.y;
        le.minWidth = cardSize.x;
        le.minHeight = cardSize.y;
        le.flexibleWidth = 0;
        le.flexibleHeight = 0;

        return go;
    }

    private TextMeshProUGUI AddText(
        GameObject parent,
        string text,
        int fontSize = 18,
        bool bold = false,
        bool wrap = true,
        TextOverflowModes overflow = TextOverflowModes.Masking,
        int? maxLines = null,
        float minHeight = 20f)
    {
        var child = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI), typeof(LayoutElement));
        child.transform.SetParent(parent.transform, false);

        var rt = child.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var tmp = child.GetComponent<TextMeshProUGUI>();
        tmp.text = text ?? "";
        tmp.fontSize = fontSize;
        tmp.enableWordWrapping = wrap;
        tmp.overflowMode = overflow;
        tmp.richText = true;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        if (bold) tmp.fontStyle |= FontStyles.Bold;
        if (maxLines.HasValue && maxLines.Value > 0) tmp.maxVisibleLines = maxLines.Value;

        var le = child.GetComponent<LayoutElement>();
        le.minHeight = minHeight;

        return tmp;
    }

    private string FormatPreview(List<PassiveDefinition> passives)
    {
        var s = new System.Text.StringBuilder();
        foreach (var p in passives)
        {
            s.AppendLine($"Name: {p.name}    Element: {p.element}");
            s.AppendLine($"{p.description}");
            s.AppendLine($"Trigger: {p.trigger}  Target: {p.target}");
            s.AppendLine("Effects:");
            if (p.effects != null)
            {
                foreach (var e in p.effects)
                {
                    var val = e.parameters != null ? (e.parameters.valueType == "percent" ? $"{e.parameters.value}%" : $"{e.parameters.value}") : "n/a";
                    s.AppendLine($" - {e.effectType}: {val}  dur:{(e.parameters?.duration?.ToString() ?? "null")}  cd:{(e.parameters?.cooldown?.ToString() ?? "null")}  chance:{(e.parameters?.chance.ToString() ?? "n/a")}");
                }
            }
            s.AppendLine(new string('-', 40));
        }
        return s.ToString();
    }
}
