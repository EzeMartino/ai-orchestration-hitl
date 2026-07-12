import argparse
import io
import json
import sys

MarkItDown = None
StreamInfo = None
PageObject = None
PdfReader = None
PdfWriter = None


def convert_pdf_bytes_to_markdown(
    pdf_bytes: bytes,
    max_characters: int,
    max_pages: int,
) -> str:
    try:
        _load_markitdown_dependencies()
        max_characters = max(1, int(max_characters))
        max_pages = max(1, int(max_pages))

        if not pdf_bytes.startswith(b"%PDF-"):
            return _failure("invalid_pdf")

        limited_pdf = _limit_pdf_pages(pdf_bytes, max_pages)
        converter = MarkItDown(enable_plugins=False)
        result = converter.convert_stream(
            io.BytesIO(limited_pdf),
            stream_info=StreamInfo(
                mimetype="application/pdf",
                extension=".pdf",
            ),
        )
        markdown = result.text_content or ""
        truncated = len(markdown) > max_characters

        return json.dumps(
            {
                "succeeded": True,
                "markdown": markdown[:max_characters],
                "truncated": truncated,
                "failureReason": None,
            },
            ensure_ascii=False,
        )
    except Exception:
        return _failure("conversion_failed")


def _limit_pdf_pages(pdf_bytes: bytes, max_pages: int) -> bytes:
    _load_pypdf_dependencies()
    reader = PdfReader(io.BytesIO(pdf_bytes))
    catalog = _resolve_pdf_object(reader.trailer["/Root"])
    page_tree = catalog["/Pages"]
    pages = _walk_page_tree(reader, page_tree, max_pages + 1)

    if len(pages) <= max_pages:
        return pdf_bytes

    writer = PdfWriter()

    for page in pages[:max_pages]:
        writer.add_page(page)

    limited_pdf = io.BytesIO()
    writer.write(limited_pdf)
    return limited_pdf.getvalue()


def _walk_page_tree(reader, root, maximum_pages: int):
    pages = []
    stack = [root]
    visited = set()

    while stack and len(pages) < maximum_pages:
        reference = stack.pop()
        node = _resolve_pdf_object(reference)
        key = _pdf_object_key(reference, node)
        if key in visited:
            continue

        visited.add(key)
        node_type = str(node.get("/Type", ""))
        if node_type == "/Page":
            pages.append(_as_page_object(reader, reference, node))
            continue

        children = node.get("/Kids", ())
        stack.extend(reversed(children))

    return pages


def _as_page_object(reader, reference, resolved):
    if isinstance(resolved, PageObject):
        return resolved

    if getattr(reference, "idnum", None) is not None:
        return PageObject(reader, reference)

    page = PageObject(reader)
    page.update(resolved)
    return page


def _resolve_pdf_object(value):
    getter = getattr(value, "get_object", None)
    return getter() if getter is not None else value


def _pdf_object_key(reference, resolved):
    object_number = getattr(reference, "idnum", None)
    generation = getattr(reference, "generation", None)
    return (
        ("indirect", object_number, generation)
        if object_number is not None
        else ("direct", id(resolved))
    )


def _load_markitdown_dependencies():
    global MarkItDown, StreamInfo
    if MarkItDown is None:
        from markitdown import MarkItDown as markitdown_type

        MarkItDown = markitdown_type
    if StreamInfo is None:
        from markitdown import StreamInfo as stream_info_type

        StreamInfo = stream_info_type


def _load_pypdf_dependencies():
    global PageObject, PdfReader, PdfWriter
    if PageObject is None:
        from pypdf import PageObject as page_object_type

        PageObject = page_object_type
    if PdfReader is None:
        from pypdf import PdfReader as pdf_reader_type

        PdfReader = pdf_reader_type
    if PdfWriter is None:
        from pypdf import PdfWriter as pdf_writer_type

        PdfWriter = pdf_writer_type


def _failure(reason: str) -> str:
    return json.dumps(
        {
            "succeeded": False,
            "markdown": "",
            "truncated": False,
            "failureReason": reason,
        },
        ensure_ascii=False,
    )


def _read_pdf_bytes(input_stream, max_input_bytes: int):
    limit = max(1, int(max_input_bytes))
    pdf_bytes = input_stream.read(limit + 1)
    return None if len(pdf_bytes) > limit else pdf_bytes


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


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--max-characters", type=int, required=True)
    parser.add_argument("--max-pages", type=int, required=True)
    parser.add_argument("--max-input-bytes", type=int, required=True)
    parser.add_argument("--max-memory-bytes", type=int, required=True)
    args = parser.parse_args()
    _apply_memory_limit(args.max_memory_bytes)
    pdf_bytes = _read_pdf_bytes(sys.stdin.buffer, args.max_input_bytes)
    response = (
        _failure("input_too_large")
        if pdf_bytes is None
        else convert_pdf_bytes_to_markdown(
            pdf_bytes,
            args.max_characters,
            args.max_pages,
        )
    )
    sys.stdout.write(response)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
