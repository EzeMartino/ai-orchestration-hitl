using System;
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
            
            await userManager.CreateAsync(adminUser, "Password1!");
        }
    }
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
