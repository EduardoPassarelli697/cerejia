ALTER TABLE conversas
    ADD COLUMN IF NOT EXISTS usuario_id UUID REFERENCES usuarios(id);

CREATE INDEX IF NOT EXISTS idx_conversas_usuario ON conversas (usuario_id);
