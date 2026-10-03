using Microsoft.AspNetCore.Components.Forms;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Blazored.LocalStorage;
using Cerejia.Web.Models;

namespace Cerejia.Web.Services;

public class ApiService(HttpClient http, AuthStateService auth, ILocalStorageService storage)
{
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    private enum NivelToken { Chat, Admin, Colaboradores }

    private async Task ConfigurarHeaderAsync(NivelToken nivel = NivelToken.Chat)
    {
        var chave = nivel switch
        {
            NivelToken.Admin => "tokenAdmin",
            NivelToken.Colaboradores => "tokenColaboradores",
            _ => "token"
        };
        var token = await storage.GetItemAsync<string>(chave);
        http.DefaultRequestHeaders.Authorization = string.IsNullOrEmpty(token)
            ? null
            : new AuthenticationHeaderValue("Bearer", token);
    }

    public async Task<ValidarCnpjResultado> ValidarCnpjAsync(string cnpj, CancellationToken ct)
    {
        try
        {
            var resp = await http.GetAsync($"api/empresas/validar-cnpj/{Uri.EscapeDataString(cnpj)}", ct);
            if (!resp.IsSuccessStatusCode)
                return new ValidarCnpjResultado(false, false, null, await ExtrairErroAsync(resp));

            var dto = await resp.Content.ReadFromJsonAsync<ValidarCnpjResponse>(_json, ct);
            if (dto == null)
                return new ValidarCnpjResultado(false, false, null, "Resposta inválida do servidor.");

            return new ValidarCnpjResultado(dto.Valido, dto.Ativo, dto.RazaoSocial, dto.Mensagem);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ValidarCnpjResultado(false, false, null, $"Não foi possível verificar o CNPJ: {ex.Message}");
        }
    }

    public async Task<(bool Sucesso, string? Erro)> CadastrarEmpresaAsync(
        string nome, string cnpj, string senhaAdmin, List<FuncionarioNovo> funcionarios)
    {
        var resp = await http.PostAsJsonAsync("api/empresas/cadastro", new
        {
            Nome = nome,
            Cnpj = cnpj,
            SenhaAdmin = senhaAdmin,
            Funcionarios = funcionarios.Select(f => new
            {
                f.Nome,
                f.Cpf,
                f.EmailCorporativo,
                f.Senha,
                f.Supervisor
            })
        });

        if (!resp.IsSuccessStatusCode)
            return (false, await ExtrairErroAsync(resp));

        var token = await resp.Content.ReadFromJsonAsync<TokenResponse>(_json);
        if (token == null) return (false, "Resposta inválida do servidor.");

        await SalvarTokenChatAsync(token);
        return (true, null);
    }

    public async Task<(bool Sucesso, string? Erro)> EntrarAsync(string cnpj, string emailCorporativo, string senha)
    {
        var resp = await http.PostAsJsonAsync("api/empresas/entrar",
            new { Cnpj = cnpj, EmailCorporativo = emailCorporativo, Senha = senha });

        if (!resp.IsSuccessStatusCode)
            return (false, await ExtrairErroAsync(resp));

        var token = await resp.Content.ReadFromJsonAsync<TokenResponse>(_json);
        if (token == null) return (false, "Resposta inválida do servidor.");

        await SalvarTokenChatAsync(token);
        return (true, null);
    }

    public async Task<(bool Sucesso, string? Erro)> VerificarAdminAsync(string senhaAdmin)
    {
        if (string.IsNullOrEmpty(auth.Token))
            return (false, "Sessão inválida. Faça login novamente.");

        await ConfigurarHeaderAsync(NivelToken.Chat);

        var resp = await http.PostAsJsonAsync("api/empresas/verificar-admin",
            new { SenhaAdmin = senhaAdmin });

        if (!resp.IsSuccessStatusCode)
            return (false, await ExtrairErroAsync(resp));

        var token = await resp.Content.ReadFromJsonAsync<TokenResponse>(_json);
        if (token == null) return (false, "Resposta inválida do servidor.");

        await storage.SetItemAsync("tokenAdmin", token.Token);
        auth.AdminToken = token.Token;
        return (true, null);
    }

