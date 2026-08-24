using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using PTS.SharedKernel.Identity;

namespace PTS.Host.Authentication;

public static class AuthenticationExtensions
{
    public static IServiceCollection AddPtsAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(JwtAuthenticationOptions.SectionName);
        var options = new JwtAuthenticationOptions
        {
            Issuer = section["Issuer"] ?? "pts",
            Audience = section["Audience"] ?? "pts-api",
            SigningKey = ResolveSigningKey(section["SigningKey"]),
            AccessTokenLifetimeMinutes = int.TryParse(section["AccessTokenLifetimeMinutes"], out var minutes)
                ? minutes
                : 60,
        };

        if (Encoding.UTF8.GetByteCount(options.SigningKey) < 32)
        {
            throw new InvalidOperationException(
                "Authentication:Jwt:SigningKey must be at least 32 bytes (256 bits) for HMAC-SHA256.");
        }

        services.AddSingleton(options);
        services.AddSingleton<JwtAccessTokenIssuer>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = options.Issuer,
                    ValidAudience = options.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = HttpContextCurrentUser.UserIdClaimType,
                };
            });

        services.AddAuthorization();
        return services;
    }

    private static string ResolveSigningKey(string? configured)
    {
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var fromEnv = Environment.GetEnvironmentVariable("PTS_JWT_SIGNING_KEY");
        if (!string.IsNullOrWhiteSpace(fromEnv))
        {
            return fromEnv;
        }

        throw new InvalidOperationException(
            "JWT signing key is not configured. Set Authentication:Jwt:SigningKey or PTS_JWT_SIGNING_KEY.");
    }
}
