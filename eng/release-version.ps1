function Assert-ReleaseVersion {
    param([Parameter(Mandatory = $true)][string]$Version)

    # NuGet normalizes some alternative spellings. Require one unambiguous SemVer
    # spelling so the project, archive name and release notes identify the same build.
    $number = '(0|[1-9][0-9]*)'
    $identifier = '(0|[1-9][0-9]*|[0-9]*[A-Za-z-][0-9A-Za-z-]*)'
    if ($Version -cnotmatch "\A$number\.$number\.$number(-$identifier(\.$identifier)*)?\z") {
        throw 'Expected a stable or prerelease SemVer without build metadata or leading zeros.'
    }
}
