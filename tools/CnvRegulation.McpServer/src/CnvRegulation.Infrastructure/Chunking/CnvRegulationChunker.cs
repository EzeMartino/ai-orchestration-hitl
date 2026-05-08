namespace CnvRegulation.Infrastructure.Chunking;

/// <summary>
/// Backward-compatible alias for the legal-structure regulation chunker.
/// </summary>
public sealed class CnvRegulationChunker(LegalStructureDetector structureDetector)
    : LegalStructureRegulationChunker(structureDetector);
