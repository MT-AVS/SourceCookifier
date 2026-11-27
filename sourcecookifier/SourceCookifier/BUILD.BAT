@ECHO OFF

IF "%SETUP_ENV_DONE%" == "" CALL ..\SETUP_ENV.BAT

IF "%FIND_IN_SESSION%" == "1" (
  MSBUILD /restore /target:Rebuild /Property:Configuration=Debug;DefineConstants="TRACE_ALL;FIND_IN_SESSION" SourceCookifier_%BUILD_ENV_NET%.sln
  IF ERRORLEVEL 1 EXIT /B 1
  MOVE /Y ..\..\Notepad++\plugins\SourceCookifier\SourceCookifier.dll ..\..\Notepad++\plugins\SourceCookifier\bin\SourceCookifier.Debug.Build.dll
  IF ERRORLEVEL 1 EXIT /B 1
  MSBUILD /target:Rebuild /Property:Configuration=Release;DefineConstants=FIND_IN_SESSION;PostBuildEvent="" SourceCookifier_%BUILD_ENV_NET%.sln
  IF ERRORLEVEL 1 EXIT /B 1
) ELSE (
  MSBUILD /restore /target:Rebuild /Property:Configuration=Debug;DefineConstants=TRACE_ALL SourceCookifier_%BUILD_ENV_NET%.sln
  IF ERRORLEVEL 1 EXIT /B 1
  MOVE /Y ..\..\Notepad++\plugins\SourceCookifier\SourceCookifier.dll ..\..\Notepad++\plugins\SourceCookifier\bin\SourceCookifier.Debug.Build.dll
  IF ERRORLEVEL 1 EXIT /B 1
  MSBUILD /target:Rebuild /Property:Configuration=Release;PostBuildEvent="" SourceCookifier_%BUILD_ENV_NET%.sln
  IF ERRORLEVEL 1 EXIT /B 1
)
