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



//     protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
//     {
//         optionsBuilder.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
//     }
//     error CS1061: 'DbContextOptionsBuilder' does not contain a definition for 'UseQuerySplittingBehavior 
//     ' and no accessible extension method 'UseQuerySplittingBehavior' accepting a first argument of type 'DbContextOptionsBuilder' could be found (are 
//     you missing a using directive or an assembly reference?) [D:\repos\GeFeSLE-server\GeFeSLE.csproj]

protected override void OnModelCreating(ModelBuilder builder)
{
    base.OnModelCreating(builder); // This needs to be called for Identity to work properly.


    builder.Entity<GeList>()    // each list has zero or more listowners
        .HasMany(g => g.ListOwners)
        .WithMany();

    builder.Entity<GeList>()    // each list has zero or more contributors
        .HasMany(g => g.Contributors)
        .WithMany();

    builder.Entity<GeList>()    // each list has exactly one creator, who can create many lists
        .HasOne(g => g.Creator)
        .WithMany()
        .HasForeignKey(g => g.CreatorId);
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
        var dbName = args.Length > 0 ? args[0] : "default.db";
        Console.WriteLine("MIGRATING database: " + dbName);
        optionsBuilder.UseSqlite($"Data Source={dbName}");

        return new GeFeSLEDb(optionsBuilder.Options);
    }
}
