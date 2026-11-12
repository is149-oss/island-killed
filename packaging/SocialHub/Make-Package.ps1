param(
    [Parameter(Mandatory=$true)][string]$PublishDirectory,
    [Parameter(Mandatory=$true)][string]$MakeAppx,
    [Parameter(Mandatory=$true)][string]$OutputPackage
)
$ErrorActionPreference='Stop'
# No install, certificate trust or OS policy changes. Output is deliberately unsigned.
$publishRoot=(Resolve-Path -LiteralPath $PublishDirectory).Path
if(!(Test-Path -LiteralPath (Join-Path $publishRoot 'Island.exe'))){throw 'Missing published Island.exe'}
foreach($profileName in @('SocialHubProfile','DeveloperProProfile','ReviewProfile')){
    if(Test-Path -LiteralPath (Join-Path $publishRoot $profileName)){throw 'Do not package a used folder: publish to a fresh directory without user profiles'}
}
if((Get-AuthenticodeSignature -LiteralPath $MakeAppx).Status -ne 'Valid'){throw 'Use a valid Microsoft-signed MakeAppx from Windows SDK'}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'AppxManifest.xml') -Destination (Join-Path $publishRoot 'AppxManifest.xml')
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'SocialAssets') -Destination $publishRoot -Recurse -Force
& $MakeAppx pack /d $publishRoot /p $OutputPackage /o
if($LASTEXITCODE -ne 0){throw "MakeAppx failed: $LASTEXITCODE"}
Write-Output 'Unsigned package created. Use your own trusted package-signing certificate; installation and permission grant are manual.'
