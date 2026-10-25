
using Dsw2025Tpi.Api.NewFolder;
using Dsw2025Tpi.Application.Services;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Data.Helper;
using Dsw2025Tpi.Data.Repositories;
using Dsw2025Tpi.Domain.Entities;
using Dsw2025Tpi.Domain.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using System.Text.Json.Serialization;

namespace Dsw2025Tpi.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configuración de la conexión a la base de datos
        var connectionString = builder.Configuration["DB_CONNECTION_STRING"]
            ?? builder.Configuration.GetConnectionString("Dsw2025TpiEntities")
            ?? builder.Configuration.GetConnectionString("DB_CONNECTION_STRING")
            ?? throw new InvalidOperationException(
                "No se configuró la connection string. Definí la app setting 'DB_CONNECTION_STRING' o 'ConnectionStrings__Dsw2025TpiEntities'.");

        if (!OperatingSystem.IsWindows() &&
            connectionString.Contains("(localdb)", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "La connection string apunta a LocalDB, que no está disponible en este sistema. " +
                "Configurá en el App Service una app setting 'DB_CONNECTION_STRING' con la cadena ADO.NET de tu SQL de Azure.");
        }

        var serverPart = connectionString
            .Split(';')
            .FirstOrDefault(x =>
                x.TrimStart().StartsWith("Server=", StringComparison.OrdinalIgnoreCase) ||
                x.TrimStart().StartsWith("Data Source=", StringComparison.OrdinalIgnoreCase));

        Console.WriteLine($"Conectando a base de datos: {serverPart}");

        builder.Services.AddLogging(config =>
        {
            config.ClearProviders();
            config.AddConsole();

            var path = builder.Configuration.GetValue<string>("LogPath");
            if (!string.IsNullOrEmpty(path))
            {
                config.AddFile(path);
            }
        });

        builder.Services.AddControllers().AddJsonOptions(opt =>
        {
            opt.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
        });

        builder.Services.AddEndpointsApiExplorer();

        builder.Services.AddSwaggerGen(o =>
        {
            o.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Desarollo de software TPI",
                Version = "v1",
            });

            o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "Bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Ingrese el token JWT"
            });

            o.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });
        });

        builder.Services.AddHealthChecks();

        builder.Services.AddDbContext<AuthenticateContext>(options =>
        {
            options.UseSqlServer(connectionString);
        });

        // Configuración de ASP.NET Identity
        builder.Services.AddIdentity<IdentityUser, IdentityRole>(options =>
        {
            options.Password = new PasswordOptions
            {
                RequiredLength = 8
            };
        })
        .AddEntityFrameworkStores<AuthenticateContext>()
        .AddDefaultTokenProviders()
        .AddErrorDescriber<SpanishIdentityErrorDescriber>();

        var jwtConfig = builder.Configuration.GetSection("Jwt");
        var keyText = jwtConfig["Key"]
            ?? throw new ArgumentNullException("JWT Key");

        var key = Encoding.UTF8.GetBytes(keyText);

        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme =
                JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme =
                JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = jwtConfig["Issuer"],
                ValidAudience = jwtConfig["Audience"],
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ClockSkew = TimeSpan.Zero
            };

            options.Events = new JwtBearerEvents
            {
                OnAuthenticationFailed = context =>
                {
                    var logger = context.HttpContext.RequestServices
                        .GetRequiredService<ILogger<Program>>();

                    logger.LogWarning(
                        "Falló la validación de un token JWT. Tipo de excepción: {ExceptionType}",
                        context.Exception.GetType().Name);

                    return Task.CompletedTask;
                }
            };
        });

        builder.Services.AddDbContext<Dsw2025TpiContext>(options =>
        {
            options.UseSqlServer(connectionString);
        });

        builder.Services.AddSingleton<JwtTokenService>();
        builder.Services.AddAuthorization();

        builder.Services.AddScoped<IRepository, EfRepository>();
        builder.Services.AddScoped<ProductsManagementService>();
        builder.Services.AddScoped<OrdersManagementService>();

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowFrontend", policy =>
            {
                policy.WithOrigins("http://localhost:5173")
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            });
        });

        var app = builder.Build();

        // Crear roles y configurar el administrador inicial
        using (var scope = app.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            var logger = services.GetRequiredService<ILogger<Program>>();

            try
            {
                var authContext = services.GetRequiredService<AuthenticateContext>();
                authContext.Database.Migrate();

                var context = services.GetRequiredService<Dsw2025TpiContext>();
                context.Database.Migrate();

                var userManager = services.GetRequiredService<UserManager<IdentityUser>>();
                var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

                foreach (var roleName in new[] { "Admin", "User" })
                {
                    if (!await roleManager.RoleExistsAsync(roleName))
                    {
                        var roleResult = await roleManager.CreateAsync(
                            new IdentityRole(roleName));

                        if (!roleResult.Succeeded)
                        {
                            throw new InvalidOperationException(
                                $"No se pudo crear el rol {roleName}: " +
                                string.Join("; ", roleResult.Errors.Select(e => e.Description)));
                        }

                        logger.LogInformation(
                            "Rol {Role} creado correctamente.",
                            roleName);
                    }
                }

                // Crear el administrador inicial desde la configuración segura
                var adminUsername = builder.Configuration["BootstrapAdmin:Username"];
                var adminEmail = builder.Configuration["BootstrapAdmin:Email"];
                var adminPassword = builder.Configuration["BootstrapAdmin:Password"];

                if (string.IsNullOrWhiteSpace(adminUsername) ||
                    string.IsNullOrWhiteSpace(adminEmail) ||
                    string.IsNullOrWhiteSpace(adminPassword))
                {
                    logger.LogWarning(
                        "No se configuró BootstrapAdmin completo. Se omite la creación del administrador inicial.");
                }
                else
                {
                    var adminUser = await userManager.FindByNameAsync(adminUsername);

                    if (adminUser == null)
                    {
                        adminUser = new IdentityUser
                        {
                            UserName = adminUsername,
                            Email = adminEmail,
                            EmailConfirmed = true
                        };

                        var createResult = await userManager.CreateAsync(
                            adminUser,
                            adminPassword);

                        if (!createResult.Succeeded)
                        {
                            logger.LogError(
                                "No se pudo crear el administrador inicial {Username}: {Errors}",
                                adminUsername,
                                string.Join("; ", createResult.Errors.Select(e => e.Description)));
                        }
                        else
                        {
                            logger.LogInformation(
                                "Administrador inicial {Username} creado correctamente.",
                                adminUsername);
                        }
                    }
                    else
                    {
                        logger.LogInformation(
                            "El administrador inicial {Username} ya existe.",
                            adminUsername);
                    }

                    adminUser = await userManager.FindByNameAsync(adminUsername);

                    if (adminUser != null &&
                        !await userManager.IsInRoleAsync(adminUser, "Admin"))
                    {
                        var assignRoleResult = await userManager.AddToRoleAsync(
                            adminUser,
                            "Admin");

                        if (!assignRoleResult.Succeeded)
                        {
                            logger.LogError(
                                "No se pudo asignar el rol Admin a {Username}: {Errors}",
                                adminUsername,
                                string.Join("; ", assignRoleResult.Errors.Select(e => e.Description)));
                        }
                        else
                        {
                            logger.LogInformation(
                                "Rol Admin asignado a {Username}.",
                                adminUsername);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Falló la inicialización de roles y administradores.");
            }

            // Cargar clientes de manera independiente
            try
            {
                var context = services.GetRequiredService<Dsw2025TpiContext>();
                context.Seedwork<Customer>("customers.json");
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Falló la carga inicial de clientes.");
            }
        }

        // Configuración del pipeline HTTP
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
            app.UseHttpsRedirection();
        }

        app.UseCors("AllowFrontend");
        app.UseMiddleware<ExceptionHandlingMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        app.MapHealthChecks("/healthcheck");

        app.Run();
    }
}
