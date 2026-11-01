"""Extract one MIT-licensed Word-authored theme from a pinned Open XML SDK fixture."""

import argparse
import hashlib
import pathlib
import zipfile


SOURCE_HASH = "8d297bee1383f87d0fa77e7b2e5573e88517297a88f36f569ec5eaa0f8201878"
THEME_HASH = "bf4992e3fa7832e5c68fdbc4ec5189c8e5d347fef03c5ba2522ad704bde5078f"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=pathlib.Path, required=True)
    parser.add_argument("--output", type=pathlib.Path, required=True)
    args = parser.parse_args()
    if hashlib.sha256(args.input.read_bytes()).hexdigest() != SOURCE_HASH:
        raise ValueError("The upstream fixture hash does not match the pinned source")
    with zipfile.ZipFile(args.input) as archive:
        info = archive.getinfo("word/theme/theme1.xml")
        if info.file_size > 1024 * 1024:
            raise ValueError("The theme exceeds its extraction bound")
        data = archive.read(info)
    if hashlib.sha256(data).hexdigest() != THEME_HASH:
        raise ValueError("The theme part hash does not match the pinned source")
    args.output.mkdir(parents=True, exist_ok=True)
    with (args.output / "theme1.xml").open("xb") as destination:
        destination.write(data)
    print("Extracted the verified Word-authored theme XML.")


if __name__ == "__main__":
    main()
