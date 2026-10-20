#!/usr/bin/env python3
"""Run pinned UNO client against the installed independent office applications."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import socket
import subprocess
import time
import urllib.request

ROOT = Path(__file__).resolve().parents[1]


def run(directory):
    pins = json.loads((ROOT / 'eng/p2-tool-pins.json').read_text())
    tools = directory / 'office-tools'; tools.mkdir(parents=True, exist_ok=True)
    pin = pins['libreOfficeJava']; jar = tools / 'libreoffice-26.2.2.jar'
    if not jar.exists():
        jar.write_bytes(urllib.request.urlopen(pin['url'], timeout=60).read())
    assert hashlib.sha256(jar.read_bytes()).hexdigest() == pin['sha256']
    command = ['javac'] if shutil.which('javac') else None
    if command is None:
        compiler = tools / 'ecj-3.40.0.jar'; pin = pins['fallbackJavaCompiler']
        if not compiler.exists(): compiler.write_bytes(urllib.request.urlopen(pin['url'], timeout=60).read())
        assert hashlib.sha256(compiler.read_bytes()).hexdigest() == pin['sha256']
        command = ['java', '-jar', str(compiler), '-17']
    subprocess.run(command + ['-cp', str(jar), '-d', str(tools), str(ROOT / 'eng/EditP2Objects.java')], check=True, timeout=60)
    profile = (tools / 'profile').resolve().as_uri(); log = (tools / 'office.log').open('w')
    office = subprocess.Popen(['soffice', '-env:UserInstallation=' + profile, '--headless', '--norestore', '--nodefault', '--nofirststartwizard',
                               '--accept=socket,host=127.0.0.1,port=2017;urp;StarOffice.ServiceManager'], stdout=log, stderr=subprocess.STDOUT)
    try:
        for _ in range(100):
            if office.poll() is not None: raise RuntimeError('Independent office exited before UNO was ready; see office.log')
            try:
                with socket.create_connection(('127.0.0.1', 2017), timeout=.2): break
            except OSError: time.sleep(.2)
        else: raise TimeoutError('Independent office UNO socket was not ready within 20 seconds')
        args = ['java', '-cp', os.pathsep.join(map(str, [tools, jar])), 'EditP2Objects', str(directory / 'objects.docx'),
                str(directory / 'objects-office-edited.docx'), str(directory / 'objects-office-edited.pdf')]
        result = subprocess.run(args, capture_output=True, text=True, timeout=120)
        (tools / 'edits.log').write_text(result.stdout + result.stderr)
        print(result.stdout + result.stderr, flush=True)
        result.check_returncode()
        assert 'Calc cell editor: B2 10.0 -> 42.0' in result.stdout
        assert 'Chart-data editor: first value 10.0 -> 42.0' in result.stdout
        assert 'Equation editor:' in result.stdout and '-> {x} over {3}' in result.stdout
    finally:
        office.terminate()
        try: office.wait(timeout=10)
        except subprocess.TimeoutExpired: office.kill(); office.wait(timeout=10)
        log.close()


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('artifacts', type=Path); args = parser.parse_args()
    run(args.artifacts.resolve())
