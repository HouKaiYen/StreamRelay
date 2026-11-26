param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'artifacts'),
    [string]$MediaMtxArchive
)

$ErrorActionPreference = 'Stop'
$mediaMtxVersion = 'v1.21.4'
$mediaMtxFile = "mediamtx_${mediaMtxVersion}_windows_amd64.zip"
$expectedHash = '835645b2700ffce4d2b6cb04bfa0aadce95335da6dfc0d7c2114e6da585734f8'
$output = (New-Item -ItemType Directory -Path $OutputDirectory -Force).FullName
$work = (New-Item -ItemType Directory -Path (Join-Path $output ('publish-' + [Guid]::NewGuid().ToString('N')))).FullName

try {
    if (!$MediaMtxArchive) {
        $MediaMtxArchive = Join-Path $work $mediaMtxFile
        Invoke-WebRequest "https://github.com/bluenviron/mediamtx/releases/download/$mediaMtxVersion/$mediaMtxFile" -OutFile $MediaMtxArchive
    }

    if ((Get-FileHash -LiteralPath $MediaMtxArchive -Algorithm SHA256).Hash -ne $expectedHash) {
        throw "MediaMTX archive SHA-256 verification failed. Expected the official $mediaMtxFile."
    }

    $engine = Join-Path $work 'engine'
    Expand-Archive -LiteralPath $MediaMtxArchive -DestinationPath $engine
    $name = 'StreamRelay-Portable-win-x64'
    $package = Join-Path $work $name
    $project = Join-Path $PSScriptRoot 'StreamRelay/StreamRelay.csproj'

    dotnet publish $project -c Release -r win-x64 --self-contained true -o $package "-p:MediaMtxDirectory=$engine" -p:DebugType=None
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $package
    $requiredFiles = @(
        'StreamRelay.exe'
        'relay.yml'
        'MediaMTX/mediamtx.exe'
        'MediaMTX/LICENSE'
        'Licenses/dotnet-runtime-license.txt'
        'Licenses/dotnet-runtime-third-party-notices.txt'
        'Licenses/dotnet-windows-desktop-license.txt'
    )
    foreach ($file in $requiredFiles) {
        if (!(Test-Path -LiteralPath (Join-Path $package $file) -PathType Leaf)) {
            throw "Missing portable package file: $file"
        }
    }

    $stagedArchive = Join-Path $work "$name.zip"
    Compress-Archive -LiteralPath $package -DestinationPath $stagedArchive
    $archive = Join-Path $output "$name.zip"
    Move-Item -LiteralPath $stagedArchive -Destination $archive -Force
    Get-Item -LiteralPath $archive
}
finally {
    $resolvedWork = (Resolve-Path -LiteralPath $work).Path
    if ((Split-Path -Parent $resolvedWork) -eq $output -and (Split-Path -Leaf $resolvedWork) -match '^publish-[0-9a-f]{32}$') {
        Remove-Item -LiteralPath $resolvedWork -Recurse -Force
    }
}
