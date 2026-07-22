using Elyspio.Utils.Telemetry.Technical.Helpers;
using Elyspio.Utils.Telemetry.Tracing.Elements;
using Microsoft.AspNetCore.Mvc;
using ProxmoxIpMonitor.Abstractions.Interfaces;
using ProxmoxIpMonitor.Abstractions.Transports;

namespace ProxmoxIpMonitor.Web.Controllers;

[ApiController]
[Route("api/nodes")]
public sealed class NodesController(INodeService nodes, ILogger<NodesController> logger) : TracingController(logger)
{
	[HttpGet]
	public async Task<IReadOnlyList<NodeDto>> GetAll(CancellationToken ct)
	{
		using var trace = LogController();

		return await nodes.GetAllAsync(ct);
	}

	[HttpPost]
	public async Task<NodeDto> Create([FromBody] NodeWriteDto body, CancellationToken ct)
	{
		// The body carries the raw token secret: only safe fields go through Log.F.
		using var trace = LogController($"{Log.F(body.DisplayName)}");

		return await nodes.CreateAsync(body, ct);
	}

	[HttpPut("{id}")]
	public async Task<NodeDto> Update(string id, [FromBody] NodeWriteDto body, CancellationToken ct)
	{
		using var trace = LogController($"{Log.F(id)} {Log.F(body.DisplayName)}");

		return await nodes.UpdateAsync(id, body, ct);
	}

	[HttpDelete("{id}")]
	public async Task<IActionResult> Delete(string id, CancellationToken ct)
	{
		using var trace = LogController($"{Log.F(id)}");

		await nodes.DeleteAsync(id, ct);
		return NoContent();
	}

	/// <summary>
	///     Verifies reachability, TLS and token before the operator commits a node.
	///     Accepts an unsaved node so the settings form can test what is on screen.
	/// </summary>
	[HttpPost("test")]
	public async Task<NodeTestResultDto> Test([FromBody] NodeWriteDto body, [FromQuery] string? id, CancellationToken ct)
	{
		using var trace = LogController($"{Log.F(id)} {Log.F(body.DisplayName)}");

		return await nodes.TestAsync(body, id, ct);
	}
}
