"""Run pinned offline veraPDF UA-1, including an untagged negative control.

Uses Docker copy rather than host mounts, so local-socket daemons in managed
workspaces can read the exact input bytes. No network or writable host mount.
"""
import argparse
import io
import os
from pathlib import Path
import subprocess
import tarfile
import uuid
import xml.etree.ElementTree as ET

IMAGE = "verapdf/cli:v1.30.2@sha256:d5ee329657cf9bc4b2400392dd54c7d0a0ce9980ff6fa2da5590eebeec007cdb"

def validate(directory, output, flavour):
    files = ["accessible-ua1.pdf", "accessible-multipage.pdf", "accessible-early-link.pdf", "accessible-negative.pdf"] if flavour == "ua1" else sorted(p.name for p in directory.glob("*.pdf"))
    assert files, "Generate the PDF corpus first."
    environment = os.environ.copy()
    for key in ("DOCKER_HOST", "DOCKER_CONTEXT", "DOCKER_TLS", "DOCKER_TLS_VERIFY", "DOCKER_CERT_PATH"):
        environment.pop(key, None)
    docker = ["docker", "--host=unix:///var/run/docker.sock"]
    name = "wordnet-ua-" + uuid.uuid4().hex
    def run(arguments, **kwargs):
        return subprocess.run(docker + arguments, env=environment, check=kwargs.pop("check", True), **kwargs)
    run(["create", "--name", name, "--network", "none", IMAGE, "--format", "xml", "--flavour", flavour] + ["/tmp/" + f for f in files], stdout=subprocess.DEVNULL)
    try:
        archive = io.BytesIO()
        with tarfile.open(fileobj=archive, mode="w") as tar:
            for filename in files:
                data = (directory / filename).read_bytes(); info = tarfile.TarInfo(filename); info.size = len(data); info.mode = 0o644; tar.addfile(info, io.BytesIO(data))
        run(["cp", "-", name + ":/tmp"], input=archive.getvalue())
        result = run(["start", "-a", name], capture_output=True, check=False)
        output.parent.mkdir(parents=True, exist_ok=True); output.write_bytes(result.stdout); output.with_suffix(".stderr").write_bytes(result.stderr)
        root = ET.fromstring(result.stdout); reports = {}
        for job in root.findall("./jobs/job"):
            report = job.find("validationReport"); assert report is not None
            reports[Path(job.findtext("item/name")).name] = report.get("isCompliant") == "true"
        expected = {f: f != "accessible-negative.pdf" for f in files} if flavour == "ua1" else {f: True for f in files}
        assert reports == expected, reports
        print("veraPDF 1.30.2 PDF/UA-1: all supported fixtures compliant; untagged negative control rejected." if flavour == "ua1" else f"veraPDF 1.30.2 PDF/A-2b: {len(files)}/{len(files)} compliant.")
    finally:
        run(["rm", "-f", name], stdout=subprocess.DEVNULL)

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument("artifacts", type=Path); parser.add_argument("--output", type=Path, default=Path("artifacts/bucket4/ua/qualified.xml")); parser.add_argument("--flavour", choices=("ua1", "2b"), default="ua1"); args = parser.parse_args(); validate(args.artifacts, args.output, args.flavour)
