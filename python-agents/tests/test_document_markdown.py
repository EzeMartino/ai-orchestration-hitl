import base64
import json
import sys
import unittest
from pathlib import Path
from unittest.mock import patch


REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "python-agents" / "data_agent"))

from document_markdown import convert_pdf_to_markdown  # noqa: E402


class _ConversionResult:
    text_content = "# Income Statement\n\n| Metric | 2024A |\n|---|---:|\n| Revenue | 100 |"


class _FakeMarkItDown:
    def convert_stream(self, stream, *, stream_info):
        self.payload = stream.read()
        self.stream_info = stream_info
        return _ConversionResult()


class DocumentMarkdownTests(unittest.TestCase):
    @patch("document_markdown.MarkItDown", return_value=_FakeMarkItDown())
    def test_converts_pdf_bytes_with_explicit_stream_info(self, markitdown):
        request = json.dumps(
            {
                "pdfBase64": base64.b64encode(b"%PDF-test").decode("ascii"),
                "maxCharacters": 10_000,
            }
        )

        result = json.loads(convert_pdf_to_markdown(request))
        converter = markitdown.return_value

        markitdown.assert_called_once_with(enable_plugins=False)
        self.assertEqual(converter.payload, b"%PDF-test")
        self.assertEqual(converter.stream_info.mimetype, "application/pdf")
        self.assertEqual(converter.stream_info.extension, ".pdf")
        self.assertTrue(result["succeeded"])
        self.assertEqual(result["markdown"], _ConversionResult.text_content)
        self.assertIsNone(result["failureReason"])
        self.assertFalse(result["truncated"])

    @patch("document_markdown.MarkItDown", return_value=_FakeMarkItDown())
    def test_truncates_output_at_configured_boundary(self, _):
        request = json.dumps(
            {
                "pdfBase64": base64.b64encode(b"%PDF-test").decode("ascii"),
                "maxCharacters": 12,
            }
        )

        result = json.loads(convert_pdf_to_markdown(request))

        self.assertTrue(result["succeeded"])
        self.assertEqual(len(result["markdown"]), 12)
        self.assertTrue(result["truncated"])

    @patch("document_markdown.MarkItDown")
    def test_rejects_non_pdf_payload(self, markitdown):
        request = json.dumps(
            {
                "pdfBase64": base64.b64encode(b"not-a-pdf").decode("ascii"),
                "maxCharacters": 100,
            }
        )

        result = json.loads(convert_pdf_to_markdown(request))

        markitdown.assert_not_called()
        self.assertFalse(result["succeeded"])
        self.assertEqual(result["failureReason"], "invalid_pdf")

    @patch("document_markdown.MarkItDown", side_effect=RuntimeError("conversion failed"))
    def test_maps_conversion_errors_to_failure_reason(self, _):
        request = json.dumps(
            {
                "pdfBase64": base64.b64encode(b"%PDF-test").decode("ascii"),
                "maxCharacters": 100,
            }
        )

        result = json.loads(convert_pdf_to_markdown(request))

        self.assertFalse(result["succeeded"])
        self.assertEqual(result["markdown"], "")
        self.assertFalse(result["truncated"])
        self.assertEqual(result["failureReason"], "conversion_failed")


if __name__ == "__main__":
    unittest.main()
