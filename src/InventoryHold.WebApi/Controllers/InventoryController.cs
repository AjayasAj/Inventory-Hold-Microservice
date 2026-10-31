using InventoryHold.Contracts;
using InventoryHold.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace InventoryHold.WebApi.Controllers;

[ApiController]
[Route("api/inventory")]
[Produces("application/json")]
public sealed class InventoryController(IInventoryService inventory) : ControllerBase
{
    /// <summary>Current available stock per product (served from Redis, invalidated on every stock change).</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<InventoryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<InventoryResponse>>> Get(CancellationToken ct) =>
        Ok(await inventory.GetInventoryAsync(ct));
}
