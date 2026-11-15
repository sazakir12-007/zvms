using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using VisitorManagement.Models;
using VisitorManagement.Services;

namespace VisitorManagement.Data;

/// <summary>
/// One database shared by all tenants. Every <see cref="ITenantOwned"/> entity gets a global query filter
/// on the current tenant, so queries only ever see that tenant's rows, and is stamped with the tenant
/// when saved. Cross-tenant access (platform admin) must opt out explicitly with IgnoreQueryFilters().
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options, TenantContext tenant) : DbContext(options)
{
    /// <summary>Read by the query filters; EF evaluates it per query, so it follows the current request's tenant.</summary>
    public int CurrentTenantId => tenant.Id;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<PlatformAdmin> PlatformAdmins => Set<PlatformAdmin>();

    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Visit> Visits => Set<Visit>();
    public DbSet<VisitPhoto> VisitPhotos => Set<VisitPhoto>();
    public DbSet<VisitAttachment> VisitAttachments => Set<VisitAttachment>();
    public DbSet<BlacklistEntry> BlacklistEntries => Set<BlacklistEntry>();
    public DbSet<LookupItem> LookupItems => Set<LookupItem>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<StaffUser> StaffUsers => Set<StaffUser>();
    public DbSet<DataExportLog> DataExportLogs => Set<DataExportLog>();
    public DbSet<LicensePackage> LicensePackages => Set<LicensePackage>();
    public DbSet<CreditTransaction> CreditTransactions => Set<CreditTransaction>();
    public DbSet<AppFeedback> Feedback => Set<AppFeedback>();
    public DbSet<PlatformSetting> PlatformSettings => Set<PlatformSetting>();
    public DbSet<PaymentOrder> PaymentOrders => Set<PaymentOrder>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Tenant>(t =>
        {
            t.HasIndex(x => x.Slug).IsUnique();
            t.HasOne(x => x.LicensePackage).WithMany().HasForeignKey(x => x.LicensePackageId).OnDelete(DeleteBehavior.Restrict);
        });
        b.Entity<LicensePackage>(p =>
        {
            p.HasIndex(x => x.Name).IsUnique();
            p.Ignore(x => x.FeatureSet);
        });
        b.Entity<PaymentOrder>(o =>
        {
            o.Property(x => x.Status).HasConversion<string>();
            o.Property(x => x.Purpose).HasConversion<string>().HasDefaultValue(PaymentPurpose.Credit).HasSentinel((PaymentPurpose)(-1));
            o.HasIndex(x => x.GatewayOrderId).IsUnique();
            o.HasIndex(x => new { x.TenantId, x.CreatedAt });
        });

        b.Entity<AppFeedback>(f =>
        {
            f.Property(x => x.Category).HasConversion<string>();
            f.Property(x => x.Status).HasConversion<string>();
            f.HasIndex(x => new { x.TenantId, x.CreatedAt });
        });

        b.Entity<CreditTransaction>(c =>
        {
            c.Property(x => x.Kind).HasConversion<string>();
            c.HasIndex(x => new { x.TenantId, x.CreatedAt });
        });
        b.Entity<PlatformAdmin>().HasIndex(a => a.Email).IsUnique();

        // Uniqueness is per tenant: two tenants may both have alex@example.com as a host.
        b.Entity<Employee>().HasIndex(e => new { e.TenantId, e.Email }).IsUnique();
        b.Entity<StaffUser>().HasIndex(u => new { u.TenantId, u.Email }).IsUnique();

        b.Entity<Visit>(v =>
        {
            v.Property(x => x.Status).HasConversion<string>();
            v.HasIndex(x => new { x.TenantId, x.Status });
            v.HasIndex(x => new { x.TenantId, x.CheckedInAt });
            v.HasIndex(x => new { x.TenantId, x.AccessCode });
            v.HasOne(x => x.Employee)
             .WithMany(e => e.Visits)
             .HasForeignKey(x => x.EmployeeId)
             .OnDelete(DeleteBehavior.Restrict);
            v.HasOne(x => x.Photo)
             .WithOne()
             .HasForeignKey<VisitPhoto>(p => p.VisitId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<VisitPhoto>().HasKey(p => p.VisitId);

        b.Entity<VisitAttachment>(a =>
        {
            a.HasKey(x => new { x.VisitId, x.Kind });
            a.Property(x => x.Kind).HasConversion<string>();
            a.HasOne<Visit>().WithMany().HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BlacklistEntry>(e =>
        {
            e.HasIndex(x => new { x.TenantId, x.IsActive, x.Phone });
            e.HasOne(x => x.Visit).WithMany().HasForeignKey(x => x.VisitId).OnDelete(DeleteBehavior.SetNull);
        });

        b.Entity<LookupItem>(l =>
        {
            l.Property(x => x.Category).HasConversion<string>();
            l.HasIndex(x => new { x.TenantId, x.Category, x.Name }).IsUnique();
        });

        b.Entity<AppSetting>().HasKey(s => new { s.TenantId, s.Key });

        // The isolation guarantee: every tenant-owned entity is filtered to the current tenant.
        foreach (var type in b.Model.GetEntityTypes().Where(t => typeof(ITenantOwned).IsAssignableFrom(t.ClrType)))
        {
            b.Entity(type.ClrType).HasIndex(nameof(ITenantOwned.TenantId));
            b.Entity(type.ClrType).HasQueryFilter(TenantFilter(type.ClrType));
        }
    }

    // e => e.TenantId == this.CurrentTenantId
    private LambdaExpression TenantFilter(Type type)
    {
        var e = Expression.Parameter(type, "e");
        var body = Expression.Equal(
            Expression.Property(e, nameof(ITenantOwned.TenantId)),
            Expression.Property(Expression.Constant(this), nameof(CurrentTenantId)));
        return Expression.Lambda(body, e);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampTenant();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampTenant();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>New rows get the current tenant; touching another tenant's row is refused.</summary>
    private void StampTenant()
    {
        foreach (var entry in ChangeTracker.Entries<ITenantOwned>())
        {
            switch (entry.State)
            {
                case EntityState.Added when entry.Entity.TenantId == 0:
                    if (CurrentTenantId == 0)
                        throw new InvalidOperationException($"Can't save {entry.Entity.GetType().Name}: no tenant for this request.");
                    entry.Entity.TenantId = CurrentTenantId;
                    break;
                case EntityState.Added or EntityState.Modified or EntityState.Deleted when entry.Entity.TenantId != CurrentTenantId:
                    throw new InvalidOperationException(
                        $"Refused to save {entry.Entity.GetType().Name} belonging to tenant {entry.Entity.TenantId} while working as tenant {CurrentTenantId}.");
            }
        }
    }
}

/// <summary>Lets `dotnet ef` build the model at design time (no request, so no tenant).</summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=design-time.db").Options, new TenantContext());
}
