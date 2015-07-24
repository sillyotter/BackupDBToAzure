[CmdletBinding()]
param (
	[string] $target = "All"
)

if (-Not (Test-Path ".paket"))
{
    mkdir ".paket"
}

$paketboot = ".paket\paket.bootstrapper.exe"
$pakettarget = ".paket\paket.targets"

if (-Not (Test-Path $paketboot))
{
	Invoke-WebRequest "https://github.com/fsprojects/Paket/releases/download/1.15.1/paket.bootstrapper.exe" -OutFile $paketboot
}

if (-Not (Test-Path $pakettarget))
{
	Invoke-WebRequest "https://github.com/fsprojects/Paket/releases/download/1.15.1/paket.targets" -OutFile $pakettarget
}

Start-Process $paketboot -NoNewWindow -Wait

if ($?)
{
    if (-Not (Test-Path "paket.lock"))
    {
        .paket\paket.exe install
    }
    else
    {
        .paket\paket.exe restore
    }
	if ($?)
	{
		packages\FAKE\tools\FAKE.exe build.fsx $target
	}
} 


