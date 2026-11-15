#!/bin/bash
# Builds CloveStrap.exe into ../out
cd "$(dirname "$0")/src"
mkdir -p ../out
CSC=/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe
$CSC -nologo -target:winexe -optimize+ -win32icon:clovestrap.ico \
  -r:System.IO.Compression.dll -r:System.IO.Compression.FileSystem.dll -r:System.Web.Extensions.dll \
  -r:System.Security.dll -r:System.Management.dll -r:System.Core.dll \
  -resource:brand/logo.png,CloveStrap.logo.png \
  -resource:brand/helpers/natsuki.png,CloveStrap.helper.natsuki.png \
  -resource:brand/helpers/clove.png,CloveStrap.helper.clove.png \
  -resource:brand/helpers/dio.png,CloveStrap.helper.dio.png \
  -resource:brand/helpers/mikasa.png,CloveStrap.helper.mikasa.png \
  -resource:brand/helpers/asuna.png,CloveStrap.helper.asuna.png \
  -resource:characters/clove.png,CloveStrap.Characters.clove.png \
  -resource:characters/natsuki.png,CloveStrap.Characters.natsuki.png \
  -resource:characters/mikasa.png,CloveStrap.Characters.mikasa.png \
  -resource:characters/dio.png,CloveStrap.Characters.dio.png \
  -resource:characters/asuna.png,CloveStrap.Characters.asuna.png \
  -out:../out/CloveStrap.exe *.cs > ../out/build.log 2>&1
status=$?
grep -v "^This compiler\|^$\|CS0169\|CS0414\|^Microsoft\|^for C#\|^Copyright" ../out/build.log
rm -f ../out/build.log
exit $status
