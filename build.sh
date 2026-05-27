#!/usr/bin/env bash

set -e

>&2 echo `date` "- [build.sh]: INFO:\tRestoring packages."
dotnet restore ./$CI_PROJECT_NAME.sln -maxCpuCount:8 --configfile tempnuget.config
>&2 echo `date` "- [build.sh]: INFO:\tBuild sources."
dotnet publish ./$CI_PROJECT_NAME.sln -c Release -o ./$CI_PROJECT_NAME/obj/Docker/publish -maxCpuCount:8 -restore:False
