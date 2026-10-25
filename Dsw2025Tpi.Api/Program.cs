
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
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dsw2025Tpi.Api;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Configuración de logging
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
            opt.JsonSerializerOptions.Converters.Add(
                new JsonStringEnumConverter());
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
            options.UseSqlServer(
                builder.Configuration.GetConnectionString(
                    "Dsw2025TpiEntities"));
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
		        context.Exception.GetType().Name
		    );

		    return Task.CompletedTask;
		}
            };
        });

        builder.Services.AddDbContext<Dsw2025TpiContext>(options =>
        {
            options.UseSqlServer(
                builder.Configuration.GetConnectionString(
                    "Dsw2025TpiEntities"));
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

        // Crear roles y cargar administradores desde admins.json
        using (var scope = app.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            var logger = services.GetRequiredService<ILogger<Program>>();

            try
            {
                var userManager =
                    services.GetRequiredService<UserManager<IdentityUser>>();

                var roleManager =
                    services.GetRequiredService<RoleManager<IdentityRole>>();

                // 1. Crear los roles necesarios y comprobar resultados
                foreach (var roleName in new[] { "Admin", "User" })
                {
                    if (!await roleManager.RoleExistsAsync(roleName))
                    {
                        var roleResult =
                            await roleManager.CreateAsync(
                                new IdentityRole(roleName));

                        if (!roleResult.Succeeded)
                        {
                            throw new InvalidOperationException(
                                $"No se pudo crear el rol {roleName}: " +
                                string.Join(
                                    "; ",
                                    roleResult.Errors.Select(
                                        e => e.Description)));
                        }

                        logger.LogInformation(
                            "Rol {Role} creado correctamente.",
                            roleName);
                    }
                }

                // 2. Localizar el archivo admins.json
                string jsonFilePath = Path.Combine(
                    AppContext.BaseDirectory,
                    "admins.json");

                logger.LogInformation(
                    "Buscando admins.json en {Path}. Existe: {Exists}",
                    jsonFilePath,
                    File.Exists(jsonFilePath));

                if (!File.Exists(jsonFilePath))
                {
                    throw new FileNotFoundException(
                        "No se encontró admins.json.",
                        jsonFilePath);
                }

                string json = await File.ReadAllTextAsync(jsonFilePath);

                var admins = JsonSerializer.Deserialize<List<AdminUser>>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                if (admins == null || admins.Count == 0)
                {
                    throw new InvalidOperationException(
                        "admins.json no contiene administradores válidos.");
                }

                // 3. Crear los administradores y asignar el rol Admin
                foreach (var adminData in admins)
                {
                    if (string.IsNullOrWhiteSpace(adminData.Username) ||
                        string.IsNullOrWhiteSpace(adminData.Email) ||
                        string.IsNullOrWhiteSpace(adminData.Password))
                    {
                        logger.LogError(
                            "Hay un administrador con campos obligatorios vacíos.");

                        continue;
                    }

                    var adminUser =
                        await userManager.FindByNameAsync(
                            adminData.Username);

                    if (adminUser == null)
                    {
                        adminUser = new IdentityUser
                        {
                            UserName = adminData.Username,
                            Email = adminData.Email,
                            EmailConfirmed = true
                        };

                        var createResult =
                            await userManager.CreateAsync(
                                adminUser,
                                adminData.Password);

                        if (!createResult.Succeeded)
                        {
                            logger.LogError(
                                "No se pudo crear el administrador {Username}: {Errors}",
                                adminData.Username,
                                string.Join(
                                    "; ",
                                    createResult.Errors.Select(
                                        e => e.Description)));

                            continue;
                        }

                        logger.LogInformation(
                            "Administrador {Username} creado correctamente.",
                            adminData.Username);
                    }
                    else
                    {
                        logger.LogInformation(
                            "El administrador {Username} ya existe.",
                            adminData.Username);
                    }

                    if (!await userManager.IsInRoleAsync(
                        adminUser,
                        "Admin"))
                    {
                        var roleResult =
                            await userManager.AddToRoleAsync(
                                adminUser,
                                "Admin");

                        if (!roleResult.Succeeded)
                        {
                            logger.LogError(
                                "No se pudo asignar el rol Admin a {Username}: {Errors}",
                                adminData.Username,
                                string.Join(
                                    "; ",
                                    roleResult.Errors.Select(
                                        e => e.Description)));
                        }
                        else
                        {
                            logger.LogInformation(
                                "Rol Admin asignado a {Username}.",
                                adminData.Username);
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

            // 4. Cargar clientes de manera independiente
            try
            {
                var context =
                    services.GetRequiredService<Dsw2025TpiContext>();

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
        }

        app.UseHttpsRedirection();
        app.UseCors("AllowFrontend");

        app.UseMiddleware<ExceptionHandlingMiddleware>();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();

        app.MapHealthChecks("/healthcheck");

        app.Run();
    }
}
