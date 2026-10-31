using InventoryHold.Contracts;
using InventoryHold.Domain.Services;
using Microsoft.AspNetCore.Mvc;

namespace InventoryHold.WebApi.Controllers;

/// <summary>
/// Hold lifecycle endpoints. Business-rule failures surface as domain exceptions and are turned
/// into RFC 7807 problem responses by <see cref="ErrorHandling.DomainExceptionHandler"/>.
/// </summary>
[ApiController]
[Route("api/holds")]
[Produces("application/json")]
public sealed class HoldsController(IHoldService holds) : ControllerBase
{
    /// <summary>Reserves stock for every item atomically and creates a hold.</summary>
    [HttpPost]
    [ProducesResponseType<HoldResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<HoldResponse>> Create([FromBody] CreateHoldRequest request, CancellationToken ct)
    {
        var hold = await holds.CreateAsync(request, ct);
        return CreatedAtAction(nameof(Get), new { holdId = hold.Id }, hold);
    }

    /// <summary>Lists holds, newest first. Overdue holds are reported (and processed) as expired.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<HoldResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IReadOnlyList<HoldResponse>>> List(
        [FromQuery] HoldStatus? status,
        [FromQuery] int limit = 50,
        CancellationToken ct = default) =>
        Ok(await holds.ListAsync(status, limit, ct));

    /// <summary>Gets a hold. An overdue hold is expired on read and returned with status Expired.</summary>
    [HttpGet("{holdId}")]
    [ProducesResponseType<HoldResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<HoldResponse>> Get(string holdId, CancellationToken ct) =>
        Ok(await holds.GetAsync(holdId, ct));

    /// <summary>Releases an active hold and returns its stock.</summary>
    /// <response code="200">Released; body is the closed hold.</response>
    /// <response code="404">No such hold.</response>
    /// <response code="409">Hold was already released or expired.</response>
    /// <response code="410">Hold had passed its expiry time; it was expired and its stock returned.</response>
    [HttpDelete("{holdId}")]
    [ProducesResponseType<HoldResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone)]
    public async Task<ActionResult<HoldResponse>> Release(string holdId, CancellationToken ct) =>
        Ok(await holds.ReleaseAsync(holdId, ct));
}
