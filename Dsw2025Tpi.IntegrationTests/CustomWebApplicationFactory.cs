
using Dsw2025Tpi.Api;
using Dsw2025Tpi.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Dsw2025Tpi.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _applicationConnection;
    private readonly SqliteConnection _identityConnection;

    public CustomWebApplicationFactory()
    {
        _applicationConnection =
            new SqliteConnection("Data Source=:memory:");

        _identityConnection =
            new SqliteConnection("Data Source=:memory:");

        _applicationConnection.Open();
        _identityConnection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Reemplazar la configuración de la base principal.
            services.RemoveAll<DbContextOptions<Dsw2025TpiContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<Dsw2025TpiContext>>();
            services.RemoveAll<Dsw2025TpiContext>();

            services.AddDbContext<Dsw2025TpiContext>(options =>
            {
                options.UseSqlite(_applicationConnection);
            });

            // Reemplazar la configuración de la base de Identity.
            services.RemoveAll<DbContextOptions<AuthenticateContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AuthenticateContext>>();
            services.RemoveAll<AuthenticateContext>();

            services.AddDbContext<AuthenticateContext>(options =>
            {
                options.UseSqlite(_identityConnection);
            });
        });
    }

    public void InitializeDatabase()
    {
        using var scope = Services.CreateScope();

        var applicationContext =
            scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();

        var identityContext =
            scope.ServiceProvider.GetRequiredService<AuthenticateContext>();

        applicationContext.Database.EnsureCreated();
        identityContext.Database.EnsureCreated();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _applicationConnection.Dispose();
            _identityConnection.Dispose();
        }
    }
}
