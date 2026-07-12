import io
import json
import sys
import unittest
from pathlib import Path
from unittest.mock import patch

from pypdf import PdfReader, PdfWriter


REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "python-agents" / "data_agent"))

from document_markdown import (  # noqa: E402
    _apply_memory_limit,
    _limit_pdf_pages,
    _read_pdf_bytes,
    convert_pdf_bytes_to_markdown,
)


class _ConversionResult:
    text_content = "# Income Statement\n\n| Metric | 2024A |\n|---|---:|\n| Revenue | 100 |"


class _FakeMarkItDown:
    def convert_stream(self, stream, *, stream_info):
        self.payload = stream.read()
        self.stream_info = stream_info
        return _ConversionResult()


class DocumentMarkdownTests(unittest.TestCase):
    def test_applies_address_space_limit_on_supported_platforms(self):
        resource_module = unittest.mock.Mock()
        resource_module.RLIMIT_AS = 9
        resource_module.RLIM_INFINITY = -1
        resource_module.getrlimit.return_value = (-1, -1)

        _apply_memory_limit(1_073_741_824, resource_module)

        resource_module.setrlimit.assert_called_once_with(
            9,
            (1_073_741_824, -1),
        )

    @patch("document_markdown.PdfWriter")
    @patch("document_markdown.PdfReader")
    def test_page_limit_walks_page_tree_without_flattening_all_pages(
        self,
        pdf_reader,
        pdf_writer,
    ):
        class Reference:
            def __init__(self, value):
                self.value = value

            def get_object(self):
                return self.value

        pages = [Reference({"/Type": "/Page", "/Id": index}) for index in range(4)]
        root = Reference({"/Type": "/Pages", "/Kids": pages})
        reader = pdf_reader.return_value
        reader.trailer = {"/Root": {"/Pages": root}}
        type(reader).pages = unittest.mock.PropertyMock(
            side_effect=AssertionError("pages must not be flattened")
        )
        writer = pdf_writer.return_value
        writer.write.side_effect = lambda stream: stream.write(b"limited")

        result = _limit_pdf_pages(b"%PDF-test", 2)

        self.assertEqual(b"limited", result)
        self.assertEqual(2, writer.add_page.call_count)

    @patch("document_markdown.MarkItDown", return_value=_FakeMarkItDown())
    def test_converts_pdf_bytes_with_explicit_stream_info(self, markitdown):
        pdf_bytes = self._one_page_pdf()
        result = json.loads(
            convert_pdf_bytes_to_markdown(pdf_bytes, 10_000, 20)
        )
        converter = markitdown.return_value

        markitdown.assert_called_once_with(enable_plugins=False)
        self.assertEqual(converter.payload, pdf_bytes)
        self.assertEqual(converter.stream_info.mimetype, "application/pdf")
        self.assertEqual(converter.stream_info.extension, ".pdf")
        self.assertTrue(result["succeeded"])
        self.assertEqual(result["markdown"], _ConversionResult.text_content)
        self.assertIsNone(result["failureReason"])
        self.assertFalse(result["truncated"])

    @patch("document_markdown.MarkItDown", return_value=_FakeMarkItDown())
    def test_truncates_output_at_configured_boundary(self, _):
        result = json.loads(
            convert_pdf_bytes_to_markdown(self._one_page_pdf(), 12, 20)
        )

        self.assertTrue(result["succeeded"])
        self.assertEqual(len(result["markdown"]), 12)
        self.assertTrue(result["truncated"])

    @patch("document_markdown.MarkItDown")
    def test_serializes_unicode_without_ascii_escape_expansion(self, markitdown):
        unicode_markdown = "😀" * 200_000
        markitdown.return_value.convert_stream.return_value.text_content = (
            unicode_markdown
        )

        response = convert_pdf_bytes_to_markdown(
            self._one_page_pdf(),
            200_000,
            20,
        )

        self.assertIn("😀", response)
        self.assertLess(len(response.encode("utf-8")), 1_000_000)

    @patch("document_markdown.MarkItDown")
    def test_rejects_non_pdf_payload(self, markitdown):
        result = json.loads(convert_pdf_bytes_to_markdown(b"not-a-pdf", 100, 20))

        markitdown.assert_not_called()
        self.assertFalse(result["succeeded"])
        self.assertEqual(result["failureReason"], "invalid_pdf")

    @patch("document_markdown.MarkItDown", side_effect=RuntimeError("conversion failed"))
    def test_maps_conversion_errors_to_failure_reason(self, _):
        result = json.loads(convert_pdf_bytes_to_markdown(b"%PDF-test", 100, 20))

        self.assertFalse(result["succeeded"])
        self.assertEqual(result["markdown"], "")
        self.assertFalse(result["truncated"])
        self.assertEqual(result["failureReason"], "conversion_failed")

    @patch("document_markdown.MarkItDown", return_value=_FakeMarkItDown())
    def test_limits_three_page_pdf_to_two_before_markitdown(self, markitdown):
        writer = PdfWriter()
        for _ in range(3):
            writer.add_blank_page(width=72, height=72)
        source = io.BytesIO()
        writer.write(source)

        result = json.loads(convert_pdf_bytes_to_markdown(source.getvalue(), 10_000, 2))

        self.assertTrue(result["succeeded"])
        converted_pdf = PdfReader(io.BytesIO(markitdown.return_value.payload))
        self.assertEqual(len(converted_pdf.pages), 2)

    def test_bounds_worker_standard_input(self):
        self.assertEqual(_read_pdf_bytes(io.BytesIO(b"1234"), 4), b"1234")
        self.assertIsNone(_read_pdf_bytes(io.BytesIO(b"12345"), 4))

    @staticmethod
    def _one_page_pdf() -> bytes:
        writer = PdfWriter()
        writer.add_blank_page(width=72, height=72)
        output = io.BytesIO()
        writer.write(output)
        return output.getvalue()


if __name__ == "__main__":
    unittest.main()
