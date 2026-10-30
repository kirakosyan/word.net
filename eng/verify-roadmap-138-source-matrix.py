#!/usr/bin/env python3
"""Validate and optionally run the tests mapped by the unreleased source matrix."""

import argparse
import json
import pathlib
import re
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run", action="store_true", help="run the mapped tests on the selected framework")
    parser.add_argument("--framework", default="net10.0", choices=("net8.0", "net10.0"))
    args = parser.parse_args()
    root = pathlib.Path(__file__).resolve().parent.parent
    matrix = json.loads((root / "docs/roadmap-138-source-matrix.json").read_text(encoding="utf-8"))
    if matrix.get("schemaVersion") != 1 or not matrix.get("profiles"):
        raise ValueError("The source matrix has no supported schema or profiles")
    sources = [path.read_text(encoding="utf-8") for path in (root / "tests/wordnet.Conversion.Tests").glob("*.cs")]
    ids, methods = set(), set()
    for profile in matrix["profiles"]:
        if (profile["id"] in ids or profile["outcome"] not in {"supported-bounded", "rejected"}
                or not profile["boundary"] or not profile["tests"]):
            raise ValueError("Invalid or duplicate source matrix profile")
        ids.add(profile["id"])
        for test in profile["tests"]:
            cls, method = test["class"], test["method"]
            if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", cls) or not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", method):
                raise ValueError("Invalid test identifier")
            if not any(re.search(r"\bclass\s+" + cls + r"\b", source) and re.search(r"\bvoid\s+" + method + r"\s*\(", source) for source in sources):
                raise ValueError("Mapped test is missing: " + cls + "." + method)
            methods.add("FullyQualifiedName~WordNet.Tests." + cls + "." + method)
    print(f"Validated {len(ids)} unreleased profiles and {len(methods)} mapped tests.", flush=True)
    if args.run:
        subprocess.run(["dotnet", "test", str(root / "tests/wordnet.Conversion.Tests"), "-c", "Release",
                        "-f", args.framework, "--no-restore", "--filter", "|".join(sorted(methods))], cwd=root, check=True)
    print("This mapping does not establish release, package or runtime qualification.")


if __name__ == "__main__":
    main()
