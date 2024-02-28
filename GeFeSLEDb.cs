using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using GeFeSLE;
using Microsoft.EntityFrameworkCore.Design;


public class GeFeSLEDb : IdentityDbContext<GeFeSLEUser>
{
    public GeFeSLEDb()
    {
    }

    public GeFeSLEDb(DbContextOptions<GeFeSLEDb> options)
    : base(options) { }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder); // This needs to be called for Identity to work properly.

        // Your existing model configuration...
    }

    public DbSet<GeListItem> Items => Set<GeListItem>();
    public DbSet<GeList> Lists => Set<GeList>();

}

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<GeFeSLEDb>
{
    public GeFeSLEDb CreateDbContext(string[] args)
    {
        Console.WriteLine("Creating design-time context");
        var optionsBuilder = new DbContextOptionsBuilder<GeFeSLEDb>();
        var dbName = args.Length > 0  ? args[0] : "default.db";
        Console.WriteLine("MIGRATING database: " + dbName);
        optionsBuilder.UseSqlite($"Data Source={dbName}");

        return new GeFeSLEDb(optionsBuilder.Options);
    }
}
