namespace Cerejia.Web.Services;

public class AuthStateService
{

    public string? Token { get; set; }
    public string? EmpresaNome { get; set; }
    public Guid? EmpresaId { get; set; }
    public string? Cnpj { get; set; }
    public Guid? UsuarioId { get; set; }
    public string? NomeUsuario { get; set; }
    public bool Supervisor { get; set; }

    public string? AdminToken { get; set; }

    public string? ColaboradoresToken { get; set; }

    public bool EstaAutenticado => !string.IsNullOrEmpty(Token);
    public bool AdminDesbloqueado => !string.IsNullOrEmpty(AdminToken);
    public bool ColaboradoresDesbloqueado => !string.IsNullOrEmpty(ColaboradoresToken);

    public void Sair()
    {
        Token = null;
        EmpresaNome = null;
        EmpresaId = null;
        Cnpj = null;
        UsuarioId = null;
        NomeUsuario = null;
        Supervisor = false;
        AdminToken = null;
        ColaboradoresToken = null;
    }

    public void BloquearAdmin() => AdminToken = null;
    public void BloquearColaboradores() => ColaboradoresToken = null;
}
