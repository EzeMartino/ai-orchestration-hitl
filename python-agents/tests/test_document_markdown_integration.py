import importlib.util
import json
import sys
import unittest
from pathlib import Path


MARKITDOWN_AVAILABLE = importlib.util.find_spec("markitdown") is not None

if MARKITDOWN_AVAILABLE:
    REPO_ROOT = Path(__file__).resolve().parents[2]
    sys.path.insert(0, str(REPO_ROOT / "python-agents" / "data_agent"))

    from document_markdown import convert_pdf_bytes_to_markdown  # noqa: E402


def _minimal_pdf(text: str) -> bytes:
    content = f"BT /F1 12 Tf 72 720 Td ({text}) Tj ET".encode("ascii")
    objects = [
        b"<< /Type /Catalog /Pages 2 0 R >>",
        b"<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
        (
            b"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] "
            b"/Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>"
        ),
        b"<< /Length %d >>\nstream\n%s\nendstream" % (len(content), content),
        b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
    ]

    document = bytearray(b"%PDF-1.4\n")
    offsets = [0]

    for object_number, body in enumerate(objects, start=1):
        offsets.append(len(document))
        document.extend(f"{object_number} 0 obj\n".encode("ascii"))
        document.extend(body)
        document.extend(b"\nendobj\n")

    xref_offset = len(document)
    document.extend(f"xref\n0 {len(objects) + 1}\n".encode("ascii"))
    document.extend(b"0000000000 65535 f \n")

    for offset in offsets[1:]:
        document.extend(f"{offset:010d} 00000 n \n".encode("ascii"))

    document.extend(
        (
            f"trailer\n<< /Size {len(objects) + 1} /Root 1 0 R >>\n"
            f"startxref\n{xref_offset}\n%%EOF\n"
        ).encode("ascii")
    )
    return bytes(document)


@unittest.skipUnless(
    MARKITDOWN_AVAILABLE,
    "MarkItDown is unavailable; run with the managed data_agent/.venv Python.",
)
class DocumentMarkdownIntegrationTests(unittest.TestCase):
    def test_converts_generated_pdf_with_real_markitdown(self):
        result = json.loads(
            convert_pdf_bytes_to_markdown(
                _minimal_pdf("Revenue 100"),
                10_000,
                20,
            )
        )

        self.assertTrue(result["succeeded"], result["failureReason"])
        self.assertIn("Revenue 100", result["markdown"])
        self.assertFalse(result["truncated"])
        self.assertIsNone(result["failureReason"])


if __name__ == "__main__":
    unittest.main()
