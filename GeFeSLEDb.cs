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

    builder.Entity<JwtToken>()
        .HasKey(j => j.Id);

    builder.Entity<GeAPActor>()
        .OwnsOne(a => a.Icon);
    builder.Entity<GeAPActor>()
        .Navigation(a => a.Icon)
        .IsRequired();

    builder.Entity<GeAPActor>()
        .OwnsOne(a => a.Image);
    builder.Entity<GeAPActor>()
        .Navigation(a => a.Image)
        .IsRequired();

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

    builder.Entity<GeFeSLEUser>()
        .HasIndex(u => u.UploadsPath)
        .IsUnique();

    builder.Entity<GeListItemComment>()
        .HasIndex(c => new { c.ListId, c.ItemId });

    builder.Entity<GeListItemComment>()
        .HasIndex(c => c.ParentCommentId);

    builder.Entity<GeListItemComment>()
        .HasIndex(c => new { c.ListId, c.RemoteObjectIri })
        .IsUnique();

    builder.Entity<ActivityPubObjectLike>()
        .HasIndex(l => new { l.ListId, l.ObjectIri, l.ActorIri })
        .IsUnique();

    builder.Entity<ActivityPubObjectLike>()
        .HasIndex(l => l.LikeActivityIri)
        .IsUnique();

    builder.Entity<ActivityPubObjectLike>()
        .HasIndex(l => new { l.ListId, l.ItemId, l.IsActive });

    builder.Entity<ActivityPubObjectLike>()
        .HasIndex(l => new { l.ListId, l.CommentId, l.IsActive });
}

public DbSet<GeListItem> Items => Set<GeListItem>();
public DbSet<GeList> Lists => Set<GeList>();
public DbSet<GeListFollower> ListFollowers => Set<GeListFollower>();
public DbSet<GeListItemComment> ItemComments => Set<GeListItemComment>();
public DbSet<ActivityPubObjectLike> ActivityPubObjectLikes => Set<ActivityPubObjectLike>();

}

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<GeFeSLEDb>
{
    public GeFeSLEDb CreateDbContext(string[] args)
    {
        Console.WriteLine("Creating design-time context");
        var optionsBuilder = new DbContextOptionsBuilder<GeFeSLEDb>();
        var dbName = args.Length > 0 ? args[0] : "default.db";
        Console.WriteLine("MIGRATING database: " + dbName);
        optionsBuilder.UseSqlite(
            $"Data Source={dbName}",
            sqliteOptions => sqliteOptions.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));

        return new GeFeSLEDb(optionsBuilder.Options);
    }
}
