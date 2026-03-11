#!/usr/bin/env bash
dotnet build src/Armada.sln
dotnet pack src/Armada.Helm -o ./src/nupkg
dotnet tool install --global --add-source ./src/nupkg Armada.Helm
