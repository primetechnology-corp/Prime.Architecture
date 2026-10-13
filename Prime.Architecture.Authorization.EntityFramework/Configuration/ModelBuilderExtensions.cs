using Microsoft.EntityFrameworkCore;
using Prime.Architecture.Authorization.EntityFramework.Entities;

namespace Prime.Architecture.Authorization.EntityFramework.Configuration;

public static class ModelBuilderExtensions
{
    /// <summary>
    /// Mapeia as tabelas de permissões. Chame em <c>OnModelCreating</c> do seu DbContext.
    /// </summary>
    public static ModelBuilder AplicarResourceAuthorization(this ModelBuilder modelBuilder, string? schema = null)
    {
        modelBuilder.Entity<Permissao>(e =>
        {
            e.ToTable("Permissoes", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Sigla).HasMaxLength(150).IsRequired();
            e.Property(x => x.Descricao).HasMaxLength(255);
            e.HasIndex(x => x.Sigla).IsUnique();
        });

        modelBuilder.Entity<PerfilPermissao>(e =>
        {
            e.ToTable("PerfisPermissoes", schema);
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.PerfilId, x.PermissaoId }).IsUnique();
            e.HasOne(x => x.Permissao).WithMany(p => p.Perfis).HasForeignKey(x => x.PermissaoId);
        });

        modelBuilder.Entity<UsuarioPermissao>(e =>
        {
            e.ToTable("UsuariosPermissoes", schema);
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UsuarioId, x.PermissaoId }).IsUnique();
            e.HasOne(x => x.Permissao).WithMany(p => p.Usuarios).HasForeignKey(x => x.PermissaoId);
        });

        return modelBuilder;
    }
}
