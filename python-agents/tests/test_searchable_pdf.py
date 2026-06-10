import json
import sys
import tempfile
import unittest
from pathlib import Path

from pypdf import PdfReader, PdfWriter


REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "python-agents" / "data_agent"))

from searchable_pdf import merge_pdf_pages  # noqa: E402


class SearchablePdfTests(unittest.TestCase):
    def test_merges_pdf_pages_in_supplied_order(self):
        with tempfile.TemporaryDirectory() as temporary_directory:
            working_directory = Path(temporary_directory)
            page_one = working_directory / "page-1-ocr.pdf"
            page_two = working_directory / "page-2-ocr.pdf"
            output_path = working_directory / "searchable.pdf"
            self._write_one_page_pdf(page_one, width=100, height=200)
            self._write_one_page_pdf(page_two, width=300, height=400)

            result = json.loads(
                merge_pdf_pages(
                    json.dumps(
                        {
                            "workingDirectory": str(working_directory),
                            "pagePaths": [str(page_one), str(page_two)],
                            "outputPath": str(output_path),
                        }
                    )
                )
            )

            self.assertTrue(result["succeeded"])
            self.assertIsNone(result["failureReason"])
            reader = PdfReader(output_path)
            self.assertEqual(len(reader.pages), 2)
            self.assertEqual(float(reader.pages[0].mediabox.width), 100)
            self.assertEqual(float(reader.pages[1].mediabox.width), 300)

    def test_rejects_input_path_outside_working_directory(self):
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            working_directory = root / "working"
            working_directory.mkdir()
            page_path = root / "outside.pdf"
            output_path = working_directory / "searchable.pdf"
            self._write_one_page_pdf(page_path, width=100, height=200)

            result = json.loads(
                merge_pdf_pages(
                    json.dumps(
                        {
                            "workingDirectory": str(working_directory),
                            "pagePaths": [str(page_path)],
                            "outputPath": str(output_path),
                        }
                    )
                )
            )

            self.assertFalse(result["succeeded"])
            self.assertEqual(
                result["failureReason"],
                "path_outside_working_directory",
            )
            self.assertFalse(output_path.exists())

    def test_rejects_output_path_outside_working_directory(self):
        with tempfile.TemporaryDirectory() as temporary_directory:
            root = Path(temporary_directory)
            working_directory = root / "working"
            working_directory.mkdir()
            page_path = working_directory / "page-1-ocr.pdf"
            output_path = root / "outside.pdf"
            self._write_one_page_pdf(page_path, width=100, height=200)

            result = json.loads(
                merge_pdf_pages(
                    json.dumps(
                        {
                            "workingDirectory": str(working_directory),
                            "pagePaths": [str(page_path)],
                            "outputPath": str(output_path),
                        }
                    )
                )
            )

            self.assertFalse(result["succeeded"])
            self.assertEqual(
                result["failureReason"],
                "path_outside_working_directory",
            )
            self.assertFalse(output_path.exists())

    @staticmethod
    def _write_one_page_pdf(
        path: Path,
        *,
        width: float,
        height: float,
    ) -> None:
        writer = PdfWriter()
        writer.add_blank_page(width=width, height=height)
        with path.open("wb") as output:
            writer.write(output)


if __name__ == "__main__":
    unittest.main()
