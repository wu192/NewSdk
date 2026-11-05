#!/usr/bin/env python3
"""Validate one XML document against a selected, local XSD without Skill code."""

import argparse
import json
import os
from pathlib import Path
import sys
from urllib.parse import urlparse
from urllib.request import url2pathname


MAX_XML_BYTES = 8 * 1024 * 1024
MAX_SCHEMA_BYTES = 32 * 1024 * 1024
MAX_SCHEMA_FILES = 4096
MAX_DIAGNOSTICS = 64


def diagnostic(message, code, line=None, column=None):
    result = {"message": str(message)[:1500], "code": code}
    if line and line > 0:
        result["line"] = int(line)
    if column and column > 0:
        result["column"] = int(column)
    return result


def emit(status, diagnostics):
    # ASCII JSON remains valid on Windows Python pipes with a legacy encoding.
    print(json.dumps({"status": status, "diagnostics": diagnostics[:MAX_DIAGNOSTICS],
                      "backend": "lxml"}, ensure_ascii=True))
    return {"PASS": 0, "FAIL": 1, "UNKNOWN": 2}[status]


def errors(error_log, fallback, code):
    items = [entry for entry in error_log if entry.level >= 2]
    if not items:
        return [diagnostic(fallback, code)]
    return [diagnostic(entry.message, code, entry.line, entry.column)
            for entry in items[:MAX_DIAGNOSTICS]]


def read_limited(file, maximum):
    if not file.is_file() or file.stat().st_size > maximum:
        raise OSError("Input is not a regular file or exceeds its byte budget.")
    with file.open("rb") as handle:
        data = handle.read(maximum + 1)
    if len(data) > maximum:
        raise OSError("Input changed or exceeds its byte budget.")
    return data


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--schema", required=True, help="Selected local normalized XSD entry")
    parser.add_argument("--xml", required=True, help="The single XML file to check")
    parser.add_argument("--json", action="store_true", help="Return a bounded JSON report")
    args = parser.parse_args()

    try:
        from lxml import etree
    except ImportError:
        return emit("UNKNOWN", [diagnostic("Python lxml is required for XSD validation.",
                                           "XSD_LIBRARY_MISSING")])

    try:
        selected = Path(args.schema).resolve(strict=True)
        schema_root = selected.parent
    except OSError as exc:
        return emit("UNKNOWN", [diagnostic(str(exc), "XSD_UNAVAILABLE")])

    # The separately distributed Skill verifies the pinned wrapper's manifest.
    # Standalone checks must not silently relabel an unverified wrapper as trusted.
    if selected.name == "public-fx-types-wrapper.xsd":
        return emit("UNKNOWN", [diagnostic(
            "This wrapper requires the imported Skill's schema-profile provenance check.",
            "XSD_PROVENANCE_REQUIRED")])

    def safe_parser():
        return etree.XMLParser(no_network=True, resolve_entities=False, load_dtd=False,
                               huge_tree=False)

    class LocalResolver(etree.Resolver):
        def __init__(self):
            super().__init__()
            self.sources = {}
            self.bytes = 0

        def resolve(self, url, pubid, context):
            # An absolute native path is possible when lxml opens the main file.
            if os.path.isabs(url):
                requested = Path(url)
            else:
                parsed = urlparse(url)
                if parsed.scheme not in {"", "file"} or parsed.netloc not in {"", "localhost"}:
                    raise OSError("Remote XSD dependencies are disabled.")
                if parsed.query or parsed.fragment:
                    raise OSError("XSD dependency queries and fragments are unsupported.")
                # url2pathname already decodes URI escapes (and Windows drive
                # letters). Decoding twice changes a literal "%20" filename.
                requested = Path(url2pathname(parsed.path))
            target = requested.resolve(strict=True)
            try:
                target.relative_to(schema_root)
            except ValueError:
                raise OSError("XSD dependency escapes the selected schema directory.")
            data = self.sources.get(target)
            if data is None:
                if len(self.sources) >= MAX_SCHEMA_FILES:
                    raise OSError("XSD dependency file budget exceeded.")
                data = read_limited(target, MAX_SCHEMA_BYTES - self.bytes)
                model = etree.fromstring(data, safe_parser())
                if model.getroottree().docinfo.doctype:
                    raise OSError("DTD declarations are disabled in XSD documents.")
                self.sources[target] = data
                self.bytes += len(data)
            return self.resolve_string(data, context, base_url=target.as_uri())

    schema_parser = safe_parser()
    schema_parser.resolvers.add(LocalResolver())
    try:
        schema = etree.XMLSchema(etree.parse(selected.as_uri(), schema_parser))
    except (OSError, ValueError, etree.Error) as exc:
        return emit("UNKNOWN", errors(getattr(exc, "error_log", []), str(exc),
                                      "XSD_UNAVAILABLE"))

    try:
        data = read_limited(Path(args.xml), MAX_XML_BYTES)
        document = etree.fromstring(data, safe_parser())
        if document.getroottree().docinfo.doctype:
            return emit("FAIL", [diagnostic("DTD declarations are disabled in XML documents.",
                                           "XML_DTD_FORBIDDEN")])
        if schema.validate(document):
            return emit("PASS", [])
        return emit("FAIL", errors(schema.error_log, "XML does not match the selected schema.",
                                   "XSD_INVALID"))
    except (OSError, ValueError, etree.Error) as exc:
        return emit("FAIL", errors(getattr(exc, "error_log", []), str(exc), "XML_INVALID"))


if __name__ == "__main__":
    sys.exit(main())
