using System.Diagnostics.CodeAnalysis;

namespace ProxmoxIpMonitor.Abstractions.Technical;

/// <summary>
///     Carries either a value or the failure that prevented producing it.
///     Kept from the original exporter: collection must degrade per host rather than throw.
///     Business errors of the API use the Result of Elyspio.Utils.Results instead.
/// </summary>
public class Attempt<T>
{
	private Attempt(T? data, Exception? error = null)
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

	public static implicit operator Attempt<T>(T data)
	{
		return new Attempt<T>(data);
	}

	public static implicit operator Attempt<T>(Exception err)
	{
		return new Attempt<T>(default, err);
	}

	public static implicit operator T(Attempt<T> result)
	{
		return !result.Success ? throw new InvalidOperationException("Cannot get Data from a failed Attempt.", result.Error) : result.Data!;
	}
}
