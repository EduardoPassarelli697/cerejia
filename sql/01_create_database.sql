CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS "uuid-ossp";

CREATE TABLE empresas (
    id                UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    nome              VARCHAR(200) NOT NULL,
    cnpj              VARCHAR(14) NOT NULL UNIQUE,
    cnpj_ativo        BOOLEAN NOT NULL DEFAULT FALSE,
    senha_admin_hash  TEXT NOT NULL,
    criado_em         TIMESTAMP DEFAULT NOW(),
    ativo             BOOLEAN DEFAULT TRUE
);

CREATE TABLE usuarios (
    id                 UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    empresa_id         UUID NOT NULL REFERENCES empresas(id) ON DELETE CASCADE,
    cpf                VARCHAR(11) NOT NULL,
    email_corporativo  VARCHAR(255) NOT NULL,
    senha_hash         TEXT NOT NULL,
    supervisor         BOOLEAN NOT NULL DEFAULT FALSE,
    criado_em          TIMESTAMP DEFAULT NOW(),
    ativo              BOOLEAN DEFAULT TRUE,
    UNIQUE (empresa_id, email_corporativo)
);

CREATE TABLE documentos (
    id           UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    empresa_id   UUID NOT NULL REFERENCES empresas(id) ON DELETE CASCADE,
    nome_arquivo VARCHAR(500) NOT NULL,
    tipo_arquivo VARCHAR(50) NOT NULL,
    tamanho_kb   INT,
    status       VARCHAR(20) DEFAULT 'processando',
    criado_em    TIMESTAMP DEFAULT NOW()
);

CREATE TABLE documento_chunks (
    id           UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    documento_id UUID NOT NULL REFERENCES documentos(id) ON DELETE CASCADE,
    empresa_id   UUID NOT NULL REFERENCES empresas(id) ON DELETE CASCADE,
    conteudo     TEXT NOT NULL,
    embedding    VECTOR(384),
    chunk_index  INT NOT NULL,
    criado_em    TIMESTAMP DEFAULT NOW()
);

CREATE INDEX idx_chunks_embedding
    ON documento_chunks USING ivfflat (embedding vector_cosine_ops)
    WITH (lists = 100);

CREATE INDEX idx_chunks_empresa ON documento_chunks (empresa_id);

CREATE TABLE conversas (
    id           UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    empresa_id   UUID NOT NULL REFERENCES empresas(id) ON DELETE CASCADE,
    titulo       VARCHAR(300),
    criado_em    TIMESTAMP DEFAULT NOW()
);

CREATE TABLE mensagens (
    id            UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    conversa_id   UUID NOT NULL REFERENCES conversas(id) ON DELETE CASCADE,
    papel         VARCHAR(20) NOT NULL,
    conteudo      TEXT NOT NULL,
    tokens_usados INT DEFAULT 0,
    criado_em     TIMESTAMP DEFAULT NOW()
);

CREATE INDEX idx_mensagens_conversa ON mensagens (conversa_id, criado_em);
CREATE INDEX idx_conversas_empresa  ON conversas (empresa_id, criado_em DESC);
CREATE INDEX idx_documentos_empresa ON documentos (empresa_id);
CREATE INDEX idx_usuarios_empresa   ON usuarios (empresa_id);
