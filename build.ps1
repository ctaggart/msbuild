# the version in progress, used by pre-release builds
$version = '14.1.0'

$exit = 0
trap [Exception] {
    $exit++
    Write-Warning $_.Exception.Message
    continue
}

$pst = [TimeZoneInfo]::FindSystemTimeZoneById('Pacific Standard Time')
$date = New-Object DateTimeOffset([TimeZoneInfo]::ConvertTimeFromUtc([DateTime]::UtcNow, $pst), $pst.BaseUtcOffset)
$v = $version + '-a' + $date.ToString('yyMMddHHmm')

if ($env:appveyor){
	$v = $version + '-b' + [int]::Parse($env:appveyor_build_number).ToString('000')

    # remove Visual Studio 2010 entries
    # https://github.com/ctaggart/msbuild/issues/1
    Remove-Item -Path 'HKLM:Software\Wow6432Node\Microsoft\DevDiv\vs\Servicing\10.0' -Recurse

    # add environment variables needed by build.cmd
    choco install pscx -y
    Import-Module 'C:\Program Files (x86)\PowerShell Community Extensions\Pscx3\Pscx\Pscx.psd1'

    # the VS CTP 6 is missing the AppVeyor logging extensions in the VS 2015 directory
    copy 'C:\Program Files (x86)\Microsoft Visual Studio 12.0\Common7\IDE\CommonExtensions\Microsoft\TestWindow\Extensions\AppVeyor*' `
    'C:\Program Files (x86)\Microsoft Visual Studio 14.0\Common7\IDE\CommonExtensions\Microsoft\TestWindow\Extensions'

    # install SourceLink.exe
    choco install SourceLink -y
}

echo '--> build'
Invoke-BatchFile "$env:vs140comntools\vsvars32.bat"
.\build.cmd /p:Configuration=Release /t:Rebuild
#msbuild .\src\MSBuild.sln /p:Configuration=Release /t:Rebuild /v:m

echo '--> source index'
$u = 'https://raw.githubusercontent.com/ctaggart/msbuild/{0}/%var2%'
$pp = 'Configuration Release'
$sl = "SourceLink index -u '$u' -pp $pp -pr"
iex "$sl src\XMakeBuildEngine\Microsoft.Build.csproj"
iex "$sl src\Framework\Microsoft.Build.Framework.csproj"
iex "$sl src\Utilities\Microsoft.Build.Utilities.csproj"

echo '--> run tests'
$vstest = 'vstest.console'
if ($env:appveyor) { $vstest += ' /logger:Appveyor' }
$env:path = "C:\Program Files (x86)\Microsoft Visual Studio 14.0\Common7\IDE\CommonExtensions\Microsoft\TestWindow;$env:path"
echo 'CommandLine'
iex "$vstest bin\Windows_NT\Release\Microsoft.Build.CommandLine.UnitTests.dll > bin\CommandLine.log"
gc .\bin\CommandLine.log | select -Last 3
echo 'Engine'
iex "$vstest bin\Windows_NT\Release\Microsoft.Build.Engine.UnitTests.dll > bin\Engine.log"
gc .\bin\Engine.log | select -Last 3
echo 'Framework'
iex "$vstest bin\Windows_NT\Release\Microsoft.Build.Framework.UnitTests.dll > bin\Framework.log"
gc .\bin\Framework.log | select -Last 3
echo 'Tasks'
iex "$vstest bin\Windows_NT\Release\Microsoft.Build.Tasks.UnitTests.dll > bin\Tasks.log"
gc .\bin\Tasks.log | select -Last 3
echo 'Utilities'
iex "$vstest bin\Windows_NT\Release\Microsoft.Build.Utilities.UnitTests.dll > bin\Utilities.log"
gc .\bin\Utilities.log | select -Last 3

echo '--> create nuget package'
if ($env:appveyor_repo_tag -eq 'true' -and $env:appveyor_repo_tag_name.StartsWith('v')){
    $v = $env:appveyor_repo_tag_name.Substring(1)
}
.\paket.bootstrapper.exe
.\paket.exe pack output bin version $v
echo "created $pwd\bin\SourceLink.MSBuild.$v.nupkg"

exit $exit