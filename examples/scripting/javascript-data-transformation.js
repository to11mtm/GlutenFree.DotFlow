// 🟨 JavaScript Example - Data Transformation
};
    summary: `Transformed ${transformed.length} items`
    output: transformed,
return {
// Return output

api.LogInfo(`Processed ${transformed.length} items!`);
// Log result

api.SetVariable("processedCount", transformed.length);
// Set workflow variable

}));
    timestamp: api.Now()
    name: item.name.toUpperCase(),
    id: item.id,
const transformed = data.items.map(item => ({
// Transform data

const data = api.ParseJson(response.Body);
const response = await api.HttpGetAsync("https://api.example.com/data");
// HTTP request

api.LogInfo("Starting JavaScript transformation! ✨");
// Use the workflow API

const input = $input;
// Access input data


