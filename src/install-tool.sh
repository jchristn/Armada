#!/usr/bin/env bash
dotnet build Armada.sln
dotnet pack Armada.Helm -o ./nupkg
dotnet tool install --global --add-source ./nupkg Armada.Helm
