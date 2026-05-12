CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS regulation_documents (
    id TEXT PRIMARY KEY,
    source TEXT NOT NULL,
    document_type TEXT NOT NULL,
    resolution_number TEXT NULL,
    title TEXT NOT NULL,
    publication_date DATE NULL,
    effective_date DATE NULL,
    url TEXT NOT NULL,
    status TEXT NOT NULL,
    requires_review BOOLEAN NOT NULL DEFAULT TRUE,
    retrieved_at TIMESTAMPTZ NULL,
    text TEXT NOT NULL,
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS regulation_chunks (
    id TEXT PRIMARY KEY,
    document_id TEXT NOT NULL REFERENCES regulation_documents(id) ON DELETE CASCADE,
    title TEXT NULL,
    chapter TEXT NULL,
    section TEXT NULL,
    article TEXT NULL,
    chunk_index INTEGER NOT NULL,
    text TEXT NOT NULL,
    embedding vector(1536) NULL,
    embedding_model TEXT NULL,
    embedding_generated_at TIMESTAMPTZ NULL,
    content_hash TEXT NULL,
    duplicate_of_chunk_id TEXT NULL,
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now()
);

ALTER TABLE regulation_chunks
ADD COLUMN IF NOT EXISTS content_hash TEXT NULL;

ALTER TABLE regulation_chunks
ADD COLUMN IF NOT EXISTS duplicate_of_chunk_id TEXT NULL;

ALTER TABLE regulation_chunks
ADD COLUMN IF NOT EXISTS embedding vector(1536) NULL;

ALTER TABLE regulation_chunks
ADD COLUMN IF NOT EXISTS embedding_model TEXT NULL;

ALTER TABLE regulation_chunks
ADD COLUMN IF NOT EXISTS embedding_generated_at TIMESTAMPTZ NULL;

CREATE INDEX IF NOT EXISTS idx_regulation_documents_source
ON regulation_documents(source);

CREATE INDEX IF NOT EXISTS idx_regulation_documents_resolution
ON regulation_documents(resolution_number);

CREATE INDEX IF NOT EXISTS idx_regulation_chunks_document_id
ON regulation_chunks(document_id);

CREATE INDEX IF NOT EXISTS idx_regulation_chunks_article
ON regulation_chunks(article);

CREATE INDEX IF NOT EXISTS idx_regulation_chunks_content_hash
ON regulation_chunks(content_hash);

CREATE INDEX IF NOT EXISTS idx_regulation_chunks_text_fts
ON regulation_chunks
USING GIN (to_tsvector('spanish', coalesce(text, '')));

CREATE INDEX IF NOT EXISTS idx_regulation_documents_text_fts
ON regulation_documents
USING GIN (to_tsvector('spanish', coalesce(text, '')));

CREATE INDEX IF NOT EXISTS idx_regulation_documents_status
ON regulation_documents(status);

CREATE INDEX IF NOT EXISTS idx_regulation_documents_requires_review
ON regulation_documents(requires_review);

CREATE INDEX IF NOT EXISTS idx_regulation_documents_document_type
ON regulation_documents(document_type);
