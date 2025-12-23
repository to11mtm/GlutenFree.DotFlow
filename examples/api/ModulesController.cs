/// <summary>
/// 📦 Module management endpoints
/// </summary>
[ApiController]
[Route("api/v1/[controller]")]
public class ModulesController : ControllerBase
{
    /// <summary>
    /// Get all available modules 📋
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<ModuleInfo>), 200)]
    public async Task<IActionResult> GetModules([FromQuery] string? category = null);
    
    /// <summary>
    /// Get module details 🔍
    /// </summary>
    [HttpGet("{moduleId}")]
    [ProducesResponseType(typeof(ModuleDetails), 200)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> GetModule(string moduleId);
    
    /// <summary>
    /// Upload a custom module package 📤
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ModuleUploadResponse), 201)]
    [ProducesResponseType(typeof(ValidationProblemDetails), 400)]
    public async Task<IActionResult> UploadModule([FromForm] IFormFile package);
    
    /// <summary>
    /// Delete a custom module 🗑️
    /// </summary>
    [HttpDelete("{packageId}")]
    [ProducesResponseType(204)]
    [ProducesResponseType(404)]
    public async Task<IActionResult> DeleteModule(string packageId);
}

