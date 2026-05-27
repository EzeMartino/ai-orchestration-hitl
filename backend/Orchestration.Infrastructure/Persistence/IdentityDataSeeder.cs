using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Orchestration.Infrastructure.Persistence;

public class IdentityDataSeeder(IServiceProvider serviceProvider) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = serviceProvider.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser<Guid>>>();
        
        var adminEmail = "admin@ezemartino.com";
        var adminUser = await userManager.FindByEmailAsync(adminEmail);
        
        if (adminUser is null)
        {
            adminUser = new IdentityUser<Guid>
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                UserName = adminEmail,
                Email = adminEmail,
                EmailConfirmed = true
            };
            
            var result = await userManager.CreateAsync(adminUser, "Password1!");
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Failed to create admin user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }
        }
        else if (adminUser.PasswordHash == "AQAAAAIAAYagAAAAEIKh2o5e/K4t8N2h8f8Xb5nZ45zE..." || string.IsNullOrEmpty(adminUser.PasswordHash))
        {
            var removeResult = await userManager.RemovePasswordAsync(adminUser);
            if (!removeResult.Succeeded)
            {
                throw new InvalidOperationException($"Failed to remove corrupted password from admin user: {string.Join(", ", removeResult.Errors.Select(e => e.Description))}");
            }

            var addResult = await userManager.AddPasswordAsync(adminUser, "Password1!");
            if (!addResult.Succeeded)
            {
                throw new InvalidOperationException($"Failed to set new password for admin user: {string.Join(", ", addResult.Errors.Select(e => e.Description))}");
            }
        }

        var secondEmail = "user@ezemartino.com";
        var secondUser = await userManager.FindByEmailAsync(secondEmail);
        
        if (secondUser is null)
        {
            secondUser = new IdentityUser<Guid>
            {
                Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                UserName = secondEmail,
                Email = secondEmail,
                EmailConfirmed = true
            };
            
            var result = await userManager.CreateAsync(secondUser, "Password1!");
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Failed to create second user: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }
        }
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

