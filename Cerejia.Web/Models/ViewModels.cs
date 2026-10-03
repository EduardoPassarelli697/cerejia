namespace Cerejia.Web.Models;

public class RespostaChatDto
{
    public Guid ConversaId { get; set; }
    public string Resposta { get; set; } = "";
    public List<FonteDto> Fontes { get; set; } = [];
    public int TokensUsados { get; set; }
}

public class FonteDto
{
    public string NomeArquivo { get; set; } = "";
    public string Trecho { get; set; } = "";
}

public class ConversaResumo
{
    public Guid Id { get; set; }
    public string? Titulo { get; set; }
    public DateTime CriadoEm { get; set; }
}

public class ConversaCompleta
{
    public Guid Id { get; set; }
    public string? Titulo { get; set; }
    public DateTime CriadoEm { get; set; }
    public List<MensagemDto> Mensagens { get; set; } = [];
}

public class MensagemDto
{
    public Guid Id { get; set; }
    public string Papel { get; set; } = "";
    public string Conteudo { get; set; } = "";
    public DateTime CriadoEm { get; set; }
}

public class DocumentoDto
{
    public Guid Id { get; set; }
    public string NomeArquivo { get; set; } = "";
    public string TipoArquivo { get; set; } = "";
    public string Status { get; set; } = "";
    public int? TamanhoKb { get; set; }
    public DateTime CriadoEm { get; set; }
}
