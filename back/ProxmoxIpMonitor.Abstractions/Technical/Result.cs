using System.Diagnostics.CodeAnalysis;

namespace ProxmoxIpMonitor.Abstractions.Technical;

/// <summary>
///     Carries either a value or the failure that prevented producing it.
///     Kept from the original exporter: collection must degrade per host rather than throw.
/// </summary>
public class Result<T>
{
	private Result(T? data, Exception? error = null)
	{
		if (error is not null)
		{
			Success = false;
			Error = error;
		}
		else
		{
			Success = true;
			Data = data;
		}
	}

	[MemberNotNullWhen(true, nameof(Data))]
	[MemberNotNullWhen(false, nameof(Error))]
	public bool Success { get; set; }

	public T? Data { get; set; }

	public Exception? Error { get; set; }

	public static implicit operator Result<T>(T data)
	{
		return new Result<T>(data);
	}

	public static implicit operator Result<T>(Exception err)
	{
		return new Result<T>(default, err);
	}

	public static implicit operator T(Result<T> result)
	{
		return !result.Success ? throw new InvalidOperationException("Cannot get Data from a failed Result.", result.Error) : result.Data!;
	}
}
