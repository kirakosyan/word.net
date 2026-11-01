"""Build the chart-advance probe and independently check its output."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile
from xml.sax.saxutils import escape

ROOT = Path(__file__).resolve().parents[1]


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("output", type=Path, help="new scratch directory for the generated documents")
    args = parser.parse_args()
    output = args.output.resolve()
    if output.exists():
        parser.error("Choose a new output directory.")
    source = ROOT / "eng/roadmap-138-chart-advance-probe.cs"
    checker = ROOT / "eng/check-roadmap-138-chart-advances.py"
    font = ROOT / "src/wordnet/Resources/Fonts/NotoSans-Regular.ttf"
    with tempfile.TemporaryDirectory(prefix="wordnet-chart-advance-") as directory:
        project = Path(directory) / "Probe.csproj"
        project.write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
            '<TargetFramework>net10.0</TargetFramework></PropertyGroup><ItemGroup>'
            '<ProjectReference Include="' + escape(str(ROOT / "src/wordnet/wordnet.csproj")) + '"/>'
            '<Compile Include="' + escape(str(source)) + '"/></ItemGroup></Project>', encoding="utf-8")
        for command in (["dotnet", "restore", str(project)],
                        ["dotnet", "build", str(project), "-c", "Release", "--no-restore", "-m:1"]):
            subprocess.run(command, cwd=ROOT, check=True, capture_output=True, text=True)
        binary = project.parent / "bin/Release/net10.0"
        subprocess.run(["dotnet", str(binary / "Probe.dll"), str(output)], cwd=ROOT, check=True)
        candidate = binary / "wordnet.dll"
        expected = ROOT / "src/wordnet/bin/Release/net10.0/wordnet.dll"
        if sha(candidate) != sha(expected):
            raise ValueError("The probe and source assemblies differ")
        result = subprocess.run([sys.executable, str(checker), str(output), str(font)], cwd=ROOT,
                                check=True, capture_output=True, text=True)
        data = json.loads(result.stdout)
        data.update({"assemblySha256": sha(candidate), "probeSourceSha256": sha(source),
                     "checkerSha256": sha(checker)})
    print(json.dumps(data, indent=2))


if __name__ == "__main__":
    main()
