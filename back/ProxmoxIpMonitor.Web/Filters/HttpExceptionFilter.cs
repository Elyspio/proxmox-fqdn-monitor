using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ProxmoxIpMonitor.Abstractions.Exceptions;

namespace ProxmoxIpMonitor.Web.Filters;

/// <summary>Maps domain failures to their status code, so controllers can just throw.</summary>
public sealed class HttpExceptionFilter : IExceptionFilter
{
	public void OnException(ExceptionContext context)
	{
		if (context.Exception is not HttpException exception) return;

		context.Result = new ObjectResult(new ProblemDetails
		{
			Status = (int)exception.StatusCode,
			Title = exception.Message
		})
		{
			StatusCode = (int)exception.StatusCode
		};

		context.ExceptionHandled = true;
	}
}
