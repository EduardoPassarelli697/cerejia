using Cerejia.Api.Data;
using Cerejia.Api.DTOs;
using Cerejia.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Cerejia.Api.Controllers;

[ApiController]
[Route("api/chat")]
[Authorize]
public class ChatController(RagService ragService, AppDbContext db, ILogger<ChatController> logger) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<RespostaChatDto>> EnviarMensagem(
        [FromBody] EnviarMensagemDto dto,
        CancellationToken ct)
    {
        var empresaId = ObterEmpresaId();
        var usuarioId = ObterUsuarioId();
        if (empresaId == null || usuarioId == null) return Unauthorized();

        try
        {
            var resposta = await ragService.ResponderAsync(
                empresaId.Value, usuarioId.Value, dto, EhSupervisor(), ct);
            return Ok(resposta);
        }
        catch (Exception ex)
        {

            logger.LogError(ex, "Erro ao processar mensagem de chat.");
            return StatusCode(500, new { erro = "Erro ao processar mensagem.", detalhe = ex.Message });
        }
    }

    [HttpGet("historico")]
    public async Task<ActionResult<List<ConversaDto>>> ObterHistorico(CancellationToken ct)
    {
        var empresaId = ObterEmpresaId();
        var usuarioId = ObterUsuarioId();
        if (empresaId == null || usuarioId == null) return Unauthorized();

        var conversas = await db.Conversas
            .Where(c => c.EmpresaId == empresaId && c.UsuarioId == usuarioId)
            .OrderByDescending(c => c.CriadoEm)
            .Take(50)
            .Select(c => new ConversaDto(
                c.Id,
                c.Titulo,
                c.CriadoEm,
                new List<MensagemDto>()))
            .ToListAsync(ct);

        return Ok(conversas);
    }

    [HttpGet("historico/{conversaId}")]
    public async Task<ActionResult<ConversaDto>> ObterConversa(Guid conversaId, CancellationToken ct)
    {
        var empresaId = ObterEmpresaId();
        var usuarioId = ObterUsuarioId();
        if (empresaId == null || usuarioId == null) return Unauthorized();

        var conversa = await db.Conversas
            .Include(c => c.Mensagens.OrderBy(m => m.CriadoEm))
            .FirstOrDefaultAsync(c => c.Id == conversaId && c.EmpresaId == empresaId && c.UsuarioId == usuarioId, ct);

        if (conversa == null) return NotFound();

        return Ok(new ConversaDto(
            conversa.Id,
            conversa.Titulo,
            conversa.CriadoEm,
            conversa.Mensagens.Select(m => new MensagemDto(
                m.Id, m.Papel, m.Conteudo, m.CriadoEm)).ToList()
        ));
    }

    private Guid? ObterEmpresaId()
    {
        var claim = User.FindFirst("empresa_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    private Guid? ObterUsuarioId()
    {
        var claim = User.FindFirst("usuario_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }

    private bool EhSupervisor() =>
        User.FindFirst("supervisor")?.Value == "true";
}

[ApiController]
[Route("api/documentos")]
[Authorize(Policy = "AdminDocumentos")]
public class DocumentosController(DocumentoService documentoService) : ControllerBase
{
    [HttpPost("upload")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<DocumentoDto>> Upload(
        IFormFile arquivo,
        CancellationToken ct)
    {
        var empresaId = ObterEmpresaId();
        if (empresaId == null) return Unauthorized();

        if (arquivo == null || arquivo.Length == 0)
            return BadRequest(new { erro = "Nenhum arquivo enviado." });

        try
        {
            var doc = await documentoService.IniciarIndexacaoAsync(empresaId.Value, arquivo, ct);
            return Ok(doc);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { erro = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { erro = "Erro interno ao processar documento.", detalhe = ex.Message });
        }
    }

    [HttpGet]
    public async Task<ActionResult<List<DocumentoDto>>> Listar(CancellationToken ct)
    {
        var empresaId = ObterEmpresaId();
        if (empresaId == null) return Unauthorized();

        return Ok(await documentoService.ListarAsync(empresaId.Value, ct));
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Remover(Guid id, CancellationToken ct)
    {
        var empresaId = ObterEmpresaId();
        if (empresaId == null) return Unauthorized();

        try
        {
            await documentoService.RemoverAsync(empresaId.Value, id, ct);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { erro = ex.Message });
        }
    }

    private Guid? ObterEmpresaId()
    {
        var claim = User.FindFirst("empresa_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : null;
    }
}
