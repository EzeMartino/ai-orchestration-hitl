import base64
import io
import json

from markitdown import MarkItDown, StreamInfo


def convert_pdf_to_markdown(request_json: str) -> str:
    try:
        request = json.loads(request_json)
        pdf_bytes = base64.b64decode(request["pdfBase64"], validate=True)
        max_characters = max(1, int(request["maxCharacters"]))

        if not pdf_bytes.startswith(b"%PDF-"):
            return _failure("invalid_pdf")

        converter = MarkItDown(enable_plugins=False)
        result = converter.convert_stream(
            io.BytesIO(pdf_bytes),
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
            }
        )
    except Exception:
        return _failure("conversion_failed")


def _failure(reason: str) -> str:
    return json.dumps(
        {
            "succeeded": False,
            "markdown": "",
            "truncated": False,
            "failureReason": reason,
        }
    )
