using System.Text;
using Cerejia.Api.Data;
using Cerejia.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.Ollama;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

ThreadPool.GetMinThreads(out var minWorker, out var minIO);
ThreadPool.SetMinThreads(Math.Max(minWorker, 16), Math.Max(minIO, 16));

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Postgres"),
        o => o.UseVector()
    )
);

var ollamaUrl = builder.Configuration["Ollama:Url"] ?? "http://localhost:11434";
var modeloChat = builder.Configuration["Ollama:ModeloChat"] ?? "phi3";

var modeloEmbedding = builder.Configuration["Ollama:ModeloEmbedding"] ?? "all-minilm";

var kernelBuilder = Kernel.CreateBuilder();

var ollamaChatHttpClient = new HttpClient
{
    BaseAddress = new Uri(ollamaUrl),
    Timeout = TimeSpan.FromMinutes(5)
};
var ollamaEmbeddingHttpClient = new HttpClient
{
    BaseAddress = new Uri(ollamaUrl),
    Timeout = TimeSpan.FromMinutes(3)
};

kernelBuilder.AddOllamaChatCompletion(
    modelId: modeloChat,
    httpClient: ollamaChatHttpClient
);

kernelBuilder.AddOllamaTextEmbeddingGeneration(
    modelId: modeloEmbedding,
    httpClient: ollamaEmbeddingHttpClient
);

builder.Services.AddSingleton(kernelBuilder.Build());

builder.Services.AddSingleton(sp =>
{
    var kernel = sp.GetRequiredService<Kernel>();
    return kernel.GetRequiredService<Microsoft.SemanticKernel.Embeddings.ITextEmbeddingGenerationService>();
});

builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<RagService>();
builder.Services.AddScoped<DocumentoService>();

builder.Services.AddSingleton<EmbeddingConcurrencyGate>();

builder.Services.AddHttpClient("BrasilApi", client =>
{
    client.BaseAddress = new Uri("https://brasilapi.com.br");
    client.Timeout = TimeSpan.FromSeconds(10);

    client.DefaultRequestHeaders.UserAgent.ParseAdd("CerejIA/1.0 (+https://github.com/cerejia)");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
});
builder.Services.AddScoped<CnpjService>();

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key não configurado.");

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer           = true,
            ValidateAudience         = true,
            ValidateLifetime         = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer              = builder.Configuration["Jwt:Issuer"],
            ValidAudience            = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization(options =>
{

    options.AddPolicy("AdminDocumentos", policy =>
        policy.RequireClaim("admin_documentos", "true"));

    options.AddPolicy("ColaboradoresAdmin", policy =>
        policy.RequireClaim("colaboradores_admin", "true"));
});

builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:5174", "https://localhost:7268")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "CEREJ.IA API",
        Version = "v1",
        Description = "Assistente corporativo com RAG, Phi-3 e pgvector"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Informe: Bearer {seu_token}"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            []
        }
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "CEREJ.IA API v1");
        c.RoutePrefix = "swagger";
    });
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.EnsureCreatedAsync();

    var travados = await dbContext.Documentos
        .Where(d => d.Status == "processando")
        .ToListAsync();

    if (travados.Count > 0)
    {
        foreach (var doc in travados)
            doc.Status = "erro";

        await dbContext.SaveChangesAsync();

        app.Logger.LogWarning(
            "{Count} documento(s) estavam travados em \"processando\" (provavelmente por causa de um reinício durante o processamento) e foram marcados como \"erro\". Reenvie-os se necessário.",
            travados.Count);
    }
}

app.Run();
