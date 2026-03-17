using Microsoft.EntityFrameworkCore;
using Turbocharger.Domain.Entities;

namespace Turbocharger.Storage;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // DbSet с именами, соответствующими таблицам в скрипте
    public DbSet<Item> Item { get; set; }
    public DbSet<Bom> BOM { get; set; }  // Именно BOM, как в скрипте

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Конфигурация для Item
        modelBuilder.Entity<Item>(entity =>
        {
            entity.ToTable("Item");  // явно указываем имя таблицы
            entity.HasKey(e => e.item_id);
            entity.Property(e => e.item_name).IsRequired().HasMaxLength(100);
        });

        // Конфигурация для Bom
        modelBuilder.Entity<Bom>(entity =>
        {
            entity.ToTable("BOM");  // явно указываем имя таблицы
            entity.HasKey(e => e.bom_id);

            entity.Property(e => e.quantity)
                .IsRequired()
                .HasDefaultValue(1);

            // Связь с родителем
            entity.HasOne(b => b.Parent)
                .WithMany(i => i.ParentBoms)
                .HasForeignKey(b => b.parent_id)
                .OnDelete(DeleteBehavior.Cascade);  // ON DELETE CASCADE как в скрипте

            // Связь с компонентом
            entity.HasOne(b => b.Component)
                .WithMany(i => i.ComponentBoms)
                .HasForeignKey(b => b.component_id)
                .OnDelete(DeleteBehavior.Cascade);  // ON DELETE CASCADE как в скрипте
        });
    }
}