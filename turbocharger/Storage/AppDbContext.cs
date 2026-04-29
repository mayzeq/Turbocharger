using Microsoft.EntityFrameworkCore;
using Turbocharger.Domain.Entities;

namespace Turbocharger.Storage;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Item> Item { get; set; }
    public DbSet<Bom> BOM { get; set; }
    public DbSet<WarehouseOperation> WarehouseOperations { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderLine> OrderLines { get; set; }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            foreach (var property in entry.Properties.Where(p => p.Metadata.ClrType == typeof(DateTime) || p.Metadata.ClrType == typeof(DateTime?)))
            {
                if (property.CurrentValue is DateTime dt && dt.Kind != DateTimeKind.Utc)
                {
                    property.CurrentValue = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                }
            }
        }
        return base.SaveChangesAsync(cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Конфигурация для Item
        modelBuilder.Entity<Item>(entity =>
        {
            entity.ToTable("Item");  
            entity.HasKey(e => e.ItemId);
            entity.Property(e => e.ItemName).IsRequired().HasMaxLength(100);
        });

        // Конфигурация для Bom
        modelBuilder.Entity<Bom>(entity =>
        {
            entity.ToTable("BOM");  
            entity.HasKey(e => e.BomId);

            entity.Property(e => e.Quantity)
                .IsRequired()
                .HasDefaultValue(1);

            // Связь с родителем
            entity.HasOne(b => b.Parent)
                .WithMany(i => i.ParentBoms)
                .HasForeignKey(b => b.ParentId)
                .OnDelete(DeleteBehavior.Restrict); 

            // Связь с компонентом
            entity.HasOne(b => b.Component)
                .WithMany(i => i.ComponentBoms)
                .HasForeignKey(b => b.ComponentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Конфигурация WarehouseOperation
        modelBuilder.Entity<WarehouseOperation>(entity =>
        {
            entity.ToTable("WarehouseOperations");
            entity.HasKey(e => e.OperationId);
            entity.Property(e => e.OperationType).HasMaxLength(20);
            entity.Property(e => e.Comment).HasMaxLength(500);
            entity.HasOne(e => e.Item)
                .WithMany(i => i.WarehouseOperations)
                .HasForeignKey(e => e.ItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // Конфигурация Order
        modelBuilder.Entity<Order>(entity =>
        {
            entity.ToTable("Orders");
            entity.HasKey(e => e.OrderId);
            entity.Property(e => e.Status).HasMaxLength(20);
            entity.Property(e => e.Comment).HasMaxLength(500);
        });

        modelBuilder.Entity<OrderLine>(entity =>
        {
            entity.ToTable("OrderLines");
            entity.HasKey(e => e.OrderLineId);
            entity.HasOne(e => e.Order)
                .WithMany(o => o.Lines)
                .HasForeignKey(e => e.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(e => e.Item)
                .WithMany(i => i.OrderLines)
                .HasForeignKey(e => e.ItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}