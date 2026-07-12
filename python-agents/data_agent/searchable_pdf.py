import argparse
import json
import sys
from pathlib import Path

class _ResourceLimitExceeded(Exception):
    pass


class _LimitedOutput:
    def __init__(self, output, maximum_bytes: int):
        self._output = output
        self._remaining_bytes = maximum_bytes

    def write(self, data):
        if len(data) > self._remaining_bytes:
            raise _ResourceLimitExceeded()

        self._remaining_bytes -= len(data)
        return self._output.write(data)

    def __getattr__(self, name):
        return getattr(self._output, name)


def merge_pdf_pages(request_json: str) -> str:
    try:
        from pypdf import PdfWriter

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
            maximum_temporary_bytes = request.get("maxTemporaryBytes")
            maximum_searchable_pdf_bytes = request.get("maxSearchablePdfBytes")
            maximum_output_bytes = _maximum_output_bytes(
                maximum_temporary_bytes,
                maximum_searchable_pdf_bytes,
                working_directory,
                output_path,
            )
            if maximum_output_bytes is None:
                writer.write(output)
            else:
                writer.write(_LimitedOutput(output, maximum_output_bytes))

        return json.dumps(
            {
                "succeeded": True,
                "failureReason": None,
            }
        )
    except _ResourceLimitExceeded:
        return _failure("resource_limit_exceeded")
    except Exception:
        return _failure("merge_failed")


def _is_descendant(path: Path, working_directory: Path) -> bool:
    try:
        path.relative_to(working_directory)
        return True
    except ValueError:
        return False


def _temporary_bytes_except_output(
    working_directory: Path,
    output_path: Path,
) -> int:
    return sum(
        path.stat().st_size
        for path in working_directory.rglob("*")
        if path.is_file() and path.resolve() != output_path
    )


def _maximum_output_bytes(
    maximum_temporary_bytes,
    maximum_searchable_pdf_bytes,
    working_directory: Path,
    output_path: Path,
):
    limits = []

    if maximum_temporary_bytes is not None:
        available_temporary_bytes = (
            int(maximum_temporary_bytes)
            - _temporary_bytes_except_output(working_directory, output_path)
        )
        if available_temporary_bytes < 0:
            raise _ResourceLimitExceeded()
        limits.append(available_temporary_bytes)

    if maximum_searchable_pdf_bytes is not None:
        limits.append(max(0, int(maximum_searchable_pdf_bytes)))

    return min(limits) if limits else None


def _failure(reason: str) -> str:
    return json.dumps(
        {
            "succeeded": False,
            "failureReason": reason,
        }
    )


def _apply_memory_limit(max_memory_bytes: int, resource_module=None):
    if resource_module is None:
        try:
            import resource as resource_module
        except ImportError:
            return

    limit = max(1, int(max_memory_bytes))
    _, hard_limit = resource_module.getrlimit(resource_module.RLIMIT_AS)
    effective_limit = (
        limit
        if hard_limit == resource_module.RLIM_INFINITY
        else min(limit, hard_limit)
    )
    resource_module.setrlimit(
        resource_module.RLIMIT_AS,
        (effective_limit, hard_limit),
    )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--max-memory-bytes", type=int, required=True)
    args = parser.parse_args()
    _apply_memory_limit(args.max_memory_bytes)
    print(merge_pdf_pages(sys.stdin.read()), flush=True)


if __name__ == "__main__":
    main()
