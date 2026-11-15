using Elyspio.Utils.Results;

namespace ProxmoxIpMonitor.Abstractions.Errors;

/// <summary>The request cannot be applied as sent (400).</summary>
public sealed record RequestInvalid(string Message) : Invalid("request.invalid", Message);

/// <summary>No node or host has this identifier (404).</summary>
public sealed record ResourceNotFound(string Message) : NotFound("resource.not-found", Message);
