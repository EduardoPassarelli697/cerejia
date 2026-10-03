using Cerejia.Api.Models;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace Cerejia.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Empresa> Empresas => Set<Empresa>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Documento> Documentos => Set<Documento>();
    public DbSet<DocumentoChunk> DocumentoChunks => Set<DocumentoChunk>();
    public DbSet<Conversa> Conversas => Set<Conversa>();
    public DbSet<Mensagem> Mensagens => Set<Mensagem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {

        modelBuilder.HasPostgresExtension("vector");

        modelBuilder.Entity<Empresa>().ToTable("empresas");
        modelBuilder.Entity<Usuario>().ToTable("usuarios");
        modelBuilder.Entity<Documento>().ToTable("documentos");
        modelBuilder.Entity<DocumentoChunk>().ToTable("documento_chunks");
        modelBuilder.Entity<Conversa>().ToTable("conversas");
        modelBuilder.Entity<Mensagem>().ToTable("mensagens");

        modelBuilder.Entity<Empresa>()
            .HasIndex(e => e.Cnpj)
            .IsUnique();

        modelBuilder.Entity<Usuario>()
            .HasIndex(u => new { u.EmpresaId, u.EmailCorporativo })
            .IsUnique();

        modelBuilder.Entity<DocumentoChunk>()
            .Property(c => c.Embedding)
            .HasColumnType("vector(384)");

        modelBuilder.Entity<DocumentoChunk>()
            .HasIndex(c => c.Embedding)
            .HasMethod("ivfflat")
            .HasOperators("vector_cosine_ops")
            .HasStorageParameter("lists", 100);

        modelBuilder.Entity<Usuario>()
            .HasOne(u => u.Empresa)
            .WithMany(e => e.Usuarios)
            .HasForeignKey(u => u.EmpresaId);

        modelBuilder.Entity<Documento>()
            .HasOne(d => d.Empresa)
            .WithMany(e => e.Documentos)
            .HasForeignKey(d => d.EmpresaId);

        modelBuilder.Entity<DocumentoChunk>()
            .HasOne(c => c.Documento)
            .WithMany(d => d.Chunks)
            .HasForeignKey(c => c.DocumentoId);

        modelBuilder.Entity<Conversa>()
            .HasOne(c => c.Empresa)
            .WithMany(e => e.Conversas)
            .HasForeignKey(c => c.EmpresaId);

        modelBuilder.Entity<Conversa>()
            .HasOne(c => c.Usuario)
            .WithMany()
            .HasForeignKey(c => c.UsuarioId)
            .IsRequired(false);

        modelBuilder.Entity<Mensagem>()
            .HasOne(m => m.Conversa)
            .WithMany(c => c.Mensagens)
            .HasForeignKey(m => m.ConversaId);

        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var prop in entity.GetProperties())
                prop.SetColumnName(ToSnakeCase(prop.Name));

            foreach (var key in entity.GetKeys())
                key.SetName(ToSnakeCase(key.GetName()!));

            foreach (var fk in entity.GetForeignKeys())
                fk.SetConstraintName(ToSnakeCase(fk.GetConstraintName()!));
        }
    }

    private static string ToSnakeCase(string name)
    {
        return string.Concat(name.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? "_" + char.ToLower(c) : char.ToLower(c).ToString()));
    }
}
