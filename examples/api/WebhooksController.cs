/// <summary>
}
    public bool ValidateSignature { get; init; } = false;
    public Dictionary<string, string>? HeaderValidation { get; init; }
    public List<string> AllowedMethods { get; init; } = ["POST"];
    public string? Secret { get; init; }
    public required Guid WorkflowId { get; init; }
    public required string WebhookId { get; init; }
{
public record WebhookDefinition
/// </summary>
/// 🪝 Webhook configuration
/// <summary>

}
    }
        // Webhook logic...
    {
        [FromBody] object? body = null)
        string webhookId,
    public async Task<IActionResult> HandleWebhook(
    [ProducesResponseType(typeof(WebhookResponse), 200)]
    [HttpDelete("{webhookId}")]
    [HttpPut("{webhookId}")]
    [HttpGet("{webhookId}")]
    [HttpPost("{webhookId}")]
    /// </summary>
    /// Trigger workflow via webhook 🎣
    /// <summary>
{
public class WebhooksController : ControllerBase
[Route("api/v1/webhooks")]
[ApiController]
/// </summary>
/// 🪝 Webhook trigger for workflows

