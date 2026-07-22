using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;

namespace ProxmoxIpMonitor.Web.Auth;

public static class AuthModule
{
	public const string AdminPolicy = "Admin";

	/// <summary>Keycloak nests realm roles under this claim rather than emitting plain role claims.</summary>
	private const string RealmAccessClaim = "realm_access";

	public static IServiceCollection AddAppAuth(this IServiceCollection services, IConfiguration config)
	{
		var auth = new AuthOptions();
		config.GetSection(AuthOptions.SectionName).Bind(auth);

		services.AddOptions<AuthOptions>()
			.Bind(config.GetSection(AuthOptions.SectionName))
			.ValidateDataAnnotations()
			.Validate(options => IsHttpAuthority(options.Authority), "Auth:Authority must be an absolute HTTP(S) URL.")
			.ValidateOnStart();

		services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
			.AddJwtBearer(options =>
			{
				options.Authority = auth.Authority;
				options.Audience = auth.Audience;
				// Local development runs Keycloak over http; deployed authorities are https.
				options.RequireHttpsMetadata = auth.Authority.StartsWith("https", StringComparison.OrdinalIgnoreCase);
				options.TokenValidationParameters.NameClaimType = "name";
				options.Events = new JwtBearerEvents
				{
					OnTokenValidated = context =>
					{
						FlattenRealmRoles(context.Principal);
						return Task.CompletedTask;
					}
				};
			});

		services.AddAuthorizationBuilder()
			.AddPolicy(AdminPolicy, policy => policy.RequireAuthenticatedUser().RequireRole(auth.AdminRole))
			// Applied to every endpoint that does not opt out, so adding a controller cannot
			// accidentally expose an unauthenticated route.
			.SetDefaultPolicy(new AuthorizationPolicyBuilder()
				.RequireAuthenticatedUser()
				.RequireRole(auth.AdminRole)
				.Build());

		return services;
	}

	/// <summary>
	///     Lifts Keycloak's realm_access.roles array into standard role claims, so
	///     RequireRole and User.IsInRole work without every call site parsing JSON.
	/// </summary>
	private static void FlattenRealmRoles(ClaimsPrincipal? principal)
	{
		if (principal?.Identity is not ClaimsIdentity identity) return;

		var realmAccess = principal.FindFirst(RealmAccessClaim)?.Value;
		if (string.IsNullOrWhiteSpace(realmAccess)) return;

		try
		{
			using var document = JsonDocument.Parse(realmAccess);
			if (!document.RootElement.TryGetProperty("roles", out var roles) || roles.ValueKind != JsonValueKind.Array)
				return;

			foreach (var role in roles.EnumerateArray())
			{
				var value = role.GetString();
				if (!string.IsNullOrWhiteSpace(value)) identity.AddClaim(new Claim(identity.RoleClaimType, value));
			}
		}
		catch (JsonException)
		{
			// A malformed claim means no roles, which the policy then rejects. Nothing to log:
			// the token came from outside and its shape is not ours to trust.
		}
	}

	private static bool IsHttpAuthority(string authority)
	{
		return Uri.TryCreate(authority, UriKind.Absolute, out var uri)
		       && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
	}
}
