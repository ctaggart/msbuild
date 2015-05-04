if ($env:appveyor){
    # remove Visual Studio 2010 entries
    # https://github.com/ctaggart/msbuild/issues/1
    Remove-Item -Path 'HKLM:Software\Wow6432Node\Microsoft\DevDiv\vs\Servicing\10.0' -Recurse

    # add environment variables needed by build.cmd
    #choco install pscx -y
    #Import-Module 'C:\Program Files (x86)\PowerShell Community Extensions\Pscx3\Pscx\Pscx.psd1'
    #Invoke-BatchFile "$env:vs140comntools\vsvars32.bat"

    # unit tests
    # set test assemblies to: bin\Windows_NT\Debug\*.UnitTests.dll
    # the VS CTP 6 is missing the AppVeyor logging extensions in the VS 2015 directory
    copy 'C:\Program Files (x86)\Microsoft Visual Studio 12.0\Common7\IDE\CommonExtensions\Microsoft\TestWindow\Extensions\AppVeyor*' `
    'C:\Program Files (x86)\Microsoft Visual Studio 14.0\Common7\IDE\CommonExtensions\Microsoft\TestWindow\Extensions'

    # install SourceLink.exe
    choco install SourceLink -y
}

#.\build.cmd
msbuild .\src\MSBuild.sln /p:Configuration=Release /v:m

SourceLink index `
-pr src\XMakeBuildEngine\Microsoft.Build.csproj `
-pp Configuration Release `
-u 'https://raw.githubusercontent.com/Microsoft/msbuild/{0}/%var2%'

.\paket.bootstrapper.exe
if ($env:appveyor_repo_tag -eq 'true' -and $env:appveyor_repo_tag_name.StartsWith('v')){
    $v = $env:appveyor_repo_tag_name.Substring(1)
    .\paket.exe pack output bin version $v
} else {
    $v = '0.0.0'
    .\paket.exe pack output bin version $v
}
