using Cerejia.Api.Data;
using Cerejia.Api.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cerejia.Api.Controllers;

[ApiController]
[Route("api/funcionarios")]
[Authorize(Policy = "ColaboradoresAdmin")]
public class FuncionariosController(AppDbContext db) : ControllerBase
{

    [HttpGet]
    public async Task<ActionResult<List<FuncionarioDto>>> Listar(
        [FromQuery] string? busca, CancellationToken ct)
    {
        var empresaId = ObterEmpresaId();
        if (empresaId == null) return Unauthorized();

        var query = db.Usuarios.Where(u => u.EmpresaId == empresaId && u.Ativo);

        if (!string.IsNullOrWhiteSpace(busca))
        {
            var termo = busca.Trim().ToLower();
            query = query.Where(u =>
                u.Nome.ToLower().Contains(termo) ||
                u.EmailCorporativo.ToLower().Contains(termo) ||
                u.Cpf.Contains(termo));
        }

        var lista = await query
            .OrderBy(u => u.Nome)
            .Select(u => new FuncionarioDto(u.Id, u.Nome, u.Cpf, u.EmailCorporativo, u.Supervisor, u.CriadoEm))
            .ToListAsync(ct);

        return Ok(lista);
    }

    private Guid? ObterEmpresaId()
    {
        var claim = User.FindFirst("empresa_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}