    public async Task<(bool Sucesso, string? Erro)> VerificarColaboradoresAsync(string senhaAdmin)
    {
        await ConfigurarHeaderAsync(NivelToken.Chat);

        var resp = await http.PostAsJsonAsync("api/empresas/verificar-colaboradores",
            new { SenhaAdmin = senhaAdmin });

        if (!resp.IsSuccessStatusCode)
            return (false, await ExtrairErroAsync(resp));

        var token = await resp.Content.ReadFromJsonAsync<TokenResponse>(_json);
        if (token == null) return (false, "Resposta inválida do servidor.");

        await storage.SetItemAsync("tokenColaboradores", token.Token);
        auth.ColaboradoresToken = token.Token;
        return (true, null);
    }

    public async Task<(RespostaChatDto? Resposta, string? Erro)> EnviarMensagemAsync(string mensagem, Guid? conversaId)
    {
        await ConfigurarHeaderAsync();

        HttpResponseMessage resp;
        try
        {
            resp = await http.PostAsJsonAsync("api/chat",
                new { Mensagem = mensagem, ConversaId = conversaId });
        }
        catch (Exception ex)
        {
            return (null, $"Não foi possível conectar à API: {ex.Message}");
        }

        if (resp.IsSuccessStatusCode)
        {
            var dto = await resp.Content.ReadFromJsonAsync<RespostaChatDto>(_json);
            return (dto, null);
        }

        var erro = resp.StatusCode == System.Net.HttpStatusCode.Unauthorized
            ? "Sua sessão expirou. Faça login novamente."
            : await ExtrairErroAsync(resp);

        return (null, erro);
    }

    public async Task<List<ConversaResumo>?> ObterHistoricoAsync()
    {
        await ConfigurarHeaderAsync();
        try
        {
            return await http.GetFromJsonAsync<List<ConversaResumo>>("api/chat/historico", _json);
        }
        catch { return []; }
    }

    public async Task<ConversaCompleta?> ObterConversaAsync(Guid id)
    {
        await ConfigurarHeaderAsync();
        return await http.GetFromJsonAsync<ConversaCompleta>($"api/chat/historico/{id}", _json);
    }

    public async Task<UploadResultado> UploadDocumentoAsync(IBrowserFile arquivo)
    {
        await ConfigurarHeaderAsync(NivelToken.Admin);

        using var content = new MultipartFormDataContent();
        var stream = arquivo.OpenReadStream(maxAllowedSize: 20 * 1024 * 1024);
        var streamContent = new StreamContent(stream);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(
            arquivo.ContentType ?? "application/octet-stream");
        content.Add(streamContent, "arquivo", arquivo.Name);

        HttpResponseMessage resp;
        try
        {
            resp = await http.PostAsync("api/documentos/upload", content);
        }
        catch (Exception ex)
        {
            return new UploadResultado(false, $"Não foi possível conectar à API: {ex.Message}");
        }

        if (resp.IsSuccessStatusCode)
            return new UploadResultado(true, null);

        return new UploadResultado(false, await ExtrairErroAsync(resp));
    }

    public async Task<List<DocumentoDto>?> ListarDocumentosAsync()
    {
        await ConfigurarHeaderAsync(NivelToken.Admin);
        try
        {
            return await http.GetFromJsonAsync<List<DocumentoDto>>("api/documentos", _json);
        }
        catch { return []; }
    }

    public async Task<bool> RemoverDocumentoAsync(Guid id)
    {
        await ConfigurarHeaderAsync(NivelToken.Admin);
        var resp = await http.DeleteAsync($"api/documentos/{id}");
        return resp.IsSuccessStatusCode;
    }

