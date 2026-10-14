using Application.Abstractions;
using Infrastructure.Authentication;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(
            this IServiceCollection services,
            IConfiguration configuration
        )
        {
            var connectionString =
                configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' was not found.");

            services.AddDbContext<ApplicationDbContext>(
                options =>
                {
                    options.UseNpgsql(connectionString);
                }
            );

            services.AddScoped<IApplicationDbContext>(provider => provider.GetRequiredService<ApplicationDbContext>());

            services
                .AddOptions<SecurityOptions>()
                .Configure(
                    options =>
                    {
                        var section = configuration.GetSection(SecurityOptions.SectionName);
                        options.Issuer = section["Issuer"] ?? options.Issuer;
                        options.EncryptionKey = section["EncryptionKey"] ?? string.Empty;
                        options.OtpHashKey = section["OtpHashKey"] ?? string.Empty;
                        options.ChallengeLifetimeMinutes = int.TryParse(
                            section["ChallengeLifetimeMinutes"],
                            out var lifetime
                        )
                            ? lifetime
                            : options.ChallengeLifetimeMinutes;
                    }
                )
                .Validate(
                    options =>
                        IsBase64Key(
                            options.EncryptionKey,
                            32
                        ),
                    "Security:EncryptionKey must be a base64-encoded 32-byte key."
                )
                .Validate(
                    options =>
                        IsBase64Key(
                            options.OtpHashKey,
                            32,
                            minimumLength: true
                        ),
                    "Security:OtpHashKey must be a base64-encoded key of at least 32 bytes."
                )
                .ValidateOnStart();
            services.Configure<SmtpEmailOptions>(
                options =>
                {
                    var section = configuration.GetSection(SmtpEmailOptions.SectionName);
                    options.Enabled = bool.TryParse(
                        section["Enabled"],
                        out var enabled
                    )
                        && enabled;
                    options.Host = section["Host"] ?? string.Empty;
                    options.Port = int.TryParse(
                        section["Port"],
                        out var port
                    )
                        ? port
                        : 587;
                    options.UseSsl = !bool.TryParse(
                        section["UseSsl"],
                        out var useSsl
                    )
                        || useSsl;
                    options.Username = section["Username"] ?? string.Empty;
                    options.Password = section["Password"] ?? string.Empty;
                    options.FromAddress = section["FromAddress"] ?? string.Empty;
                    options.FromName = section["FromName"] ?? options.FromName;
                }
            );

            services.AddSingleton<AesSecretProtector>();
            services.AddScoped<IEmailSender, SmtpEmailSender>();
            services.AddScoped<ISecondFactorService, SecondFactorService>();
            services.AddScoped<ITokenService, TokenService>();
            services.AddScoped<IIdentityService, IdentityService>();

            return services;
        }

        private static bool IsBase64Key(
            string value,
            int length,
            bool minimumLength = false
        )
        {
            try
            {
                var bytes = Convert.FromBase64String(value);
                return minimumLength
                    ? bytes.Length >= length
                    : bytes.Length == length;
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}
