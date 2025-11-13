using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class OllamaNetwork
{
    public const string ApiChatUrl = "http://localhost:11434/api/chat";
    public const string ApiModelsUrl = "http://localhost:11434/api/models";
    private static string _defaultModel = "mistral";
    public static void SetDefaultModel(string model) => _defaultModel = model ?? _defaultModel;
    public static string GetDefaultModel() => _defaultModel;
    private static double _defaultTemperature = 0.0;
    private static double _defaultTopP = 0.1;
    private static int _defaultMaxTokens = 1224;
    public static void SetDefaultSampling(double temperature, double topP, int maxTokens)
    {
        _defaultTemperature = Math.Max(0.0, Math.Min(2.0, temperature));
        _defaultTopP = Math.Max(0.0, Math.Min(1.0, topP));
        _defaultMaxTokens = Math.Max(16, maxTokens);
    }
    [Serializable]
    public class ChatMessage { public string role; public string content; }
    [Serializable]
    public class ChatRequestBody
    {
        public string model;
        public List<ChatMessage> messages;
        public double temperature;
        public double top_p;
        public int max_tokens;
        public bool stream = false;
    }
    [Serializable]
    public class ChatResponseBody
    {
        public ChatMessage message;
    }
    public static async Task<string> SendAndGetAssistantTextAsync(
        List<ChatMessage> messages,
        string model = null,
        double? temperature = null,
        double? topP = null,
        int? maxTokens = null)
    {
        model ??= _defaultModel;
        temperature ??= _defaultTemperature;
        topP ??= _defaultTopP;
        maxTokens ??= _defaultMaxTokens;
        var requestBody = new ChatRequestBody
        {
            model = model,
            messages = messages,
            temperature = temperature.Value,
            top_p = topP.Value,
            max_tokens = maxTokens.Value,
            stream = false
        };
        string json = JsonConvert.SerializeObject(requestBody, Formatting.None);
        Debug.Log($"[OllamaNetwork] Sending request (model={model}, temp={temperature}, top_p={topP}, max_tokens={maxTokens}) body:\n{json}");
        using var request = new UnityWebRequest(ApiChatUrl, "POST");
        byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
        request.uploadHandler = new UploadHandlerRaw(bodyRaw);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        var op = request.SendWebRequest();
        var tcs = new TaskCompletionSource<bool>();
        op.completed += _ => tcs.TrySetResult(true);
        await tcs.Task;
        if (request.result != UnityWebRequest.Result.Success)
        {
            string respText = request.downloadHandler?.text ?? "";
            Debug.LogError($"[OllamaNetwork] Request failed: {request.error}\nResponse: {respText}");
            if (request.responseCode == 400 && respText.IndexOf("model", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                string[] fallbacks = new[] { "mistral-instruct", "mistral-7b", "mistral-7b-instruct", "mistral-v1", "mistral" };
                foreach (var fb in fallbacks)
                {
                    if (string.Equals(fb, model, StringComparison.OrdinalIgnoreCase)) continue;
                    Debug.LogWarning($"[OllamaNetwork] Server error suggests model required; retrying with fallback model '{fb}'...");
                    var retryBody = new ChatRequestBody { model = fb, messages = messages, temperature = temperature.Value, top_p = topP.Value, max_tokens = maxTokens.Value, stream = false };
                    string retryJson = JsonConvert.SerializeObject(retryBody, Formatting.None);
                    using var retryReq = new UnityWebRequest(ApiChatUrl, "POST");
                    byte[] retryRaw = Encoding.UTF8.GetBytes(retryJson);
                    retryReq.uploadHandler = new UploadHandlerRaw(retryRaw);
                    retryReq.downloadHandler = new DownloadHandlerBuffer();
                    retryReq.SetRequestHeader("Content-Type", "application/json");
                    var op2 = retryReq.SendWebRequest();
                    var tcs2 = new TaskCompletionSource<bool>();
                    op2.completed += _ => tcs2.TrySetResult(true);
                    await tcs2.Task;
                    if (retryReq.result == UnityWebRequest.Result.Success)
                    {
                        try
                        {
                            var resp = JsonConvert.DeserializeObject<ChatResponseBody>(retryReq.downloadHandler.text);
                            return resp?.message?.content;
                        }
                        catch (Exception ex)
                        {
                            Debug.LogError($"[OllamaNetwork] Failed to parse retry response JSON: {ex.Message}\nRaw: {retryReq.downloadHandler.text}");
                            return null;
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"[OllamaNetwork] Retry with model '{fb}' failed: {retryReq.error}\nResponse: {retryReq.downloadHandler?.text}");
                    }
                }
            }
            return null;
        }
        try
        {
            var resp = JsonConvert.DeserializeObject<ChatResponseBody>(request.downloadHandler.text);
            if (resp?.message?.content != null)
                return resp.message.content;
            var j = JToken.Parse(request.downloadHandler.text);
            var msg = j.SelectToken("message.content") ?? j.SelectToken("choices[0].message.content") ?? j.SelectToken("choices[0].text") ?? j.SelectToken("output[0].content[0].text");
            if (msg != null) return msg.ToString();
            Debug.LogWarning($"[OllamaNetwork] Response parsed but no 'message.content' found. Raw: {request.downloadHandler.text}");
            return request.downloadHandler.text;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[OllamaNetwork] Failed to parse response JSON: {ex.Message}\nRaw: {request.downloadHandler.text}");
            return null;
        }
    }
    public static async Task<List<string>> GetAvailableModelsAsync()
    {
        using var req = UnityWebRequest.Get(ApiModelsUrl);
        req.SetRequestHeader("Content-Type", "application/json");
        var op = req.SendWebRequest();
        var tcs = new TaskCompletionSource<bool>();
        op.completed += _ => tcs.TrySetResult(true);
        await tcs.Task;
        var list = new List<string>();
        if (req.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError($"[OllamaNetwork] Failed to list models: {req.error}\nResponse: {req.downloadHandler?.text}");
            return list;
        }
        try
        {
            var token = JToken.Parse(req.downloadHandler.text);
            if (token.Type == JTokenType.Array)
            {
                foreach (var it in token)
                {
                    if (it.Type == JTokenType.String)
                        list.Add(it.ToString());
                    else if (it["name"] != null)
                        list.Add(it["name"].ToString());
                    else if (it["model"] != null)
                        list.Add(it["model"].ToString());
                    else
                        list.Add(it.ToString());
                }
            }
            else
            {
                if (token["name"] != null) list.Add(token["name"].ToString());
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[OllamaNetwork] Failed to parse models response: {ex.Message}\nRaw: {req.downloadHandler.text}");
        }
        return list;
    }
}