    public async Task<List<FuncionarioDto>?> ListarColaboradoresAsync(string? busca)
    {
        await ConfigurarHeaderAsync(NivelToken.Colaboradores);
        try
        {
            var url = string.IsNullOrWhiteSpace(busca)
                ? "api/funcionarios"
                : $"api/funcionarios?busca={Uri.EscapeDataString(busca)}";
            return await http.GetFromJsonAsync<List<FuncionarioDto>>(url, _json);
        }
        catch { return []; }
    }

    private async Task SalvarTokenChatAsync(TokenResponse token)
    {
        await storage.SetItemAsync("token", token.Token);
        await storage.SetItemAsync("nome", token.Nome);
        await storage.SetItemAsync("empresaId", token.EmpresaId.ToString());
        await storage.SetItemAsync("cnpj", token.Cnpj);
        await storage.SetItemAsync("usuarioId", token.UsuarioId?.ToString() ?? "");
        await storage.SetItemAsync("nomeUsuario", token.NomeUsuario ?? "");
        await storage.SetItemAsync("supervisor", token.Supervisor.ToString());

        auth.Token = token.Token;
        auth.EmpresaNome = token.Nome;
        auth.EmpresaId = token.EmpresaId;
        auth.Cnpj = token.Cnpj;
        auth.UsuarioId = token.UsuarioId;
        auth.NomeUsuario = token.NomeUsuario;
        auth.Supervisor = token.Supervisor;
    }

    public async Task LimparAdminAsync()
    {
        await storage.RemoveItemAsync("tokenAdmin");
        auth.BloquearAdmin();
    }

    public async Task LimparColaboradoresAsync()
    {
        await storage.RemoveItemAsync("tokenColaboradores");
        auth.BloquearColaboradores();
    }

    public async Task SairAsync()
    {
        await storage.RemoveItemAsync("token");
        await storage.RemoveItemAsync("nome");
        await storage.RemoveItemAsync("empresaId");
        await storage.RemoveItemAsync("cnpj");
        await storage.RemoveItemAsync("usuarioId");
        await storage.RemoveItemAsync("nomeUsuario");
        await storage.RemoveItemAsync("supervisor");
        await storage.RemoveItemAsync("tokenAdmin");
        await storage.RemoveItemAsync("tokenColaboradores");
        auth.Sair();
    }

    private async Task<string> ExtrairErroAsync(HttpResponseMessage resp)
    {
        string? erro = null;
        try
        {
            var corpo = await resp.Content.ReadFromJsonAsync<ErroApiDto>(_json);
            erro = corpo?.Erro ?? corpo?.Detalhe;
        }
        catch {  }

        return erro ?? $"A API respondeu com erro {(int)resp.StatusCode} ({resp.StatusCode}).";
    }
}

public record UploadResultado(bool Sucesso, string? Erro);
public record FuncionarioNovo(string Nome, string Cpf, string EmailCorporativo, string Senha, bool Supervisor);
public record ValidarCnpjResultado(bool Valido, bool Ativo, string? RazaoSocial, string? Erro);

public class ValidarCnpjResponse
{
    public bool Valido { get; set; }
    public bool Ativo { get; set; }
    public string? RazaoSocial { get; set; }
    public string? Mensagem { get; set; }
}

public class ErroApiDto
{
    public string? Erro { get; set; }
    public string? Detalhe { get; set; }
}

public class TokenResponse
{
    public string Token { get; set; } = "";
    public Guid EmpresaId { get; set; }
    public string Nome { get; set; } = "";
    public string Cnpj { get; set; } = "";
    public Guid? UsuarioId { get; set; }
    public string? NomeUsuario { get; set; }
    public bool Supervisor { get; set; }
    public bool AdminDocumentos { get; set; }
    public bool ColaboradoresAdmin { get; set; }
}

public class FuncionarioDto
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = "";
    public string Cpf { get; set; } = "";
    public string EmailCorporativo { get; set; } = "";
    public bool Supervisor { get; set; }
    public DateTime CriadoEm { get; set; }
}
