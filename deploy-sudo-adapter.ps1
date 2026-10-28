# Loaded after the platform operator functions for this local app deployment.
# The password stays in the launching PowerShell process and is sent to sudo
# through SSH stdin. It is never placed in arguments, environment, or files.
function Invoke-Checked {
    param([string]$Command, [string[]]$Arguments)
    $isSudoSsh = $Command -eq 'ssh.exe' -and $Arguments.Count -gt 1 -and $Arguments[-1] -match '^sudo '
    if (-not $isSudoSsh) {
        & $Command @Arguments
        if ($LASTEXITCODE -ne 0) { throw "$Command failed (exit $LASTEXITCODE)." }
        return
    }
    if (-not $global:SalesDeploySudoPassword) { throw 'sudo-Passwort fehlt.' }
    $commandLine = @($Arguments | Where-Object { $_ -cne '-t' })
    $commandLine[-1] = $commandLine[-1] -creplace '^sudo ', "sudo -S -p '' "
    $start = [Diagnostics.ProcessStartInfo]::new()
    $start.FileName = (Get-Command ssh.exe).Source
    $start.UseShellExecute = $false
    $start.RedirectStandardInput = $true
    foreach ($argument in $commandLine) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $bstr = [IntPtr]::Zero
    $plain = $null
    try {
        if (-not $process.Start()) { throw 'SSH-Prozess konnte nicht gestartet werden.' }
        $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($global:SalesDeploySudoPassword)
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
        $process.StandardInput.WriteLine($plain)
        $process.StandardInput.Close()
        $plain = $null
        if (-not $process.WaitForExit(600000)) { $process.Kill($true); throw 'sudo-SSH-Aufruf hat das Zeitlimit ueberschritten.' }
        if ($process.ExitCode -ne 0) { throw "sudo-SSH-Aufruf fehlgeschlagen (Exitcode $($process.ExitCode))." }
    } finally {
        $plain = $null
        if ($bstr -ne [IntPtr]::Zero) { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
        if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
}
