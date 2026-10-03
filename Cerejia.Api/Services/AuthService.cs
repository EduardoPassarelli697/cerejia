using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using Cerejia.Api.Data;
using Cerejia.Api.DTOs;
using Cerejia.Api.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Cerejia.Api.Services;

public class AuthService(AppDbContext db, IConfiguration config, CnpjService cnpjService)
{
    private const int ExpiracaoChatHoras = 24;

    private const int ExpiracaoAdminMinutos = 30;

    public async Task<TokenResponseDto> CadastrarEmpresaAsync(CadastroEmpresaDto dto, CancellationToken ct)
    {
        var cnpjLimpo = new string(dto.Cnpj.Where(char.IsDigit).ToArray());

        ValidarSenhaAdmin(dto.SenhaAdmin);

        if (dto.Funcionarios.Count == 0)
            throw new InvalidOperationException("Cadastre pelo menos um colaborador.");

        var emailsVistos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in dto.Funcionarios)
        {
            if (string.IsNullOrWhiteSpace(f.Nome))
                throw new InvalidOperationException("Informe o nome de todos os colaboradores.");

            var cpfLimpo = new string(f.Cpf.Where(char.IsDigit).ToArray());
            if (!CpfValido(cpfLimpo))
                throw new InvalidOperationException($"CPF inválido: {f.Cpf}");

            if (!EmailValido(f.EmailCorporativo))
                throw new InvalidOperationException($"E-mail corporativo inválido: {f.EmailCorporativo}");

            if (!emailsVistos.Add(f.EmailCorporativo.Trim()))
                throw new InvalidOperationException($"E-mail corporativo duplicado na lista: {f.EmailCorporativo}");

            if (string.IsNullOrEmpty(f.Senha))
                throw new InvalidOperationException($"Informe uma senha para o colaborador {f.EmailCorporativo}.");
        }

        if (await db.Empresas.AnyAsync(e => e.Cnpj == cnpjLimpo, ct))
            throw new InvalidOperationException("Este CNPJ já está cadastrado.");

        var validacao = await cnpjService.ValidarAsync(cnpjLimpo, ct);
        if (!validacao.Valido)
            throw new InvalidOperationException(validacao.Mensagem ?? "CNPJ inválido.");
        if (!validacao.Ativo)
            throw new InvalidOperationException("CNPJ não está ativo.");

        var empresa = new Empresa
        {
            Nome = dto.Nome,
            Cnpj = cnpjLimpo,
            CnpjAtivo = true,
            SenhaAdminHash = BCrypt.Net.BCrypt.HashPassword(dto.SenhaAdmin)
        };
        db.Empresas.Add(empresa);

        var funcionarios = dto.Funcionarios.Select(f => new Usuario
        {
            Empresa = empresa,
            Nome = f.Nome.Trim(),
            Cpf = new string(f.Cpf.Where(char.IsDigit).ToArray()),
            EmailCorporativo = f.EmailCorporativo.Trim(),
            SenhaHash = BCrypt.Net.BCrypt.HashPassword(f.Senha),
            Supervisor = f.Supervisor
        }).ToList();
        db.Usuarios.AddRange(funcionarios);

        await db.SaveChangesAsync(ct);

