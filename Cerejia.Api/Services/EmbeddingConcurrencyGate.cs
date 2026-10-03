namespace Cerejia.Api.Services;

public class EmbeddingConcurrencyGate
{
    private readonly SemaphoreSlim _semaforo;

    public EmbeddingConcurrencyGate(IConfiguration configuration)
    {

        var limite = configuration.GetValue<int?>("Ollama:ConcorrenciaMaxima") ?? 1;
        _semaforo = new SemaphoreSlim(limite, limite);
    }

    public Task EsperarAsync(CancellationToken ct = default) => _semaforo.WaitAsync(ct);

    public void Liberar() => _semaforo.Release();
}
