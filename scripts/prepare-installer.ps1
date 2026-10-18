$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$target = Join-Path $root '.tools\InnoSetup'
if (Test-Path -LiteralPath (Join-Path $target 'ISCC.exe')) { return }
New-Item -ItemType Directory -Path (Join-Path $root '.tools') -Force | Out-Null
$package = Join-Path $root '.tools\innosetup-6.7.3.exe'
Invoke-WebRequest 'https://github.com/jrsoftware/issrc/releases/download/is-6_7_3/innosetup-6.7.3.exe' -OutFile $package
if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne '9C73C3BAE7ED48D44112A0F48E66742C00090BDB5BEF71D9D3C056C66E97B732') { throw 'Inno Setup package hash mismatch.' }
$signature = Get-AuthenticodeSignature -LiteralPath $package
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*Pyrsys B.V.*') { throw 'Inno Setup signature verification failed.' }
$compilerArguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/SP-','/CURRENTUSER','/NOICONS','/TASKS=',('/DIR="' + $target + '"'))
$process = Start-Process -FilePath $package -ArgumentList $compilerArguments -WindowStyle Hidden -PassThru -Wait
if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $target 'ISCC.exe'))) { throw 'Inno Setup installation failed.' }