        var primeiro = funcionarios[0];
        return GerarTokenChat(empresa, primeiro);
    }

    public async Task<TokenResponseDto> EntrarAsync(EntrarDto dto, CancellationToken ct)
    {
        var cnpjLimpo = new string(dto.Cnpj.Where(char.IsDigit).ToArray());

        var empresa = await db.Empresas
            .FirstOrDefaultAsync(e => e.Cnpj == cnpjLimpo && e.Ativo, ct)
            ?? throw new UnauthorizedAccessException("CNPJ, e-mail ou senha inválidos.");

        var usuario = await db.Usuarios.FirstOrDefaultAsync(u =>
            u.EmpresaId == empresa.Id &&
            u.Ativo &&
            u.EmailCorporativo.ToLower() == dto.EmailCorporativo.Trim().ToLower(), ct)
            ?? throw new UnauthorizedAccessException("CNPJ, e-mail ou senha inválidos.");

        if (!BCrypt.Net.BCrypt.Verify(dto.Senha, usuario.SenhaHash))
            throw new UnauthorizedAccessException("CNPJ, e-mail ou senha inválidos.");

        return GerarTokenChat(empresa, usuario);
    }

    public async Task<TokenResponseDto> VerificarAdminAsync(
        Guid usuarioId, VerificarAdminDto dto, CancellationToken ct)
    {
        var usuario = await db.Usuarios
            .Include(u => u.Empresa)
            .FirstOrDefaultAsync(u => u.Id == usuarioId && u.Ativo, ct)
            ?? throw new UnauthorizedAccessException("Sessão inválida.");

        if (!usuario.Supervisor)
            throw new UnauthorizedAccessException("Só supervisores podem acessar essa área.");

        if (!BCrypt.Net.BCrypt.Verify(dto.SenhaAdmin, usuario.Empresa.SenhaAdminHash))
            throw new UnauthorizedAccessException("Senha do admin incorreta.");

        return GerarTokenElevado(usuario.Empresa, "admin_documentos");
    }

    public async Task<TokenResponseDto> VerificarColaboradoresAsync(
        Guid usuarioId, VerificarColaboradoresDto dto, CancellationToken ct)
    {
        var usuario = await db.Usuarios
            .Include(u => u.Empresa)
            .FirstOrDefaultAsync(u => u.Id == usuarioId && u.Ativo, ct)
            ?? throw new UnauthorizedAccessException("Sessão inválida.");

        if (!usuario.Supervisor)
            throw new UnauthorizedAccessException("Só supervisores podem acessar essa área.");

        if (!BCrypt.Net.BCrypt.Verify(dto.SenhaAdmin, usuario.Empresa.SenhaAdminHash))
            throw new UnauthorizedAccessException("Senha do admin incorreta.");

        return GerarTokenElevado(usuario.Empresa, "colaboradores_admin");
    }

    private TokenResponseDto GerarTokenChat(Empresa empresa, Usuario usuario)
    {
        var claims = new List<Claim>
        {
            new("empresa_id", empresa.Id.ToString()),
            new("usuario_id", usuario.Id.ToString()),
            new("supervisor", usuario.Supervisor ? "true" : "false"),
        };

        var token = CriarToken(claims, DateTime.UtcNow.AddHours(ExpiracaoChatHoras));

        return new TokenResponseDto(
            Token: token,
            EmpresaId: empresa.Id,
            Nome: empresa.Nome,
            Cnpj: empresa.Cnpj,
            UsuarioId: usuario.Id,
            NomeUsuario: usuario.Nome,
            Supervisor: usuario.Supervisor,
            AdminDocumentos: false,
            ColaboradoresAdmin: false
        );
    }

    private TokenResponseDto GerarTokenElevado(Empresa empresa, string nomeClaim)
    {
        var claims = new List<Claim>
        {
            new("empresa_id", empresa.Id.ToString()),
            new(nomeClaim, "true"),
        };

        var token = CriarToken(claims, DateTime.UtcNow.AddMinutes(ExpiracaoAdminMinutos));

        return new TokenResponseDto(
            Token: token,
            EmpresaId: empresa.Id,
            Nome: empresa.Nome,
            Cnpj: empresa.Cnpj,
            UsuarioId: null,
            NomeUsuario: null,
            Supervisor: false,
            AdminDocumentos: nomeClaim == "admin_documentos",
            ColaboradoresAdmin: nomeClaim == "colaboradores_admin"
        );
    }

    private string CriarToken(List<Claim> claims, DateTime expiracao)
    {
        var chave = config["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key não configurado.");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(chave));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"],
            audience: config["Jwt:Audience"],
            claims: claims,
            expires: expiracao,
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static void ValidarSenhaAdmin(string senha)
    {
        if (string.IsNullOrEmpty(senha) || senha.Length < 8)
            throw new InvalidOperationException("A senha do admin precisa ter pelo menos 8 caracteres.");
        if (!Regex.IsMatch(senha, @"[A-Za-z]"))
            throw new InvalidOperationException("A senha do admin precisa ter pelo menos uma letra.");
        if (!Regex.IsMatch(senha, @"[0-9]"))
            throw new InvalidOperationException("A senha do admin precisa ter pelo menos um número.");
    }

    private static bool EmailValido(string email)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        try { _ = new System.Net.Mail.MailAddress(email); return true; }
        catch { return false; }
    }

    private static bool CpfValido(string cpf) => cpf.Length == 11;
}
