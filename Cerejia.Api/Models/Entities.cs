using Pgvector;

namespace Cerejia.Api.Models;

public class Empresa
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = "";

    public string Cnpj { get; set; } = "";

    public bool CnpjAtivo { get; set; }

    public string SenhaAdminHash { get; set; } = "";

    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public bool Ativo { get; set; } = true;

    public ICollection<Usuario> Usuarios { get; set; } = [];
    public ICollection<Documento> Documentos { get; set; } = [];
    public ICollection<Conversa> Conversas { get; set; } = [];
}

public class Usuario
{
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }
    public string Nome { get; set; } = "";
    public string Cpf { get; set; } = "";
    public string EmailCorporativo { get; set; } = "";
    public string SenhaHash { get; set; } = "";
    public bool Supervisor { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;
    public bool Ativo { get; set; } = true;

    public Empresa Empresa { get; set; } = null!;
}

public class Documento
{
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }
    public string NomeArquivo { get; set; } = "";
    public string TipoArquivo { get; set; } = "";
    public int? TamanhoKb { get; set; }
    public string Status { get; set; } = "processando";
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public Empresa Empresa { get; set; } = null!;
    public ICollection<DocumentoChunk> Chunks { get; set; } = [];
}

public class DocumentoChunk
{
    public Guid Id { get; set; }
    public Guid DocumentoId { get; set; }
    public Guid EmpresaId { get; set; }
    public string Conteudo { get; set; } = "";
    public Vector? Embedding { get; set; }
    public int ChunkIndex { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public Documento Documento { get; set; } = null!;
}

public class Conversa
{
    public Guid Id { get; set; }
    public Guid EmpresaId { get; set; }

    public Guid? UsuarioId { get; set; }

    public string? Titulo { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public Empresa Empresa { get; set; } = null!;
    public Usuario? Usuario { get; set; }
    public ICollection<Mensagem> Mensagens { get; set; } = [];
}

public class Mensagem
{
    public Guid Id { get; set; }
    public Guid ConversaId { get; set; }
    public string Papel { get; set; } = "";
    public string Conteudo { get; set; } = "";
    public int TokensUsados { get; set; }
    public DateTime CriadoEm { get; set; } = DateTime.UtcNow;

    public Conversa Conversa { get; set; } = null!;
}

