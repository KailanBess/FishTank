param([ValidateSet('Signature')][string]$Mode='Signature',[string]$RequestBase64)
$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$request=[Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($RequestBase64)) | ConvertFrom-Json
$signature=Get-AuthenticodeSignature -LiteralPath ([string]$request.Path)
$pin=''
$subject=''
if($null -ne $signature.SignerCertificate){
 $subject=$signature.SignerCertificate.Subject
 $sha=[Security.Cryptography.SHA256]::Create()
 try{$pin=([BitConverter]::ToString($sha.ComputeHash($signature.SignerCertificate.RawData))).Replace('-','')}finally{$sha.Dispose()}
}
[pscustomobject]@{Status=[string]$signature.Status;Message=[string]$signature.StatusMessage;Subject=$subject;CertificateSha256=$pin;Timestamped=($null -ne $signature.TimeStamperCertificate)} | ConvertTo-Json -Compress
