namespace Cerejia.Api.DTOs;

public record CadastroFuncionarioDto(string Nome, string Cpf, string EmailCorporativo, string Senha, bool Supervisor);

public record CadastroEmpresaDto(
    string Nome,
    string Cnpj,
    string SenhaAdmin,
    List<CadastroFuncionarioDto> Funcionarios
);

public record EntrarDto(string Cnpj, string EmailCorporativo, string Senha);

public record ValidarCnpjResponseDto(bool Valido, bool Ativo, string? RazaoSocial, string? Mensagem);

public record VerificarAdminDto(string SenhaAdmin);

public record VerificarColaboradoresDto(string SenhaAdmin);

public record TokenResponseDto(
    string Token,
    Guid EmpresaId,
    string Nome,
    string Cnpj,
    Guid? UsuarioId,
    string? NomeUsuario,
    bool Supervisor,
    bool AdminDocumentos,
    bool ColaboradoresAdmin
);

public record FuncionarioDto(
    Guid Id,
    string Nome,
    string Cpf,
    string EmailCorporativo,
    bool Supervisor,
    DateTime CriadoEm
);

public record EnviarMensagemDto(
    Guid? ConversaId,
    string Mensagem
);

public record RespostaChatDto(
    Guid ConversaId,
    string Resposta,
    List<FonteDto> Fontes,
    int TokensUsados
);

public record FonteDto(
    string NomeArquivo,
    string Trecho
);

public record DocumentoDto(
    Guid Id,
    string NomeArquivo,
    string TipoArquivo,
    string Status,
    int? TamanhoKb,
    DateTime CriadoEm
);

public record ConversaDto(
    Guid Id,
    string? Titulo,
    DateTime CriadoEm,
    List<MensagemDto> Mensagens
);

public record MensagemDto(
    Guid Id,
    string Papel,
    string Conteudo,
    DateTime CriadoEm
);
