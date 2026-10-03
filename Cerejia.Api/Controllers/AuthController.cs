using Cerejia.Api.DTOs;
using Cerejia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Cerejia.Api.Controllers;

[ApiController]
[Route("api/empresas")]
public class AuthController(AuthService authService, CnpjService cnpjService) : ControllerBase
{

    [HttpGet("validar-cnpj/{cnpj}")]
    public async Task<ActionResult<ValidarCnpjResponseDto>> ValidarCnpj(string cnpj, CancellationToken ct)
    {
        var resultado = await cnpjService.ValidarAsync(cnpj, ct);
        return Ok(new ValidarCnpjResponseDto(
            resultado.Valido, resultado.Ativo, resultado.RazaoSocial, resultado.Mensagem));
    }

    [HttpPost("cadastro")]
    public async Task<ActionResult<TokenResponseDto>> Cadastrar(
        [FromBody] CadastroEmpresaDto dto, CancellationToken ct)
    {
        try
        {
            return Ok(await authService.CadastrarEmpresaAsync(dto, ct));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { erro = ex.Message });
        }
    }

    [HttpPost("entrar")]
    public async Task<ActionResult<TokenResponseDto>> Entrar(
        [FromBody] EntrarDto dto, CancellationToken ct)
    {
        try
        {
            return Ok(await authService.EntrarAsync(dto, ct));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { erro = ex.Message });
        }
    }

    [HttpPost("verificar-admin")]
    [Authorize]
    public async Task<ActionResult<TokenResponseDto>> VerificarAdmin(
        [FromBody] VerificarAdminDto dto, CancellationToken ct)
    {
        var usuarioIdClaim = User.FindFirst("usuario_id")?.Value;
        if (!Guid.TryParse(usuarioIdClaim, out var usuarioId))
            return Unauthorized(new { erro = "Sessão inválida." });

        try
        {
            return Ok(await authService.VerificarAdminAsync(usuarioId, dto, ct));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { erro = ex.Message });
        }
    }

    [HttpPost("verificar-colaboradores")]
    [Authorize]
    public async Task<ActionResult<TokenResponseDto>> VerificarColaboradores(
        [FromBody] VerificarColaboradoresDto dto, CancellationToken ct)
    {
        var usuarioIdClaim = User.FindFirst("usuario_id")?.Value;
        if (!Guid.TryParse(usuarioIdClaim, out var usuarioId))
            return Unauthorized(new { erro = "Sessão inválida." });

        try
        {
            return Ok(await authService.VerificarColaboradoresAsync(usuarioId, dto, ct));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { erro = ex.Message });
        }
    }
}
