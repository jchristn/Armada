@echo off
dotnet tool uninstall --global Armada.Helm
dotnet build Armada.sln
dotnet pack Armada.Helm -o ./nupkg
dotnet tool install --global --add-source ./nupkg Armada.Helm
