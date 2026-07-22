using System.Net;

namespace ProxmoxIpMonitor.Abstractions.Exceptions;

/// <summary>Domain failure carrying the status code the API should answer with.</summary>
public class HttpException(HttpStatusCode statusCode, string message) : Exception(message)
{
	public HttpStatusCode StatusCode { get; } = statusCode;

	public static HttpException NotFound(string message)
	{
		return new HttpException(HttpStatusCode.NotFound, message);
	}

	public static HttpException BadRequest(string message)
	{
		return new HttpException(HttpStatusCode.BadRequest, message);
	}

	public static HttpException Conflict(string message)
	{
		return new HttpException(HttpStatusCode.Conflict, message);
	}
}
