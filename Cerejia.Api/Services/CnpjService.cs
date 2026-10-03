using System.Text.Json;

namespace Cerejia.Api.Services;

public record ResultadoValidacaoCnpj(bool Valido, bool Ativo, string? RazaoSocial, string? Mensagem);

public class CnpjService(IHttpClientFactory httpClientFactory, ILogger<CnpjService> logger)
{
    public async Task<ResultadoValidacaoCnpj> ValidarAsync(string cnpjBruto, CancellationToken ct = default)
    {
        var cnpj = LimparCnpj(cnpjBruto);

        if (!TemFormatoValido(cnpj))
            return new ResultadoValidacaoCnpj(false, false, null, "CNPJ inválido.");

        try
        {
            var http = httpClientFactory.CreateClient("BrasilApi");
            HttpResponseMessage resposta;

            const int maxTentativas = 3;
            var tentativa = 1;
            while (true)
            {
                resposta = await http.GetAsync($"/api/cnpj/v1/{cnpj}", ct);

                if (resposta.StatusCode != System.Net.HttpStatusCode.TooManyRequests || tentativa >= maxTentativas)
                    break;

                logger.LogWarning(
                    "BrasilAPI retornou 429 (limite de requisições) — tentativa {Tentativa}/{Max}. Aguardando para tentar de novo.",
                    tentativa, maxTentativas);
                await Task.Delay(TimeSpan.FromSeconds(2 * tentativa), ct);
                tentativa++;
            }

            using var _ = resposta;

            if (resposta.StatusCode == System.Net.HttpStatusCode.NotFound)
                return new ResultadoValidacaoCnpj(false, false, null, "CNPJ não encontrado na Receita Federal.");

            if (resposta.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                logger.LogWarning("BrasilAPI seguiu retornando 429 após {Max} tentativas.", maxTentativas);
                return new ResultadoValidacaoCnpj(false, false, null,
                    "O serviço de verificação de CNPJ está com muitas requisições agora. Aguarde 1-2 minutos e tente novamente.");
            }

            if (!resposta.IsSuccessStatusCode)
            {
                logger.LogWarning("BrasilAPI retornou {Status} ao consultar CNPJ.", resposta.StatusCode);
                return new ResultadoValidacaoCnpj(false, false, null,
                    "Não foi possível verificar o CNPJ agora. Tente novamente em instantes.");
            }

            using var stream = await resposta.Content.ReadAsStreamAsync(ct);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = json.RootElement;

            var situacao = root.TryGetProperty("descricao_situacao_cadastral", out var s) ? s.GetString() : null;
            var razaoSocial = root.TryGetProperty("razao_social", out var r) ? r.GetString() : null;
            var ativo = string.Equals(situacao, "ATIVA", StringComparison.OrdinalIgnoreCase);

            return new ResultadoValidacaoCnpj(
                Valido: true,
                Ativo: ativo,
                RazaoSocial: razaoSocial,
                Mensagem: ativo ? null : "CNPJ não está ativo.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Falha ao consultar a BrasilAPI para validar CNPJ.");
            return new ResultadoValidacaoCnpj(false, false, null,
                "Não foi possível verificar o CNPJ agora (sem conexão com a Receita Federal). Tente novamente.");
        }
    }

    private static string LimparCnpj(string cnpj) =>
        new(cnpj.Where(char.IsDigit).ToArray());

    private static bool TemFormatoValido(string cnpj)
    {
        if (cnpj.Length != 14 || cnpj.Distinct().Count() == 1)
            return false;

        int[] multiplicadores1 = [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];
        int[] multiplicadores2 = [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2];

        var digitos = cnpj.Select(c => c - '0').ToArray();

        var soma1 = digitos.Take(12).Select((d, i) => d * multiplicadores1[i]).Sum();
        var resto1 = soma1 % 11;
        var dv1 = resto1 < 2 ? 0 : 11 - resto1;
        if (digitos[12] != dv1) return false;

        var soma2 = digitos.Take(13).Select((d, i) => d * multiplicadores2[i]).Sum();
        var resto2 = soma2 % 11;
        var dv2 = resto2 < 2 ? 0 : 11 - resto2;
        if (digitos[13] != dv2) return false;

        return true;
    }
}
