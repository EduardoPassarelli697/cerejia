# CEREJ.IA

Assistente corporativo com RAG (Retrieval-Augmented Generation), rodando 100% local via Ollama.
Cada empresa tem sua base de documentos isolada; o chat responde com base apenas nos documentos daquela empresa.

## Arquitetura

- **Cerejia.Api** — backend ASP.NET Core 8. Autenticação por empresa (CNPJ), RAG com Semantic Kernel + Ollama, PostgreSQL + pgvector.
- **Cerejia.Web** — frontend Blazor WebAssembly.
- **Ollama** (local) — `phi3` para o chat, `all-minilm` para os embeddings.
- **PostgreSQL + pgvector** — armazenamento e busca vetorial dos documentos.

### Modelo de acesso

Cada **empresa** se cadastra com:
- Nome
- CNPJ (validado automaticamente como ativo na Receita Federal, via BrasilAPI)
- Uma senha de admin
- A lista de colaboradores (CPF, e-mail corporativo, senha, e se é supervisor)

O **login no chat** é individual: CNPJ da empresa + e-mail corporativo + senha do colaborador.

A aba de **Documentos** (upload/exclusão) exige, adicionalmente, a senha do admin.

A aba **Administrar colaboradores** exige a senha do admin **e** que o colaborador logado
seja supervisor — as duas condições precisam ser verdadeiras.

## Pré-requisitos

1. **.NET 8 SDK** — https://dotnet.microsoft.com/download
2. **PostgreSQL 15+** com a extensão `pgvector` instalada
3. **Ollama** — https://ollama.com
   ```
   ollama pull phi3
   ollama pull all-minilm
   ```

## Configuração

### 1. Banco de dados

Crie um banco chamado `cerejia` e rode o script:

```
sql/01_create_database.sql
```

(pelo pgAdmin: conecte no banco `cerejia`, abra o Query Tool, cole o conteúdo do arquivo e execute)

### 2. `Cerejia.Api/appsettings.json`

Ajuste:
- `ConnectionStrings:Postgres` — usuário/senha do seu PostgreSQL
- `Jwt:Key` — troque por uma chave secreta própria (mín. 32 caracteres)

### 3. Rodar

Em dois terminais separados:

```
cd Cerejia.Api
dotnet restore
dotnet run
```

```
cd Cerejia.Web
dotnet restore
dotnet run
```

Acesse **http://localhost:5174**.

## Fluxo de uso

1. Na tela de login, clique em **"Cadastrar nova empresa"** — informe nome, CNPJ (real, será validado), a senha de admin, e quantos colaboradores deseja cadastrar.
2. Preencha CPF, e-mail corporativo, senha e se é supervisor para cada colaborador. Ao concluir, você já entra automaticamente logado como o primeiro colaborador cadastrado.
3. Para enviar documentos, clique em **"Gerenciar documentos"** e informe a senha do admin quando pedido.
4. Para ver a lista de colaboradores, clique em **"Administrar colaboradores"** — só funciona se o colaborador logado for supervisor **e** a senha do admin for informada corretamente.
5. Depois de enviar um documento (PDF, DOCX ou TXT), aguarde o status mudar de "processando" para "indexado" — a partir daí o chat já responde com base nele.

## Notas técnicas

- **Embeddings**: `all-minilm` (384 dimensões) — escolhido por rodar rápido em CPU, sem GPU dedicada.
- **Concorrência**: só uma chamada de embedding por vez em toda a aplicação (`Ollama:ConcorrenciaMaxima`
  em `appsettings.json`), para não sobrecarregar máquinas sem GPU. Pode ser aumentado com cautela.
- **Retry automático**: falhas transitórias do Ollama durante a indexação são reenviadas até 3 vezes.
- **Autorização em dois níveis**: o backend exige a claim `admin_documentos` (emitida só depois da
  verificação da senha) para qualquer endpoint de documento — protegido mesmo contra chamadas diretas
  na API, não só escondido na tela.

## Troubleshooting

**Computador trava ao subir um documento** — confirme que `Ollama:ConcorrenciaMaxima` está em `1`
em `appsettings.json`, e que o Ollama está usando GPU se você tiver uma (`ollama ps`).

**Erro "expected N dimensions, not M"** — o modelo de embedding configurado não bate com a dimensão
da coluna `embedding` no banco. Se trocar de modelo, ajuste `vector(N)` em `AppDbContext.cs` e recrie
a tabela `documento_chunks`.

**"CNPJ não está ativo"** — a Receita Federal (via BrasilAPI) retornou situação cadastral diferente de
"ATIVA" para o CNPJ informado. Confirme o número ou use um CNPJ de teste realmente ativo.

**Chat demora muito / dá timeout** — modelos de IA local em CPU podem ser lentos. O timeout do frontend
já está em 5 minutos (`Cerejia.Web/Program.cs`); se ainda não for suficiente, considere um modelo de
chat mais leve que o `phi3`.
