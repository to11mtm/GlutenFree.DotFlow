// 🌐 JavaScript Example - API Integration

// Get configuration from variables
const apiKey = api.GetVariable("apiKey");
const endpoint = api.GetVariable("endpoint");

// Make authenticated request
const response = await api.HttpRequestAsync({
    Url: endpoint,
    Method: "POST",
    Headers: {
        "Authorization": `Bearer ${apiKey}`,
        "Content-Type": "application/json"
    },
    Body: $input,
    TimeoutSeconds: 60
});

if (response.IsSuccess) {
    api.LogInfo("✅ Request successful!");
    return { output: api.ParseJson(response.Body) };
} else {
    api.LogError(`❌ Request failed with status ${response.StatusCode}`);
    throw new Error(`HTTP ${response.StatusCode}`);
}

