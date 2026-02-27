param(
    [string]$version = "0.1.0"
)

# build and produce nupkg (will appear in ./nupkgs)
dotnet pack -c Release 
# remove previous global install if exists
dotnet tool uninstall --global lattice
# install from local folder (tool manifest not required for global install)
dotnet tool install --global --add-source ./build/ --version $version lattice