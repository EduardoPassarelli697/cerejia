using Cerejia.Api.Data;
using Cerejia.Api.DTOs;
using Cerejia.Api.Models;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel.Embeddings;
using Npgsql;
using Pgvector;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Cerejia.Api.Services;

public class DocumentoService(
    AppDbContext db,
    ILogger<DocumentoService> logger,
    IServiceScopeFactory scopeFactory,
    EmbeddingConcurrencyGate concurrencyGate)
{

    private const int TamanhoChunk = 800;

    private const int SobreposicaoChunk = 150;

    private const int TamanhoLote = 5;

    public async Task<DocumentoDto> IniciarIndexacaoAsync(
        Guid empresaId,
        IFormFile arquivo,
        CancellationToken ct = default)
    {
        var extensao = Path.GetExtension(arquivo.FileName).ToLower().TrimStart('.');

        if (!new[] { "pdf", "docx", "txt" }.Contains(extensao))
            throw new InvalidOperationException("Tipo de arquivo não suportado. Use PDF, DOCX ou TXT.");

        using var memStream = new MemoryStream();
        await arquivo.CopyToAsync(memStream, ct);
        var bytes = memStream.ToArray();

        var documento = new Documento
        {
            EmpresaId = empresaId,
            NomeArquivo = arquivo.FileName,
            TipoArquivo = extensao,
            TamanhoKb = (int)(arquivo.Length / 1024),
            Status = "processando"
        };
        db.Documentos.Add(documento);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Upload recebido, iniciando processamento em segundo plano: {Nome}", arquivo.FileName);

        _ = Task.Run(() => ProcessarEmSegundoPlanoAsync(documento.Id, empresaId, arquivo.FileName, extensao, bytes));

        return ToDto(documento);
    }

    private async Task ProcessarEmSegundoPlanoAsync(
        Guid documentoId, Guid empresaId, string nomeArquivo, string extensao, byte[] bytes)
    {

        using var scope = scopeFactory.CreateScope();
        var dbBg = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var embeddingBg = scope.ServiceProvider.GetRequiredService<ITextEmbeddingGenerationService>();
        var loggerBg = scope.ServiceProvider.GetRequiredService<ILogger<DocumentoService>>();

        var documento = await dbBg.Documentos.FirstOrDefaultAsync(d => d.Id == documentoId);
        if (documento == null) return;

        try
        {

            await ProcessarInternoAsync(documento, documentoId, empresaId, nomeArquivo, extensao, bytes, dbBg, embeddingBg, loggerBg)
                .WaitAsync(TimeSpan.FromMinutes(10));
        }
        catch (TimeoutException)
        {
            loggerBg.LogError(
                "Timeout (10min) processando documento {Id} ({Nome}). Marcando como erro.",
                documento.Id, nomeArquivo);

            var aindaExisteAposTimeout = await dbBg.Documentos.AnyAsync(d => d.Id == documento.Id);
            if (aindaExisteAposTimeout)
            {
                documento.Status = "erro";
                await dbBg.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {

            if (EhViolacaoDeChaveEstrangeira(ex))
            {
                loggerBg.LogWarning(
                    "Documento {Id} foi excluído durante o processamento em segundo plano — descartando os chunks já gerados.",
                    documento.Id);
                return;
            }

            loggerBg.LogError(ex, "Erro ao indexar documento {Id}", documento.Id);

            var aindaExiste = await dbBg.Documentos.AnyAsync(d => d.Id == documento.Id);
            if (!aindaExiste) return;

            documento.Status = "erro";
            await dbBg.SaveChangesAsync();
        }
    }

    private async Task ProcessarInternoAsync(
        Documento documento, Guid documentoId, Guid empresaId, string nomeArquivo, string extensao, byte[] bytes,
        AppDbContext dbBg, ITextEmbeddingGenerationService embeddingBg, ILogger loggerBg)
    {

        using var stream = new MemoryStream(bytes);
        var texto = extensao switch
        {
            "pdf"  => ExtrairTextoPdf(stream),
            "docx" => ExtrairTextoDocx(stream),
            _      => await new StreamReader(stream).ReadToEndAsync()
        };

        if (string.IsNullOrWhiteSpace(texto))
            throw new InvalidOperationException("Não foi possível extrair texto do documento.");

        var chunks = DividirEmChunks(texto);
        loggerBg.LogInformation("{Count} chunks gerados para {Nome}.", chunks.Count, nomeArquivo);

        var lotes = new List<List<string>>();
        for (int i = 0; i < chunks.Count; i += TamanhoLote)
            lotes.Add(chunks.Skip(i).Take(TamanhoLote).ToList());

        var entidades = new DocumentoChunk?[chunks.Count];
        var progresso = 0;

        var tarefas = lotes.Select(async (lote, loteIndex) =>
        {

            await concurrencyGate.EsperarAsync();
            try
            {
                var embeddings = await GerarEmbeddingsComRetryAsync(embeddingBg, lote, loggerBg, nomeArquivo);
                for (int j = 0; j < lote.Count; j++)
                {
                    var chunkIndex = loteIndex * TamanhoLote + j;
                    entidades[chunkIndex] = new DocumentoChunk
                    {
                        DocumentoId = documentoId,
                        EmpresaId = empresaId,
                        Conteudo = lote[j],
                        Embedding = new Vector(embeddings[j].ToArray()),
                        ChunkIndex = chunkIndex
                    };
                }
                var feitos = Interlocked.Increment(ref progresso);
                loggerBg.LogInformation("Lote {Feitos}/{Total} indexado ({Nome}).", feitos, lotes.Count, nomeArquivo);
            }
            finally
            {
                concurrencyGate.Liberar();
            }
        });

        await Task.WhenAll(tarefas);

        dbBg.DocumentoChunks.AddRange(entidades.Where(e => e != null)!);

        documento.Status = "indexado";
        await dbBg.SaveChangesAsync();

        loggerBg.LogInformation("Documento indexado com sucesso: {Id}", documento.Id);
    }

    private static bool EhViolacaoDeChaveEstrangeira(Exception ex) =>
        ex is DbUpdateException { InnerException: PostgresException { SqlState: "23503" } };

    public async Task<List<DocumentoDto>> ListarAsync(Guid empresaId, CancellationToken ct = default)
    {
        var documentos = await db.Documentos
            .Where(d => d.EmpresaId == empresaId)
            .OrderByDescending(d => d.CriadoEm)
            .ToListAsync(ct);

        return documentos.Select(ToDto).ToList();
    }

    public async Task RemoverAsync(Guid empresaId, Guid documentoId, CancellationToken ct = default)
    {
        var doc = await db.Documentos
            .FirstOrDefaultAsync(d => d.Id == documentoId && d.EmpresaId == empresaId, ct)
            ?? throw new KeyNotFoundException("Documento não encontrado.");

        if (doc.Status == "processando")
            throw new InvalidOperationException("Aguarde o processamento terminar antes de remover este documento.");

        db.Documentos.Remove(doc);
        await db.SaveChangesAsync(ct);
    }

    private static async Task<IList<ReadOnlyMemory<float>>> GerarEmbeddingsComRetryAsync(
        ITextEmbeddingGenerationService embeddingBg,
        List<string> lote,
        ILogger loggerBg,
        string nomeArquivo)
    {
        const int maxTentativas = 3;
        for (var tentativa = 1; ; tentativa++)
        {
            try
            {
                return await embeddingBg.GenerateEmbeddingsAsync(lote).WaitAsync(TimeSpan.FromMinutes(2));
            }
            catch (Exception ex) when (tentativa < maxTentativas)
            {
                var espera = TimeSpan.FromSeconds(Math.Pow(2, tentativa));
                loggerBg.LogWarning(ex,
                    "Falha ao gerar embeddings (tentativa {Tentativa}/{Max}) para {Nome}. Tentando novamente em {Espera}s.",
                    tentativa, maxTentativas, nomeArquivo, espera.TotalSeconds);
                await Task.Delay(espera);
            }
        }
    }

    private static string ExtrairTextoPdf(Stream stream)
    {
        using var pdf = PdfDocument.Open(stream);
        var textos = pdf.GetPages()
            .Select(p => p.Text)
            .Where(t => !string.IsNullOrWhiteSpace(t));
        return string.Join("\n\n", textos);
    }

    private static string ExtrairTextoDocx(Stream stream)
    {
        using var doc = WordprocessingDocument.Open(stream, false);
        var body = doc.MainDocumentPart?.Document?.Body;
        if (body == null) return "";

        var paragrafos = body.Descendants<Paragraph>()
            .Select(p => p.InnerText)
            .Where(t => !string.IsNullOrWhiteSpace(t));

        return string.Join("\n\n", paragrafos);
    }

    private static List<string> DividirEmChunks(string texto)
    {
        var chunks = new List<string>();
        var inicio = 0;

        const int maxIteracoes = 20_000;
        var iteracoes = 0;

        while (inicio < texto.Length)
        {
            iteracoes++;
            if (iteracoes > maxIteracoes) break;

            var fim = Math.Min(inicio + TamanhoChunk, texto.Length);

            if (fim < texto.Length)
            {
                var quebraParagrafo = texto.LastIndexOf('\n', fim, Math.Min(100, fim - inicio));
                var quebraSentenca = texto.LastIndexOf(". ", fim, Math.Min(100, fim - inicio));
                var melhorQuebra = Math.Max(quebraParagrafo, quebraSentenca);
                if (melhorQuebra > inicio + TamanhoChunk / 2)
                    fim = melhorQuebra + 1;
            }

            var chunk = texto[inicio..fim].Trim();
            if (chunk.Length > 50)
                chunks.Add(chunk);

            if (fim >= texto.Length)
                inicio = fim;
            else
            {
                inicio = fim - SobreposicaoChunk;
                if (inicio <= 0) inicio = fim;
            }
        }

        return chunks;
    }

    private static DocumentoDto ToDto(Documento d) => new(
        d.Id, d.NomeArquivo, d.TipoArquivo, d.Status, d.TamanhoKb, d.CriadoEm);
}
