import json
from pathlib import Path

from pypdf import PdfWriter


def merge_pdf_pages(request_json: str) -> str:
    try:
        request = json.loads(request_json)
        working_directory = Path(request["workingDirectory"]).resolve()
        output_path = Path(request["outputPath"]).resolve()
        page_paths = [Path(path).resolve() for path in request["pagePaths"]]

        if not _is_descendant(output_path, working_directory) or any(
            not _is_descendant(page_path, working_directory)
            for page_path in page_paths
        ):
            return _failure("path_outside_working_directory")

        writer = PdfWriter()
        for page_path in page_paths:
            writer.append(page_path)

        with output_path.open("wb") as output:
            writer.write(output)

        return json.dumps(
            {
                "succeeded": True,
                "failureReason": None,
            }
        )
    except Exception:
        return _failure("merge_failed")


def _is_descendant(path: Path, working_directory: Path) -> bool:
    try:
        path.relative_to(working_directory)
        return True
    except ValueError:
        return False


def _failure(reason: str) -> str:
    return json.dumps(
        {
            "succeeded": False,
            "failureReason": reason,
        }
    )
