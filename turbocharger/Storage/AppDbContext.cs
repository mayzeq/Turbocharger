using Microsoft.EntityFrameworkCore;
using Turbocharger.Domain.Entities;

namespace Turbocharger.Storage;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Item> Item { get; set; }
    public DbSet<Bom> BOM { get; set; }
    public DbSet<WarehouseOperation> WarehouseOperations { get; set; }
    public DbSet<StockBatch> StockBatches { get; set; }

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
                .OnDelete(DeleteBehavior.Cascade); 

            // Связь с компонентом
            entity.HasOne(b => b.Component)
                .WithMany(i => i.ComponentBoms)
                .HasForeignKey(b => b.ComponentId)
                .OnDelete(DeleteBehavior.Cascade);
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

        // Конфигурация StockBatch
        modelBuilder.Entity<StockBatch>(entity =>
        {
            entity.ToTable("StockBatches");
            entity.HasKey(e => e.StockBatchId);
            entity.HasOne(e => e.Item)
                .WithMany(i => i.StockBatches)
                .HasForeignKey(e => e.ItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}