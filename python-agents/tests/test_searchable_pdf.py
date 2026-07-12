import json
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock

from pypdf import PdfReader, PdfWriter


REPO_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO_ROOT / "python-agents" / "data_agent"))

from searchable_pdf import _apply_memory_limit, merge_pdf_pages  # noqa: E402


class SearchablePdfTests(unittest.TestCase):
    def test_cli_reads_request_from_stdin_and_writes_result_to_stdout(self):
        with tempfile.TemporaryDirectory() as temporary_directory:
            working_directory = Path(temporary_directory)
            page_path = working_directory / "page-1-ocr.pdf"
            output_path = working_directory / "searchable.pdf"
            self._write_one_page_pdf(page_path, width=100, height=200)

            completed = subprocess.run(
                [
                    sys.executable,
                    str(REPO_ROOT / "python-agents" / "data_agent" / "searchable_pdf.py"),
                    "--max-memory-bytes",
                    "1073741824",
                ],
                input=json.dumps(
                    {
                        "workingDirectory": str(working_directory),
                        "pagePaths": [str(page_path)],
                        "outputPath": str(output_path),
                    }
                ),
                text=True,
                capture_output=True,
                check=False,
            )

            self.assertEqual(completed.returncode, 0, completed.stderr)
            self.assertEqual(
                json.loads(completed.stdout),
                {"succeeded": True, "failureReason": None},
            )

    def test_applies_address_space_limit_on_supported_platforms(self):
        resource_module = Mock()
        resource_module.RLIMIT_AS = 9
        resource_module.RLIM_INFINITY = -1
        resource_module.getrlimit.return_value = (-1, -1)

        _apply_memory_limit(1_073_741_824, resource_module)

        resource_module.setrlimit.assert_called_once_with(
            9,
            (1_073_741_824, -1),
        )

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

    def test_rejects_merge_when_output_would_exceed_temporary_budget(self):
        with tempfile.TemporaryDirectory() as temporary_directory:
            working_directory = Path(temporary_directory)
            page_path = working_directory / "page-1-ocr.pdf"
            output_path = working_directory / "searchable.pdf"
            self._write_one_page_pdf(page_path, width=100, height=200)
            temporary_bytes = page_path.stat().st_size

            result = json.loads(
                merge_pdf_pages(
                    json.dumps(
                        {
                            "workingDirectory": str(working_directory),
                            "pagePaths": [str(page_path)],
                            "outputPath": str(output_path),
                            "maxTemporaryBytes": temporary_bytes,
                        }
                    )
                )
            )

            self.assertFalse(result["succeeded"])
            self.assertEqual(
                result["failureReason"],
                "resource_limit_exceeded",
            )
            self.assertLessEqual(output_path.stat().st_size, 0)

    def test_rejects_merge_when_output_would_exceed_searchable_pdf_budget(self):
        with tempfile.TemporaryDirectory() as temporary_directory:
            working_directory = Path(temporary_directory)
            page_path = working_directory / "page-1-ocr.pdf"
            output_path = working_directory / "searchable.pdf"
            self._write_one_page_pdf(page_path, width=100, height=200)

            result = json.loads(
                merge_pdf_pages(
                    json.dumps(
                        {
                            "workingDirectory": str(working_directory),
                            "pagePaths": [str(page_path)],
                            "outputPath": str(output_path),
                            "maxSearchablePdfBytes": 0,
                        }
                    )
                )
            )

            self.assertFalse(result["succeeded"])
            self.assertEqual(
                result["failureReason"],
                "resource_limit_exceeded",
            )
            self.assertLessEqual(output_path.stat().st_size, 0)

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
