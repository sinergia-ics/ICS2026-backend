using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Dsw2025Tpi.Api;
using Dsw2025Tpi.Application.Dtos;
using Dsw2025Tpi.Data;
using Dsw2025Tpi.Domain.Entities;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ECommerce.IntegrationTests;

/// <summary>API real sobre dos bases SQLite efímeras y aisladas de SQL Server.</summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _business = new($"Data Source=business-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
    private readonly SqliteConnection _identity = new($"Data Source=identity-{Guid.NewGuid():N};Mode=Memory;Cache=Shared");
    internal const string Password = "Tp4-Test-2026!";

    public CustomWebApplicationFactory()
    {
        _business.Open();
        _identity.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DB_CONNECTION_STRING"] = "Server=testing-only;Database=NeverUsed",
            ["Jwt:Key"] = "TP4-Testing-Only-Key-At-Least-32-Bytes-2026",
            ["Jwt:Issuer"] = "TP4.Tests",
            ["Jwt:Audience"] = "TP4.Tests",
            ["LogPath"] = ""
        }));
        builder.ConfigureTestServices(services =>
        {
            ReplaceContext<Dsw2025TpiContext>(services, _business);
            ReplaceContext<AuthenticateContext>(services, _identity);
        });
    }

    private static void ReplaceContext<TContext>(IServiceCollection services, SqliteConnection connection) where TContext : DbContext
    {
        services.RemoveAll<DbContextOptions<TContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<TContext>>();
        services.RemoveAll<TContext>();
        services.AddDbContext<TContext>(options => options.UseSqlite(connection));
    }

    /// <summary>Reinicia datos y crea usuarios de prueba sin tocar archivos ni servicios de producción.</summary>
    public async Task ResetAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
        await db.Database.EnsureDeletedAsync();
        await db.Database.EnsureCreatedAsync();
        var auth = scope.ServiceProvider.GetRequiredService<AuthenticateContext>();
        await auth.Database.EnsureDeletedAsync();
        await auth.Database.EnsureCreatedAsync();
        var roles = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        foreach (var role in new[] { "Admin", "User" })
        {
            var result = await roles.CreateAsync(new IdentityRole(role));
            if (!result.Succeeded) throw new InvalidOperationException(string.Join(";", result.Errors.Select(e => e.Description)));
            var user = new IdentityUser { UserName = role.ToLowerInvariant(), Email = $"{role.ToLowerInvariant()}@example.test", EmailConfirmed = true };
            result = await users.CreateAsync(user, Password);
            if (!result.Succeeded) throw new InvalidOperationException(string.Join(";", result.Errors.Select(e => e.Description)));
            result = await users.AddToRoleAsync(user, role);
            if (!result.Succeeded) throw new InvalidOperationException(string.Join(";", result.Errors.Select(e => e.Description)));
        }
    }

    public async Task SeedAsync(params EntityBase[] entities)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    public async Task<T> ReadAsync<T>(Func<Dsw2025TpiContext, Task<T>> query)
    {
        using var scope = Services.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<Dsw2025TpiContext>());
    }

    /// <summary>Obtiene el JWT a través del login real y lo adjunta al cliente HTTP.</summary>
    public async Task<HttpClient> AuthenticatedClientAsync(string username = "admin")
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginModel(username, Password));
        login.EnsureSuccessStatusCode();
        var body = await login.Content.ReadFromJsonAsync<JsonElement>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) { _business.Dispose(); _identity.Dispose(); }
    }
}

[CollectionDefinition("IntegrationTests", DisableParallelization = true)]
public sealed class IntegrationTestsCollection : ICollectionFixture<CustomWebApplicationFactory>;
