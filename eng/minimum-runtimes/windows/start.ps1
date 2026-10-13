param(
    [string]$WorkDirectory = (Join-Path $PSScriptRoot 'state'),
    [switch]$SoftwareEmulation,
    [switch]$CheckOnly
)
$ErrorActionPreference = 'Stop'
$image = 'dockurr/windows:6.06@sha256:16fd7c39a3e48bb946d29c59dc6015942e5b900a1fa5d7e5552053bfc48ee2ea'
$work = [IO.Path]::GetFullPath($WorkDirectory)
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Run this launcher on the Windows x64 test PC.' }
if (@(Get-CimInstance Win32_Processor | Where-Object Architecture -ne 9).Count -ne 0) { throw 'Use a native x64 Windows PC. This launcher does not qualify an ARM64 host running x64 emulation.' }
$dockerInfo = (& docker info --format '{{json .}}') -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'Start Docker Desktop with Linux containers, then retry.' }
$engine = $dockerInfo | ConvertFrom-Json
if ($engine.OSType -ne 'linux' -or $engine.Architecture -notin @('x86_64', 'amd64')) { throw 'Docker must use its Linux x64 engine on the x64 PC.' }
& docker compose version
if ($LASTEXITCODE -ne 0) { throw 'Docker Compose v2 or later is required.' }
$drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($work))
$existing = Test-Path -LiteralPath (Join-Path $work 'compose.json')
if (-not $existing -and $drive.AvailableFreeSpace -lt 60GB) { throw 'Choose a WorkDirectory on a disk with at least 60 GiB free, including space for the ISO and VM.' }
if ($engine.MemTotal -lt 6GB) { throw 'Allocate at least 6 GiB to Docker Desktop for this 4 GiB guest.' }
if ($CheckOnly) {
    Write-Output "Preflight passed: Linux x64 Docker; physical disk free $([Math]::Round($drive.AvailableFreeSpace / 1GB, 1)) GiB. KVM is checked at startup."
    exit 0
}
if (-not $existing) {
    if ((Test-Path -LiteralPath $work) -and @(Get-ChildItem -LiteralPath $work -Force).Count -ne 0) { throw 'Use a new or empty WorkDirectory. Existing files will not be replaced.' }
    $bundle = Join-Path $PSScriptRoot 'tests.zip'
    if (-not (Test-Path -LiteralPath $bundle)) { throw 'Use the prepared VM setup bundle, which includes tests.zip and its manifest.' }
    $manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'bundle.json') -Raw | ConvertFrom-Json
    $algorithm = [Security.Cryptography.SHA256]::Create()
    $stream = [IO.File]::OpenRead($bundle)
    try { $hash = ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '').ToLowerInvariant() }
    finally { $stream.Dispose(); $algorithm.Dispose() }
    if ($hash -ne $manifest.testsSha256) { throw 'The supplied test bundle does not match bundle.json.' }
    & docker pull --platform linux/amd64 $image
    if ($LASTEXITCODE -ne 0) { throw 'Could not pull the pinned VM image.' }
    if (-not $SoftwareEmulation) {
        & docker run --rm --network none --platform linux/amd64 --device /dev/kvm --entrypoint /bin/sh $image -c 'test -r /dev/kvm && test -w /dev/kvm'
        if ($LASTEXITCODE -ne 0) { throw 'Docker has no usable KVM device. Enable nested virtualization for its Linux backend, or rerun with -SoftwareEmulation for slower QEMU execution.' }
    }
    foreach ($folder in @('storage', 'shared', 'oem')) { [IO.Directory]::CreateDirectory((Join-Path $work $folder)) | Out-Null }
    foreach ($file in @('install.bat', 'qualify.ps1')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $work "oem/$file") }
    Copy-Item -LiteralPath $bundle -Destination (Join-Path $work 'oem/tests.zip')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'bundle.json') -Destination (Join-Path $work 'oem/bundle.json')
    $password = [Guid]::NewGuid().ToString('N') + 'aA1!'
    [IO.File]::WriteAllText((Join-Path $work 'secrets.env'), "WORDNET_VM_PASSWORD=$password`n", [Text.UTF8Encoding]::new($false))
    $service = [ordered]@{
        image = $image
        platform = 'linux/amd64'
        environment = [ordered]@{
            VERSION = '2019'; VERIFY = 'Y'; RAM_SIZE = '4G'; CPU_CORES = '2'; DISK_SIZE = '40G'
            DISK_FMT = 'qcow2'; NETWORK = 'user'; KVM = $(if ($SoftwareEmulation) { 'N' } else { 'Y' })
            USERNAME = 'Wordnet'; PASSWORD = '${WORDNET_VM_PASSWORD}'; LOG = 'Y'
        }
        ports = @('127.0.0.1:8006:8006')
        volumes = @('./storage:/storage', './oem:/oem:ro', './shared:/shared')
        restart = 'no'
        stop_grace_period = '2m'
    }
    if (-not $SoftwareEmulation) { $service.devices = @('/dev/kvm:/dev/kvm') }
    $compose = [ordered]@{ services = [ordered]@{ windows = $service } }
    [IO.File]::WriteAllText((Join-Path $work 'compose.json'), ($compose | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
}
$composePath = Join-Path $work 'compose.json'
$envPath = Join-Path $work 'secrets.env'
& docker compose --project-name wordnet-framework472 --env-file $envPath -f $composePath config --quiet
if ($LASTEXITCODE -ne 0) { throw 'Generated Docker Compose configuration is invalid.' }
& docker compose --project-name wordnet-framework472 --env-file $envPath -f $composePath up -d
if ($LASTEXITCODE -ne 0) { throw 'Windows VM startup failed.' }
Write-Output "Windows setup started. Viewer: http://localhost:8006 . Results: $work/shared/results ."
Write-Output 'The guest runs the full suite automatically and checks the actual Framework release. Startup alone is not qualification.'
Write-Output "Stop after collecting results: docker compose --project-name wordnet-framework472 --env-file `"$envPath`" -f `"$composePath`" stop"
