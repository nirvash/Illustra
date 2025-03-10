@echo off
setlocal enabledelayedexpansion

set "resxFile=%1"
set "xamlFile=%2"

echo ^<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" > "%xamlFile%"
echo xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" >> "%xamlFile%"
echo xmlns:sys="clr-namespace:System;assembly=mscorlib"^> >> "%xamlFile%"

for /f "tokens=*" %%a in ('type "%resxFile%" ^| findstr /r /c:"<data name="') do (
    set "line=%%a"
    set "name=!line:~11,-2!"
    set "name=!name:"=!"
    set "name=!name: =!"
    for /f "tokens=*" %%b in ('type "%resxFile%" ^| findstr /r /c:"<value>"') do (
        set "value=%%b"
        set "value=!value:~7,-8!"
        echo     ^<sys:String x:Key="!name!"^>!value!^</sys:String^> >> "%xamlFile%"
    )
)

echo ^</ResourceDictionary^> >> "%xamlFile%"
endlocal
