set windows-shell := ["powershell.exe", "-NoProfile", "-Command"]

default:
    @just --list

# Build the solution
build:
    dotnet build

# Clean build artifacts
clean:
    dotnet clean

# Run unit and integration tests
test:
    dotnet test

# Run tests and collect code coverage
coverage:
    dotnet test --collect:"XPlat Code Coverage"

# Run the MinniStore server
run port="25000" db="minni.db":
    dotnet run --project src/MinniStore/MinniStore.csproj -- --port {{port}} --db {{db}}
