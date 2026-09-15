using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pwdmgr.Application.Auth;

namespace Pwdmgr.Infrastructure.Persistence;

/// <summary>Used by <c>dotnet ef migrations add</c> only; no database is contacted for migration scaffolding.</summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PwdmgrDbContext>
{
    public PwdmgrDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PwdmgrDbContext>()
            .UseNpgsql("Host=localhost;Database=pwdmgr-design-time")
            .UseSnakeCaseNamingConvention()
            .Options;
        return new PwdmgrDbContext(options, new RequestContext());
    }
}
