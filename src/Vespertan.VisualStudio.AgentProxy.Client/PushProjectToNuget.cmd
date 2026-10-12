@echo off

setlocal

:: General
set useDefaultPrompt=Yes
set deletePackageAfterPush=Yes

:: Increment project version
set incrementProjectVersion_Allow=No
set incrementProjectVersion_Part=2

:: Local Nuget feed configuration
set localNugetSrc_AllowPush=Yes
set localNugetSrc_Dir=C:\NugetLocalFeed

:: Nuget source for Vespertan
set nugetSrc_AllowPush=Yes
if /I [%nugetSrc_AllowPush:~0,1%] == [y] if defined VespertanApiKey (set nugetSrc_AllowPush=Yes) else (set nugetSrc_AllowPush=No)
set nugetSrc_Uri=https://api.nuget.org/v3/index.json
set nugetSrc_ApiKey=%VespertanApiKey%

:: Nuget source for Omega
set nugetSrc2_AllowPush=Yes
if /I [%nugetSrc2_AllowPush:~0,1%] == [y] if defined OmegaApiKey (set nugetSrc2_AllowPush=Yes) else (set nugetSrc2_AllowPush=No)
set nugetSrc2_Uri=https://nuget.om.pl:5443/nugetserver/feeds/vespertan/v3/index.json
set nugetSrc2_ApiKey=%OmegaApiKey%

call NugetBase.cmd

endlocal